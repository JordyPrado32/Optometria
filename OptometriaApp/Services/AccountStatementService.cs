using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class AccountStatementService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;
    private readonly EmailSender emailSender;

    public AccountStatementService(IDbContextFactory<OpticaDbContext> dbContextFactory, EmailSender emailSender)
    {
        this.dbContextFactory = dbContextFactory;
        this.emailSender = emailSender;
    }

    public async Task<AccountStatementDocument?> BuildAsync(int accountId, int currentUserId, int currentRoleId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var header = await (
            from account in dbContext.tbl_cta_cobrar.AsNoTracking()
            join client in dbContext.clients.AsNoTracking() on account.id_cliente equals client.cliente_id
            join sale in dbContext.tbl_venta.AsNoTracking() on account.id_venta equals sale.id_venta into sales
            from sale in sales.DefaultIfEmpty()
            join patient in dbContext.tbl_pacientes.AsNoTracking() on sale.id_paciente equals patient.id_paciente into patients
            from patient in patients.DefaultIfEmpty()
            join comprobante in dbContext.tbl_comprobantes.AsNoTracking() on account.id_comprobante equals comprobante.id_comprobante into comprobantes
            from comprobante in comprobantes.DefaultIfEmpty()
            where account.id_cta_cobrar == accountId
               && (currentRoleId == 1 || client.id_usuario_creacion == currentUserId)
            select new
            {
                AccountId = account.id_cta_cobrar,
                account.id_cliente,
                account.id_venta,
                account.monto_total,
                account.saldo,
                account.fecha_emision,
                account.fecha_vencimiento,
                AccountState = account.estado,
                ClientBusinessName = client.razon_social,
                ClientFirstName = client.nombres,
                ClientLastName = client.apellidos,
                client.numero_identificacion,
                client.correo_electronico,
                client.contacto_correo,
                InvoiceNumber = comprobante.numero_comprobante,
                sale.dias_credito,
                sale.valor_cobrado,
                PatientFirstName = patient != null ? patient.nombres : string.Empty,
                PatientLastName = patient != null ? patient.apellidos : string.Empty
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var abonos = await (
            from abono in dbContext.tbl_abonos.AsNoTracking()
            join method in dbContext.tbl_metodo_pagos.AsNoTracking() on abono.metodo_pago_id equals method.id_metodo_pago into methods
            from method in methods.DefaultIfEmpty()
            where abono.id_cta_cobrar == accountId
            orderby abono.fecha_abono, abono.id_abono
            select new
            {
                Date = abono.fecha_abono ?? abono.fecha_registro ?? DateTime.Now,
                Amount = abono.monto_abono,
                Description = string.Equals(abono.tipo_movimiento, "Reversion", StringComparison.OrdinalIgnoreCase)
                    ? (method != null ? $"Reversion de abono - {method.nombre}" : "Reversion de abono")
                    : (method != null ? $"Abono registrado - {method.nombre}" : "Abono registrado"),
                Reference = string.IsNullOrWhiteSpace(abono.motivo_movimiento)
                    ? (string.IsNullOrWhiteSpace(abono.referencia_pago) ? "Sin referencia" : abono.referencia_pago!)
                    : abono.motivo_movimiento!,
                UserLabel = string.IsNullOrWhiteSpace(abono.usuario_registro) ? "Sistema" : abono.usuario_registro!
            })
            .ToListAsync(cancellationToken);

        var creditNotes = await (
            from note in dbContext.tbl_nota_credito.AsNoTracking()
            where note.id_cta_cobrar == accountId && note.estado == "Emitida"
            orderby note.fecha_emision, note.id_nota_credito
            select new
            {
                Date = note.fecha_emision,
                Amount = note.monto_total,
                Description = $"Nota de credito {note.numero_nota}",
                Reference = string.IsNullOrWhiteSpace(note.motivo) ? "Sin motivo" : note.motivo!,
                UserLabel = string.IsNullOrWhiteSpace(note.usuario_creacion) ? "Sistema" : note.usuario_creacion!
            })
            .ToListAsync(cancellationToken);

        var invoiceLines = await (
            from line in dbContext.tbl_detalle_venta.AsNoTracking()
            join product in dbContext.tbl_productos.AsNoTracking() on line.id_producto equals product.id_producto
            where line.id_venta == header.id_venta
            orderby line.id_detalle_venta
            select new AccountStatementLine
            {
                ProductCode = product.codigo_producto,
                Concept = string.IsNullOrWhiteSpace(line.concepto_item) ? product.nombre_producto : line.concepto_item!,
                Quantity = line.cantidad,
                UnitPrice = line.precio_unitario ?? 0m,
                Discount = line.descuento ?? 0m,
                Total = line.total_item ?? 0m,
                TaxLabel = (product.tiene_iva ?? false) ? $"Si, {(product.porcentaje_iva ?? 0m):0.##}%" : "No"
            })
            .ToListAsync(cancellationToken);

        var totalCredited = creditNotes.Sum(x => x.Amount);
        var originalInvoiceAmount = header.monto_total + totalCredited;
        var movements = new List<AccountStatementMovement>
        {
            new()
            {
                MovementDate = header.fecha_emision,
                Type = "Factura",
                Description = $"Emision de factura {(string.IsNullOrWhiteSpace(header.InvoiceNumber) ? $"Venta #{header.id_venta}" : header.InvoiceNumber)}",
                Reference = $"Credito {Math.Max(0, header.dias_credito ?? 0)} dias",
                Debit = originalInvoiceAmount,
                Credit = 0m,
                UserLabel = "Sistema"
            }
        };

        movements.AddRange(creditNotes.Select(note => new AccountStatementMovement
        {
            MovementDate = note.Date,
            Type = "NotaCredito",
            Description = note.Description,
            Reference = note.Reference,
            Debit = 0m,
            Credit = note.Amount,
            UserLabel = note.UserLabel
        }));

        movements.AddRange(abonos.Select(abono => new AccountStatementMovement
        {
            MovementDate = abono.Date,
            Type = abono.Amount < 0 ? "Reversion" : "Abono",
            Description = abono.Description,
            Reference = abono.Reference,
            Debit = abono.Amount < 0 ? Math.Abs(abono.Amount) : 0m,
            Credit = abono.Amount < 0 ? 0m : abono.Amount,
            UserLabel = abono.UserLabel
        }));

        var orderedMovements = movements
            .OrderBy(x => x.MovementDate)
            .ThenBy(x => x.Type == "Factura" ? 0 : 1)
            .ToList();

        var runningBalance = 0m;
        foreach (var movement in orderedMovements)
        {
            runningBalance += movement.Debit;
            runningBalance -= movement.Credit;
            movement.Balance = runningBalance;
        }

        return new AccountStatementDocument
        {
            AccountId = header.AccountId,
            ClientId = header.id_cliente,
            SaleId = header.id_venta ?? 0,
            InvoiceNumber = string.IsNullOrWhiteSpace(header.InvoiceNumber) ? $"Venta #{header.id_venta}" : header.InvoiceNumber!,
            ClientName = ResolveClientName(header.ClientBusinessName, header.ClientFirstName, header.ClientLastName),
            ClientDocument = header.numero_identificacion ?? string.Empty,
            ClientEmail = string.IsNullOrWhiteSpace(header.contacto_correo) ? (header.correo_electronico ?? string.Empty) : header.contacto_correo!,
            PatientName = ResolvePersonName(header.PatientFirstName, header.PatientLastName, "Paciente no asociado"),
            IssueDate = header.fecha_emision,
            DueDate = header.fecha_vencimiento,
            CreditDays = Math.Max(0, header.dias_credito ?? 0),
            State = header.AccountState ?? "Pendiente",
            OriginalInvoiceAmount = originalInvoiceAmount,
            CurrentTotalAmount = header.monto_total,
            Balance = header.saldo,
            TotalPaid = Math.Max(0m, abonos.Sum(x => x.Amount)),
            TotalCredited = totalCredited,
            Movements = orderedMovements,
            Lines = invoiceLines
        };
    }

    public string BuildPrintableHtml(AccountStatementDocument statement)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8" />
<title>Estado de cuenta</title>
<style>
body { font-family: Arial, sans-serif; color: #1f1f1f; margin: 24px; }
h1, h2, h3 { margin: 0 0 10px; }
.eyebrow { font-size: 12px; text-transform: uppercase; letter-spacing: 0.12em; color: #6d6258; }
.hero { display:flex; justify-content:space-between; gap:16px; margin-bottom:20px; border-bottom:1px solid #d8d1c8; padding-bottom:16px; }
.card { border:1px solid #d8d1c8; border-radius:16px; padding:16px; margin-bottom:16px; }
.grid { display:grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap:12px; }
.summary { display:grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap:12px; margin-bottom:16px; }
.summary .item { border:1px solid #e6e0d8; border-radius:14px; padding:14px; background:#fcfaf8; }
.summary .item strong { display:block; font-size:22px; margin-top:4px; }
.meta label { display:block; font-size:12px; color:#6d6258; margin-bottom:4px; }
.meta strong, .meta span { display:block; }
table { width:100%; border-collapse: collapse; }
th, td { text-align:left; padding:8px; border-bottom:1px solid #e6e0d8; font-size:12px; vertical-align:top; }
th { text-transform:uppercase; letter-spacing:0.05em; color:#6d6258; font-size:11px; }
.totals { display:grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap:12px; }
.totals .item { border:1px solid #e6e0d8; border-radius:12px; padding:12px; }
.totals strong { font-size:20px; display:block; margin-top:4px; }
.printbar { margin-bottom:16px; }
@media print { .printbar { display:none; } body { margin: 8mm; } }
</style>
</head>
<body>
<div class="printbar"><button onclick="window.print()">Imprimir</button></div>
""");

        sb.Append($"""
<div class="hero">
  <div>
    <div class="eyebrow">Cuentas por cobrar</div>
    <h1>Estado de cuenta</h1>
    <p>Factura {EscapeHtml(statement.InvoiceNumber)}</p>
  </div>
  <div style="text-align:right;">
    <div class="eyebrow">Fecha de emision del reporte</div>
    <strong>{statement.GeneratedAt:yyyy-MM-dd HH:mm}</strong>
  </div>
</div>
""");

        sb.Append($"""
<div class="summary">
  <div class="item"><span class="eyebrow">Estado</span><strong>{EscapeHtml(statement.StatusCaption)}</strong><span>{EscapeHtml(statement.AgingBucket)}</span></div>
  <div class="item"><span class="eyebrow">Saldo actual</span><strong>{statement.Balance:0.00}</strong><span>{statement.DaysPastDueCaption}</span></div>
  <div class="item"><span class="eyebrow">Cobertura</span><strong>{statement.CollectionProgressPercent:0}%</strong><span>{statement.TotalPaid:0.00} abonado</span></div>
</div>
""");

        sb.Append($"""
<div class="card">
  <h2>Resumen del cliente</h2>
  <div class="grid">
    <div class="meta"><label>Cliente</label><strong>{EscapeHtml(statement.ClientName)}</strong></div>
    <div class="meta"><label>Documento</label><span>{EscapeHtml(statement.ClientDocument)}</span></div>
    <div class="meta"><label>Correo</label><span>{EscapeHtml(statement.ClientEmail)}</span></div>
    <div class="meta"><label>Paciente</label><span>{EscapeHtml(statement.PatientName)}</span></div>
    <div class="meta"><label>Emision factura</label><span>{statement.IssueDate:yyyy-MM-dd HH:mm}</span></div>
    <div class="meta"><label>Vencimiento</label><span>{(statement.DueDate?.ToString("yyyy-MM-dd") ?? "-")}</span></div>
  </div>
</div>
""");

        sb.Append($"""
<div class="card">
  <h2>Totales</h2>
  <div class="totals">
    <div class="item"><span class="eyebrow">Factura original</span><strong>{statement.OriginalInvoiceAmount:0.00}</strong></div>
    <div class="item"><span class="eyebrow">Notas credito</span><strong>{statement.TotalCredited:0.00}</strong></div>
    <div class="item"><span class="eyebrow">Abonos</span><strong>{statement.TotalPaid:0.00}</strong></div>
    <div class="item"><span class="eyebrow">Saldo actual</span><strong>{statement.Balance:0.00}</strong></div>
  </div>
</div>
""");

        sb.Append("""
<div class="card">
  <h2>Movimientos</h2>
  <table>
    <thead>
      <tr><th>Fecha</th><th>Tipo</th><th>Detalle</th><th>Debito</th><th>Credito</th><th>Saldo</th></tr>
    </thead>
    <tbody>
""");

        foreach (var movement in statement.Movements)
        {
            sb.Append($"""
<tr>
  <td>{movement.MovementDate:yyyy-MM-dd HH:mm}</td>
  <td>{EscapeHtml(movement.TypeLabel)}</td>
  <td>{EscapeHtml(movement.Description)}<br /><span style="color:#6d6258;">{EscapeHtml(movement.Reference)} - {EscapeHtml(movement.UserLabel)}</span></td>
  <td>{movement.Debit:0.00}</td>
  <td>{movement.Credit:0.00}</td>
  <td>{movement.Balance:0.00}</td>
</tr>
""");
        }

        sb.Append("""
    </tbody>
  </table>
</div>
<div class="card">
  <h2>Detalle facturado</h2>
  <table>
    <thead>
      <tr><th>Codigo</th><th>Concepto</th><th>IVA</th><th>Cantidad</th><th>Precio</th><th>Descuento</th><th>Total</th></tr>
    </thead>
    <tbody>
""");

        foreach (var line in statement.Lines)
        {
            sb.Append($"""
<tr>
  <td>{EscapeHtml(line.ProductCode)}</td>
  <td>{EscapeHtml(line.Concept)}</td>
  <td>{EscapeHtml(line.TaxLabel)}</td>
  <td>{line.Quantity}</td>
  <td>{line.UnitPrice:0.00}</td>
  <td>{line.Discount:0.00}</td>
  <td>{line.Total:0.00}</td>
</tr>
""");
        }

        sb.Append("""
    </tbody>
  </table>
</div>
</body>
</html>
""");

        return sb.ToString();
    }

    public byte[] BuildPdf(AccountStatementDocument statement)
    {
        var lines = BuildPdfLines(statement);
        return SimplePdfBuilder.Build(lines);
    }

    public async Task SendStatementEmailAsync(AccountStatementDocument statement, string recipientEmail, CancellationToken cancellationToken = default)
    {
        var pdfBytes = BuildPdf(statement);
        var subject = $"Estado de cuenta - {statement.InvoiceNumber}";
        var body = $"""
Hola {statement.ClientName},

Adjuntamos el estado de cuenta correspondiente a la factura {statement.InvoiceNumber}.

Saldo actual: {statement.Balance.ToString("0.00", CultureInfo.InvariantCulture)}
Notas de credito: {statement.TotalCredited.ToString("0.00", CultureInfo.InvariantCulture)}
Abonos registrados: {statement.TotalPaid.ToString("0.00", CultureInfo.InvariantCulture)}
Estado: {statement.StatusCaption}
Tramo: {statement.AgingBucket}
Condicion: {statement.DaysPastDueCaption}

Este documento fue generado automaticamente desde el modulo de cartera.
""";

        await emailSender.SendAccountStatementAsync(
            recipientEmail,
            statement.ClientName,
            subject,
            body,
            $"{SanitizeFileName(statement.InvoiceNumber)}-estado-cuenta.pdf",
            pdfBytes,
            cancellationToken);
    }

    private static List<string> BuildPdfLines(AccountStatementDocument statement)
    {
        var lines = new List<string>
        {
            "ESTADO DE CUENTA",
            "",
            $"Cliente: {statement.ClientName}",
            $"Documento: {statement.ClientDocument}",
            $"Correo: {statement.ClientEmail}",
            $"Paciente: {statement.PatientName}",
            $"Factura: {statement.InvoiceNumber}",
            $"Emision: {statement.IssueDate:yyyy-MM-dd HH:mm}",
            $"Vencimiento: {(statement.DueDate?.ToString("yyyy-MM-dd") ?? "-")}",
            $"Dias de credito: {statement.CreditDays}",
            $"Estado cartera: {statement.StatusCaption}",
            $"Tramo aging: {statement.AgingBucket}",
            $"Condicion: {statement.DaysPastDueCaption}",
            "",
            $"Factura original: {statement.OriginalInvoiceAmount:0.00}",
            $"Notas de credito: {statement.TotalCredited:0.00}",
            $"Abonos: {statement.TotalPaid:0.00}",
            $"Saldo actual: {statement.Balance:0.00}",
            $"Cobertura: {statement.CollectionProgressPercent:0}%",
            "",
            "MOVIMIENTOS"
        };

        lines.Add("Fecha              Tipo           Debito       Credito      Saldo");
        lines.Add("----------------------------------------------------------------");
        foreach (var movement in statement.Movements)
        {
            lines.Add($"{movement.MovementDate:yyyy-MM-dd HH:mm}  {PadRight(movement.TypeLabel, 13)} {movement.Debit,10:0.00}  {movement.Credit,10:0.00}  {movement.Balance,10:0.00}");
            lines.AddRange(Wrap($"  {movement.Description} | {movement.Reference} | {movement.UserLabel}", 95));
        }

        lines.Add("");
        lines.Add("DETALLE FACTURADO");
        lines.Add("Codigo        Concepto                                   Cant  Precio     Desc      Total");
        lines.Add("-------------------------------------------------------------------------------------------");
        foreach (var line in statement.Lines)
        {
            var concept = line.Concept.Length > 40 ? line.Concept[..40] : line.Concept;
            lines.Add($"{PadRight(line.ProductCode, 12)}{PadRight(concept, 43)}{line.Quantity,4}  {line.UnitPrice,8:0.00}  {line.Discount,8:0.00}  {line.Total,8:0.00}");
        }

        return lines;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var remaining = text.Trim();
        while (remaining.Length > width)
        {
            var splitIndex = remaining.LastIndexOf(' ', width);
            if (splitIndex <= 0)
            {
                splitIndex = width;
            }

            yield return remaining[..splitIndex];
            remaining = remaining[splitIndex..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static string ResolveClientName(string? businessName, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(businessName))
        {
            return businessName.Trim();
        }

        return ResolvePersonName(firstName, lastName, "Cliente sin nombre");
    }

    private static string ResolvePersonName(string? firstName, string? lastName, string fallback)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? fallback : fullName;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string EscapeHtml(string? value)
    {
        return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string PadRight(string? value, int totalWidth)
    {
        var safeValue = value ?? string.Empty;
        return safeValue.Length >= totalWidth
            ? safeValue[..totalWidth]
            : safeValue.PadRight(totalWidth);
    }

    private static class SimplePdfBuilder
    {
        public static byte[] Build(IReadOnlyList<string> lines)
        {
            const int linesPerPage = 46;
            var pages = new List<List<string>>();
            for (var i = 0; i < lines.Count; i += linesPerPage)
            {
                pages.Add(lines.Skip(i).Take(linesPerPage).ToList());
            }

            var objects = new List<string>();
            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");

            var pageObjectIds = new List<int>();
            var contentObjectIds = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                pageObjectIds.Add(objects.Count + 2);
                contentObjectIds.Add(objects.Count + 3);
                objects.Add(string.Empty);
                objects.Add(string.Empty);
            }

            objects.Insert(1, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] >>");

            for (var index = 0; index < pages.Count; index++)
            {
                var pageId = pageObjectIds[index];
                var contentId = contentObjectIds[index];
                objects[pageId - 1] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents {contentId} 0 R >>";

                var content = BuildPageContent(pages[index], index + 1, pages.Count);
                objects[contentId - 1] = $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream";
            }

            var builder = new StringBuilder();
            builder.Append("%PDF-1.4\n");

            var offsets = new List<int> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
                builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xrefPosition = Encoding.ASCII.GetByteCount(builder.ToString());
            builder.Append($"xref\n0 {objects.Count + 1}\n");
            builder.Append("0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                builder.Append($"{offset:D10} 00000 n \n");
            }

            builder.Append($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
            return Encoding.ASCII.GetBytes(builder.ToString());
        }

        private static string BuildPageContent(IReadOnlyList<string> pageLines, int pageNumber, int pageCount)
        {
            var sb = new StringBuilder();
            sb.Append("BT\n/F1 10 Tf\n50 760 Td\n");
            var currentY = 760;

            foreach (var line in pageLines)
            {
                sb.Append($"1 0 0 1 50 {currentY} Tm ({EscapePdf(line)}) Tj\n");
                currentY -= 15;
            }

            sb.Append($"1 0 0 1 50 30 Tm (Pagina {pageNumber} de {pageCount}) Tj\nET");
            return sb.ToString();
        }

        private static string EscapePdf(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);
        }
    }
}

public sealed class AccountStatementDocument
{
    public int AccountId { get; set; }
    public int ClientId { get; set; }
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientDocument { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int CreditDays { get; set; }
    public string State { get; set; } = "Pendiente";
    public decimal OriginalInvoiceAmount { get; set; }
    public decimal CurrentTotalAmount { get; set; }
    public decimal Balance { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalCredited { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<AccountStatementMovement> Movements { get; set; } = [];
    public List<AccountStatementLine> Lines { get; set; } = [];
    public int DaysPastDue => DueDate.HasValue ? Math.Max(0, DateOnly.FromDateTime(DateTime.Today).DayNumber - DueDate.Value.DayNumber) : 0;
    public string AgingBucket => Balance <= 0
        ? "Sin saldo"
        : !DueDate.HasValue || DueDate.Value > DateOnly.FromDateTime(DateTime.Today)
            ? "Por vencer"
            : DaysPastDue <= 30
                ? "0-30 dias"
                : DaysPastDue <= 60
                    ? "31-60 dias"
                    : DaysPastDue <= 90
                        ? "61-90 dias"
                        : "90+ dias";
    public string StatusCaption => Balance <= 0
        ? "Cuenta saldada"
        : !DueDate.HasValue
            ? "Pendiente sin fecha"
            : DueDate.Value < DateOnly.FromDateTime(DateTime.Today)
                ? "Vencida"
                : DueDate.Value == DateOnly.FromDateTime(DateTime.Today)
                    ? "Vence hoy"
                    : "Al dia";
    public string DaysPastDueCaption => Balance <= 0
        ? "Sin saldo pendiente"
        : !DueDate.HasValue
            ? "No registra fecha de vencimiento"
            : DueDate.Value < DateOnly.FromDateTime(DateTime.Today)
                ? $"Vencido hace {DaysPastDue} dias"
                : DueDate.Value == DateOnly.FromDateTime(DateTime.Today)
                    ? "Vence hoy"
                    : $"Vence en {DueDate.Value.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber} dias";
    public decimal CollectionProgressPercent => OriginalInvoiceAmount <= 0 ? 0 : Math.Min(100m, Math.Round((TotalPaid / OriginalInvoiceAmount) * 100m, 2));
}

public sealed class AccountStatementMovement
{
    public DateTime MovementDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string UserLabel { get; set; } = "Sistema";
    public string TypeLabel => Type switch
    {
        "Factura" => "Factura",
        "Abono" => "Abono",
        "Reversion" => "Reversion",
        "NotaCredito" => "Nota credito",
        _ => Type
    };
}

public sealed class AccountStatementLine
{
    public string ProductCode { get; set; } = string.Empty;
    public string Concept { get; set; } = string.Empty;
    public string TaxLabel { get; set; } = "No";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
}
