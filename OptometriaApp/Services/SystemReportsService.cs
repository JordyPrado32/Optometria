using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class SystemReportsService
{
    public async Task<SystemReportSnapshot> BuildAsync(
        OpticaDbContext dbContext,
        SystemReportFilters filters,
        CancellationToken cancellationToken = default)
    {
        var startDate = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDate = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var salesQuery = dbContext.tbl_venta
            .AsNoTracking()
            .Include(x => x.id_usuarioNavigation)
            .Include(x => x.tbl_detalle_venta)
                .ThenInclude(x => x.id_productoNavigation)
                    .ThenInclude(x => x.id_categoriaNavigation)
            .Where(x => x.fecha_venta.HasValue && x.fecha_venta.Value >= startDate && x.fecha_venta.Value <= endDate)
            .AsQueryable();

        if (filters.UserId > 0)
        {
            salesQuery = salesQuery.Where(x => x.id_usuario == filters.UserId);
        }

        var sales = await salesQuery.ToListAsync(cancellationToken);

        var appointmentQuery = dbContext.tbl_citas
            .AsNoTracking()
            .Include(x => x.id_pacienteNavigation)
            .Include(x => x.id_medicoNavigation).ThenInclude(x => x.id_usuarioNavigation)
            .Include(x => x.id_estadoNavigation)
            .Where(x => x.fecha_cita >= filters.StartDate && x.fecha_cita <= filters.EndDate)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.State))
        {
            appointmentQuery = appointmentQuery.Where(x => x.id_estadoNavigation != null && x.id_estadoNavigation.nombre_estado == filters.State);
        }

        var appointments = await appointmentQuery.ToListAsync(cancellationToken);

        var clinicalEventsQuery = dbContext.tbl_historia_clinica_optometria_eventos
            .AsNoTracking()
            .Where(x => x.fecha_evento >= startDate && x.fecha_evento <= endDate && x.activo)
            .AsQueryable();

        if (filters.UserId > 0)
        {
            clinicalEventsQuery = clinicalEventsQuery.Where(x => x.id_optometra == filters.UserId);
        }

        var clinicalEvents = await clinicalEventsQuery
            .OrderByDescending(x => x.fecha_evento)
            .Take(100)
            .ToListAsync(cancellationToken);

        var productsQuery = dbContext.tbl_productos
            .AsNoTracking()
            .Include(x => x.id_categoriaNavigation)
            .Include(x => x.id_proveedorNavigation)
            .Where(x => x.activo ?? true)
            .AsQueryable();

        if (filters.ProductId > 0)
        {
            productsQuery = productsQuery.Where(x => x.id_producto == filters.ProductId);
        }

        if (filters.SupplierId > 0)
        {
            productsQuery = productsQuery.Where(x => x.id_proveedor == filters.SupplierId);
        }

        if (filters.CategoryId > 0)
        {
            productsQuery = productsQuery.Where(x => x.id_categoria == filters.CategoryId);
        }

        var products = await productsQuery.ToListAsync(cancellationToken);

        var kardexQuery = dbContext.tbl_kardex
            .AsNoTracking()
            .Include(x => x.id_productoNavigation)
            .Where(x => x.fecha_movimiento.HasValue && x.fecha_movimiento.Value >= startDate && x.fecha_movimiento.Value <= endDate)
            .AsQueryable();

        if (filters.ProductId > 0)
        {
            kardexQuery = kardexQuery.Where(x => x.id_producto == filters.ProductId);
        }

        var kardex = await kardexQuery.ToListAsync(cancellationToken);

        var purchaseOrdersQuery = dbContext.tbl_orden_compra
            .AsNoTracking()
            .Include(x => x.id_proveedorNavigation)
            .Include(x => x.tbl_detalle_orden_compra)
                .ThenInclude(x => x.id_productoNavigation)
            .Where(x => x.fecha_orden.HasValue && x.fecha_orden.Value >= startDate && x.fecha_orden.Value <= endDate)
            .AsQueryable();

        if (filters.SupplierId > 0)
        {
            purchaseOrdersQuery = purchaseOrdersQuery.Where(x => x.id_proveedor == filters.SupplierId);
        }

        var purchaseOrders = await purchaseOrdersQuery.ToListAsync(cancellationToken);

        var comprobantes = await dbContext.tbl_comprobantes
            .AsNoTracking()
            .Where(x => x.fecha_emision.HasValue && x.fecha_emision.Value >= startDate && x.fecha_emision.Value <= endDate)
            .ToListAsync(cancellationToken);

        var receivables = await dbContext.tbl_cta_cobrar
            .AsNoTracking()
            .Where(x => x.fecha_emision >= startDate && x.fecha_emision <= endDate)
            .ToListAsync(cancellationToken);

        var salesDetails = sales.SelectMany(x => x.tbl_detalle_venta).ToList();
        var filteredSalesDetails = salesDetails.Where(detail =>
            (filters.ProductId <= 0 || detail.id_producto == filters.ProductId) &&
            (filters.CategoryId <= 0 || detail.id_productoNavigation.id_categoria == filters.CategoryId) &&
            (filters.SupplierId <= 0 || detail.id_productoNavigation.id_proveedor == filters.SupplierId))
            .ToList();

        var totalSales = sales.Sum(x => x.total ?? 0m);
        var totalIncome = sales.Sum(x => x.valor_cobrado ?? 0m);
        var totalExpenses = purchaseOrders.Sum(x => x.total ?? 0m);

        return new SystemReportSnapshot
        {
            Filters = filters,
            SalesSummary = BuildSalesSummary(sales, filteredSalesDetails, totalSales),
            FinancialSummary = new FinancialSummary(
                totalIncome,
                totalExpenses,
                totalIncome - totalExpenses,
                receivables.Count(x => x.saldo > 0),
                receivables.Where(x => x.saldo > 0).Sum(x => x.saldo)),
            AppointmentSummary = BuildAppointmentSummary(appointments),
            ClinicalSummary = BuildClinicalSummary(clinicalEvents),
            ProductRotation = BuildProductRotation(filteredSalesDetails, products),
            StockSummary = BuildStockSummary(products),
            InventorySummary = BuildInventorySummary(kardex),
            PurchaseSummary = BuildPurchaseSummary(purchaseOrders),
            BillingSummary = BuildBillingSummary(comprobantes),
            DailyCashClosures = BuildDailyCashClosures(sales),
            GeneratedAt = DateTime.Now
        };
    }

    public string BuildCsv(SystemReportSnapshot report)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Seccion,Concepto,Valor1,Valor2,Valor3");
        csv.AppendLine($"Ventas,Total ventas,{report.SalesSummary.TotalSales.ToString("0.00", CultureInfo.InvariantCulture)},,");
        csv.AppendLine($"Finanzas,Ingresos,{report.FinancialSummary.TotalIncome.ToString("0.00", CultureInfo.InvariantCulture)},,");
        csv.AppendLine($"Finanzas,Egresos,{report.FinancialSummary.TotalExpenses.ToString("0.00", CultureInfo.InvariantCulture)},,");
        csv.AppendLine($"Citas,Agendadas,{report.AppointmentSummary.Scheduled},,");
        csv.AppendLine($"Citas,Atendidas,{report.AppointmentSummary.Completed},,");
        csv.AppendLine($"Inventario,Agotados,{report.StockSummary.OutOfStock},,");
        csv.AppendLine($"Inventario,Bajo minimo,{report.StockSummary.BelowMinimum},,");
        csv.AppendLine($"Facturacion,Autorizadas,{report.BillingSummary.Authorized},,");
        csv.AppendLine($"Facturacion,Anuladas,{report.BillingSummary.Cancelled},,");

        foreach (var item in report.ProductRotation.TopProducts)
        {
            csv.AppendLine($"TopProductos,{Escape(item.Label)},{item.Quantity},{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)},");
        }

        foreach (var item in report.ProductRotation.LowRotationProducts)
        {
            csv.AppendLine($"BajaRotacion,{Escape(item.Label)},{item.Quantity},{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)},");
        }

        foreach (var item in report.DailyCashClosures)
        {
            csv.AppendLine($"CierreCaja,{item.DateLabel},{item.TotalCollected.ToString("0.00", CultureInfo.InvariantCulture)},{item.CancelledCount},{item.SalesCount}");
        }

        return csv.ToString();
    }

    public byte[] BuildPdf(SystemReportSnapshot report)
    {
        var lines = new List<string>
        {
            "REPORTE DEL SISTEMA",
            $"Generado: {report.GeneratedAt:yyyy-MM-dd HH:mm}",
            $"Rango: {report.Filters.StartDate:yyyy-MM-dd} a {report.Filters.EndDate:yyyy-MM-dd}",
            "",
            $"Ventas totales: {report.SalesSummary.TotalSales:0.00}",
            $"Ingresos: {report.FinancialSummary.TotalIncome:0.00}",
            $"Egresos: {report.FinancialSummary.TotalExpenses:0.00}",
            $"Citas atendidas: {report.AppointmentSummary.Completed}",
            $"Consultas registradas: {report.ClinicalSummary.TotalConsultations}",
            $"Productos agotados: {report.StockSummary.OutOfStock}",
            $"Movimientos inventario: {report.InventorySummary.TotalMovements}",
            $"Compras registradas: {report.PurchaseSummary.TotalOrders}",
            $"Facturas autorizadas: {report.BillingSummary.Authorized}",
            $"Cuentas pendientes: {report.FinancialSummary.PendingReceivablesCount}",
            "",
            "Top productos:"
        };

        lines.AddRange(report.ProductRotation.TopProducts.Take(5).Select(x => $"{x.Label} | {x.Quantity} | {x.Amount:0.00}"));
        lines.Add("");
        lines.Add("Cierres de caja:");
        lines.AddRange(report.DailyCashClosures.Take(10).Select(x => $"{x.DateLabel} | {x.TotalCollected:0.00} | ventas {x.SalesCount}"));

        return SimplePdfBuilder.Build("Reporte del sistema", lines);
    }

    private static SalesSummary BuildSalesSummary(List<Models.tbl_venta> sales, List<Models.tbl_detalle_venta> details, decimal totalSales)
    {
        var grouped = details
            .GroupBy(x => ClassifySaleItem(x))
            .Select(x => new ReportMetricRow(x.Key, x.Sum(v => v.cantidad), x.Sum(v => v.total_item ?? 0m)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var daily = sales.GroupBy(x => x.fecha_venta!.Value.Date).Count();
        var weekly = sales.GroupBy(x => ISOWeek.GetWeekOfYear(x.fecha_venta!.Value)).Count();
        var monthly = sales.GroupBy(x => new { x.fecha_venta!.Value.Year, x.fecha_venta!.Value.Month }).Count();
        var yearly = sales.GroupBy(x => x.fecha_venta!.Value.Year).Count();

        return new SalesSummary(totalSales, daily, weekly, monthly, yearly, grouped);
    }

    private static AppointmentSummary BuildAppointmentSummary(List<Models.tbl_citas> appointments)
        => new(
            appointments.Count,
            appointments.Count(x => x.id_estadoNavigation?.nombre_estado == "Programada" || x.id_estadoNavigation?.nombre_estado == "Reprogramada"),
            appointments.Count(x => x.id_estadoNavigation?.nombre_estado == "Confirmada"),
            appointments.Count(x => x.id_estadoNavigation?.nombre_estado == "Realizada"),
            appointments.Count(x => x.id_estadoNavigation?.nombre_estado == "Cancelada"));

    private static ClinicalSummary BuildClinicalSummary(List<Models.tbl_historia_clinica_optometria_evento> events)
        => new(
            events.Count,
            events.Take(20).Select(x => new ClinicalReportRow(
                x.fecha_evento.ToString("yyyy-MM-dd"),
                x.motivo_consulta ?? "Sin motivo",
                x.diagnostico_resumen ?? "Sin diagnostico",
                x.anamnesis ?? string.Empty)).ToList());

    private static ProductRotationSummary BuildProductRotation(List<Models.tbl_detalle_venta> details, List<Models.tbl_producto> products)
    {
        var grouped = details
            .GroupBy(x => x.id_producto)
            .Select(x => new ReportMetricRow(
                x.First().id_productoNavigation.nombre_producto,
                x.Sum(v => v.cantidad),
                x.Sum(v => v.total_item ?? 0m)))
            .OrderByDescending(x => x.Quantity)
            .ToList();

        var top = grouped.Take(10).ToList();
        var low = products
            .Select(product =>
            {
                var match = grouped.FirstOrDefault(x => x.Label == product.nombre_producto);
                return match ?? new ReportMetricRow(product.nombre_producto, 0, 0m);
            })
            .OrderBy(x => x.Quantity)
            .ThenBy(x => x.Amount)
            .Take(10)
            .ToList();

        return new ProductRotationSummary(top, low);
    }

    private static StockSummary BuildStockSummary(List<Models.tbl_producto> products)
        => new(
            products.Count,
            products.Count(x => (x.stock_actual ?? 0) <= 0),
            products.Count(x => (x.stock_actual ?? 0) > 0 && (x.stock_actual ?? 0) <= (x.stock_minimo ?? 0)),
            products.Take(20).Select(x => new StockReportRow(
                x.nombre_producto,
                x.stock_actual ?? 0,
                x.stock_minimo ?? 0,
                x.id_categoriaNavigation?.nombre ?? "Sin categoria")).ToList());

    private static InventorySummary BuildInventorySummary(List<Models.tbl_kardex> kardex)
        => new(
            kardex.Count,
            kardex.Count(x => x.tipo_movimiento == "Entrada"),
            kardex.Count(x => x.tipo_movimiento == "Salida"),
            kardex.Count(x => x.tipo_movimiento == "Devolucion"),
            kardex.Count(x => x.tipo_movimiento == "Ajuste"));

    private static PurchaseSummary BuildPurchaseSummary(List<Models.tbl_orden_compra> orders)
        => new(
            orders.Count,
            orders.Sum(x => x.total ?? 0m),
            orders.Take(20).Select(x => new PurchaseReportRow(
                x.numero_orden,
                x.id_proveedorNavigation.nombre,
                x.fecha_orden?.ToString("yyyy-MM-dd") ?? "Sin fecha",
                x.total ?? 0m,
                x.estado_orden ?? "Sin estado")).ToList());

    private static BillingSummary BuildBillingSummary(List<Models.tbl_comprobante> comprobantes)
        => new(
            comprobantes.Count,
            comprobantes.Count(x => string.Equals(x.estado_sri, "AUTORIZADO", StringComparison.OrdinalIgnoreCase) || string.Equals(x.estado_comprobante, "Autorizada", StringComparison.OrdinalIgnoreCase)),
            comprobantes.Count(x => string.Equals(x.estado_comprobante, "Anulada", StringComparison.OrdinalIgnoreCase)),
            comprobantes.Count(x => string.Equals(x.estado_sri, "RECHAZADO", StringComparison.OrdinalIgnoreCase) || string.Equals(x.estado_comprobante, "Rechazada", StringComparison.OrdinalIgnoreCase)));

    private static List<DailyCashClosureRow> BuildDailyCashClosures(List<Models.tbl_venta> sales)
    {
        return sales
            .GroupBy(x => x.fecha_venta!.Value.Date)
            .OrderByDescending(x => x.Key)
            .Take(31)
            .Select(x => new DailyCashClosureRow(
                x.Key.ToString("yyyy-MM-dd"),
                x.Count(),
                x.Count(v => string.Equals(v.estado, "Anulada", StringComparison.OrdinalIgnoreCase)),
                x.Sum(v => v.valor_cobrado ?? 0m)))
            .ToList();
    }

    private static string ClassifySaleItem(Models.tbl_detalle_venta detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.origen_tipo))
        {
            return detail.origen_tipo!;
        }

        var tipo = detail.id_productoNavigation.tipo_item ?? detail.concepto_item ?? "Producto";
        if (tipo.Contains("serv", StringComparison.OrdinalIgnoreCase))
        {
            return "Servicios";
        }

        if (tipo.Contains("lente", StringComparison.OrdinalIgnoreCase))
        {
            return "Lentes";
        }

        if (tipo.Contains("consult", StringComparison.OrdinalIgnoreCase))
        {
            return "Consultas";
        }

        return "Productos";
    }

    private static string Escape(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static class SimplePdfBuilder
    {
        public static byte[] Build(string title, IEnumerable<string> lines)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true);
            var objects = new List<long>();
            var content = BuildContent(title, lines);

            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            objects.Add(stream.Position);
            writer.WriteLine("1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj");
            writer.Flush();

            objects.Add(stream.Position);
            writer.WriteLine("2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj");
            writer.Flush();

            objects.Add(stream.Position);
            writer.WriteLine("3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>endobj");
            writer.Flush();

            objects.Add(stream.Position);
            writer.WriteLine("4 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj");
            writer.Flush();

            objects.Add(stream.Position);
            writer.WriteLine($"5 0 obj<< /Length {content.Length} >>stream");
            writer.Flush();
            stream.Write(content, 0, content.Length);
            writer.WriteLine();
            writer.WriteLine("endstream endobj");
            writer.Flush();

            var xrefPosition = stream.Position;
            writer.WriteLine($"xref\n0 {objects.Count + 1}\n0000000000 65535 f ");
            foreach (var offset in objects)
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }

            writer.WriteLine($"trailer<< /Size {objects.Count + 1} /Root 1 0 R >>");
            writer.WriteLine($"startxref\n{xrefPosition}\n%%EOF");
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildContent(string title, IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BT");
            sb.AppendLine("/F1 16 Tf");
            sb.AppendLine("50 760 Td");
            sb.AppendLine($"({EscapePdf(title)}) Tj");
            sb.AppendLine("/F1 10 Tf");
            sb.AppendLine("0 -22 Td");

            foreach (var line in lines)
            {
                sb.AppendLine($"({EscapePdf(line)}) Tj");
                sb.AppendLine("0 -14 Td");
            }

            sb.AppendLine("ET");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string EscapePdf(string value)
            => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
}

public sealed record SystemReportFilters(
    DateOnly StartDate,
    DateOnly EndDate,
    int UserId = 0,
    int ProductId = 0,
    int SupplierId = 0,
    int CategoryId = 0,
    string State = "");

public sealed class SystemReportSnapshot
{
    public required SystemReportFilters Filters { get; init; }
    public required SalesSummary SalesSummary { get; init; }
    public required FinancialSummary FinancialSummary { get; init; }
    public required AppointmentSummary AppointmentSummary { get; init; }
    public required ClinicalSummary ClinicalSummary { get; init; }
    public required ProductRotationSummary ProductRotation { get; init; }
    public required StockSummary StockSummary { get; init; }
    public required InventorySummary InventorySummary { get; init; }
    public required PurchaseSummary PurchaseSummary { get; init; }
    public required BillingSummary BillingSummary { get; init; }
    public required List<DailyCashClosureRow> DailyCashClosures { get; init; }
    public required DateTime GeneratedAt { get; init; }
}

public sealed record SalesSummary(decimal TotalSales, int DailyBuckets, int WeeklyBuckets, int MonthlyBuckets, int YearlyBuckets, List<ReportMetricRow> Breakdown);
public sealed record FinancialSummary(decimal TotalIncome, decimal TotalExpenses, decimal NetBalance, int PendingReceivablesCount, decimal PendingReceivablesAmount);
public sealed record AppointmentSummary(int Scheduled, int Pending, int Confirmed, int Completed, int Cancelled);
public sealed record ClinicalSummary(int TotalConsultations, List<ClinicalReportRow> RecentConsultations);
public sealed record ProductRotationSummary(List<ReportMetricRow> TopProducts, List<ReportMetricRow> LowRotationProducts);
public sealed record StockSummary(int TotalProducts, int OutOfStock, int BelowMinimum, List<StockReportRow> Sample);
public sealed record InventorySummary(int TotalMovements, int Entries, int Exits, int Returns, int Adjustments);
public sealed record PurchaseSummary(int TotalOrders, decimal TotalAmount, List<PurchaseReportRow> RecentOrders);
public sealed record BillingSummary(int TotalDocuments, int Authorized, int Cancelled, int Rejected);
public sealed record ReportMetricRow(string Label, int Quantity, decimal Amount);
public sealed record ClinicalReportRow(string Date, string Diagnosis, string Treatment, string Notes);
public sealed record StockReportRow(string Product, int CurrentStock, int MinimumStock, string Category);
public sealed record PurchaseReportRow(string OrderNumber, string Supplier, string Date, decimal Amount, string Status);
public sealed record DailyCashClosureRow(string DateLabel, int SalesCount, int CancelledCount, decimal TotalCollected);
