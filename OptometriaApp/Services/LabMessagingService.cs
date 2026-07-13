using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class LabMessagingService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public LabMessagingService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<LabOrderExportDocument?> BuildOrderDocumentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var order = await dbContext.tbl_orden_rxes
            .AsNoTracking()
            .Include(x => x.id_laboratorioNavigation)
            .Include(x => x.id_consultaNavigation)
            .ThenInclude(x => x.id_pacienteNavigation)
            .Include(x => x.id_consultaNavigation)
            .ThenInclude(x => x.id_optometraNavigation)
            .Include(x => x.id_rx_lenteNavigation)
            .Include(x => x.id_rx_contactologiaNavigation)
            .FirstOrDefaultAsync(x => x.id_orden_rx == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var patient = order.id_consultaNavigation.id_pacienteNavigation;
        var optometrist = order.id_consultaNavigation.id_optometraNavigation;
        var orderNumber = string.IsNullOrWhiteSpace(order.numero_orden) ? $"RX-{order.id_orden_rx:000000}" : order.numero_orden!;
        var rxType = string.IsNullOrWhiteSpace(order.tipo_rx) ? "Lente" : order.tipo_rx!;
        var settings = await OpticaCustomizationService.GetOrCreateSettingsAsync(dbContext, cancellationToken);
        var template = await OpticaCustomizationService.GetOrCreateTemplateAsync(
            dbContext,
            "WhatsApp",
            OpticaCustomizationService.LabWhatsappTemplateType,
            OpticaCustomizationService.DefaultLabWhatsappTemplate,
            cancellationToken);
        var message = OpticaCustomizationService.RenderTemplate(
            template.contenido ?? OpticaCustomizationService.DefaultLabWhatsappTemplate,
            new Dictionary<string, string>
            {
                ["order_number"] = orderNumber,
                ["patient_name"] = $"{patient.nombres} {patient.apellidos}".Trim(),
                ["rx_type"] = rxType,
                ["laboratory_name"] = order.id_laboratorioNavigation.nombre,
                ["observations"] = string.IsNullOrWhiteSpace(order.observaciones) ? string.Empty : $"Observaciones: {order.observaciones.Trim()}."
            });

        var pdf = BuildPdf(orderNumber, rxType, patient, optometrist, order, message);

        return new LabOrderExportDocument
        {
            OrderId = order.id_orden_rx,
            OrderNumber = orderNumber,
            LaboratoryWhatsapp = OpticaCustomizationService.NormalizePhone(order.id_laboratorioNavigation.whatsapp, settings.prefijo_pais),
            Message = message,
            PdfContent = pdf
        };
    }

    public static string BuildWhatsAppLink(string phoneNumber, string message)
    {
        return $"https://wa.me/{phoneNumber}?text={Uri.EscapeDataString(message)}";
    }

    private static byte[] BuildPdf(
        string orderNumber,
        string rxType,
        Models.tbl_paciente patient,
        Models.tbl_usuario optometrist,
        Models.tbl_orden_rx order,
        string message)
    {
        var lines = new List<string>
        {
            "RECETA RX PARA LABORATORIO",
            "",
            $"Orden: {orderNumber}",
            $"Generado: {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Paciente: {patient.nombres} {patient.apellidos}".Trim(),
            $"Cedula: {patient.cedula}",
            $"Fecha consulta: {order.id_consultaNavigation.fecha_consulta?.ToString("yyyy-MM-dd HH:mm") ?? "-"}",
            $"Tipo RX: {rxType}",
            $"Laboratorio: {order.id_laboratorioNavigation.nombre}",
            $"Optometra: {optometrist.nombres} {optometrist.apellidos}".Trim(),
            "",
            "PARAMETROS OJO DERECHO"
        };

        lines.AddRange(BuildEyeLines(order, true));
        lines.Add("");
        lines.Add("PARAMETROS OJO IZQUIERDO");
        lines.AddRange(BuildEyeLines(order, false));
        lines.Add("");
        lines.Add("OBSERVACIONES");
        lines.AddRange(Wrap(string.IsNullOrWhiteSpace(order.observaciones) ? "Sin observaciones." : order.observaciones!.Trim(), 95));
        lines.Add("");
        lines.Add("MENSAJE WHATSAPP SUGERIDO");
        lines.AddRange(Wrap(message, 95));

        return SimplePdfBuilder.Build(lines);
    }

    private static IEnumerable<string> BuildEyeLines(Models.tbl_orden_rx order, bool rightEye)
    {
        if (string.Equals(order.tipo_rx, "Contactologia", StringComparison.OrdinalIgnoreCase) && order.id_rx_contactologiaNavigation is not null)
        {
            var rx = order.id_rx_contactologiaNavigation;
            return
            [
                $"Esfera: {FormatDecimal(rightEye ? rx.od_esfera : rx.oi_esfera)}",
                $"Cilindro: {FormatDecimal(rightEye ? rx.od_cilindro : rx.oi_cilindro)}",
                $"Eje: {FormatDecimal(rightEye ? rx.od_eje : rx.oi_eje)}",
                $"Diametro: {FormatDecimal(rightEye ? rx.od_diametro : rx.oi_diametro)}",
                $"Curva base: {FormatDecimal(rightEye ? rx.od_curva_base : rx.oi_curva_base)}"
            ];
        }

        var lens = order.id_rx_lenteNavigation;
        return
        [
            $"Esfera: {FormatDecimal(rightEye ? lens?.od_esfera : lens?.oi_esfera)}",
            $"Cilindro: {FormatDecimal(rightEye ? lens?.od_cilindro : lens?.oi_cilindro)}",
            $"Eje: {FormatDecimal(rightEye ? lens?.od_eje : lens?.oi_eje)}",
            $"Adicion: {FormatDecimal(rightEye ? lens?.od_addicion : lens?.oi_addicion)}"
        ];
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

    private static string FormatDecimal(decimal? value) => value?.ToString("0.##") ?? "-";
}

public sealed class LabOrderExportDocument
{
    public int OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string LaboratoryWhatsapp { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public byte[] PdfContent { get; init; } = [];
}

internal static class SimplePdfBuilder
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

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string BuildPageContent(IReadOnlyList<string> lines, int pageNumber, int pageCount)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n/F1 10 Tf\n14 TL\n");
        var currentY = 760;
        foreach (var line in lines)
        {
            sb.Append($"1 0 0 1 50 {currentY} Tm ({EscapePdf(line)}) Tj\n");
            currentY -= 14;
        }

        sb.Append($"1 0 0 1 500 20 Tm (Pag {pageNumber}/{pageCount}) Tj\n");
        sb.Append("ET");
        return sb.ToString();
    }

    private static string EscapePdf(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
