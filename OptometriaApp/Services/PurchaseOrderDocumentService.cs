using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class PurchaseOrderDocumentService
{
    public async Task<PurchaseOrderDocument?> BuildAsync(OpticaDbContext dbContext, int orderId, int userId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.tbl_orden_compra
            .AsNoTracking()
            .Include(x => x.id_proveedorNavigation)
            .Include(x => x.tbl_detalle_orden_compra)
                .ThenInclude(x => x.id_productoNavigation)
            .FirstOrDefaultAsync(x => x.id_orden_compra == orderId && x.id_usuario_solicita == userId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var xml = BuildXml(order);
        var pdf = BuildPdf(order);
        return new PurchaseOrderDocument(order.id_orden_compra, order.numero_orden, xml, pdf);
    }

    private static string BuildXml(Models.tbl_orden_compra order)
    {
        var document = new XDocument(
            new XElement("ordenCompra",
                new XElement("numero", order.numero_orden),
                new XElement("fecha", order.fecha_orden?.ToString("yyyy-MM-ddTHH:mm:ss") ?? string.Empty),
                new XElement("proveedor",
                    new XElement("id", order.id_proveedor),
                    new XElement("nombre", order.id_proveedorNavigation.nombre),
                    new XElement("ruc", order.id_proveedorNavigation.ruc ?? string.Empty)),
                new XElement("estado", order.estado_orden ?? string.Empty),
                new XElement("condicionPago", order.condicion_pago ?? string.Empty),
                new XElement("moneda", order.moneda ?? "USD"),
                new XElement("total", (order.total ?? 0m).ToString("0.00", CultureInfo.InvariantCulture)),
                new XElement("lineas",
                    order.tbl_detalle_orden_compra.Select(line =>
                        new XElement("linea",
                            new XElement("productoId", line.id_producto),
                            new XElement("producto", line.id_productoNavigation.nombre_producto),
                            new XElement("cantidad", line.cantidad_solicitada),
                            new XElement("precioUnitario", line.precio_unitario.ToString("0.00", CultureInfo.InvariantCulture)),
                            new XElement("totalLinea", (line.precio_total_linea ?? 0m).ToString("0.00", CultureInfo.InvariantCulture)),
                            new XElement("estado", line.estado_linea ?? string.Empty))))));

        return document.Declaration + Environment.NewLine + document;
    }

    private static byte[] BuildPdf(Models.tbl_orden_compra order)
    {
        var lines = new List<string>
        {
            $"Orden: {order.numero_orden}",
            $"Proveedor: {order.id_proveedorNavigation.nombre}",
            $"Fecha: {order.fecha_orden:yyyy-MM-dd HH:mm}",
            $"Estado: {order.estado_orden}",
            $"Condicion pago: {order.condicion_pago}",
            $"Total: {(order.total ?? 0m):0.00}",
            ""
        };

        lines.AddRange(order.tbl_detalle_orden_compra.Select(line =>
            $"{line.id_productoNavigation.nombre_producto} | {line.cantidad_solicitada} | {line.precio_unitario:0.00} | {(line.precio_total_linea ?? 0m):0.00}"));

        return SimplePdfBuilder.Build($"Orden de compra {order.numero_orden}", lines);
    }

    private static class SimplePdfBuilder
    {
        public static byte[] Build(string title, IEnumerable<string> lines)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true);
            var offsets = new List<long>();
            var content = BuildContent(title, lines);

            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            offsets.Add(stream.Position);
            writer.WriteLine("1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj");
            writer.Flush();

            offsets.Add(stream.Position);
            writer.WriteLine("2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj");
            writer.Flush();

            offsets.Add(stream.Position);
            writer.WriteLine("3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>endobj");
            writer.Flush();

            offsets.Add(stream.Position);
            writer.WriteLine("4 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj");
            writer.Flush();

            offsets.Add(stream.Position);
            writer.WriteLine($"5 0 obj<< /Length {content.Length} >>stream");
            writer.Flush();
            stream.Write(content, 0, content.Length);
            writer.WriteLine();
            writer.WriteLine("endstream endobj");
            writer.Flush();

            var xref = stream.Position;
            writer.WriteLine($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f ");
            foreach (var offset in offsets)
            {
                writer.WriteLine($"{offset:0000000000} 00000 n ");
            }

            writer.WriteLine($"trailer<< /Size {offsets.Count + 1} /Root 1 0 R >>");
            writer.WriteLine($"startxref\n{xref}\n%%EOF");
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildContent(string title, IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BT");
            sb.AppendLine("/F1 16 Tf");
            sb.AppendLine("50 760 Td");
            sb.AppendLine($"({Escape(title)}) Tj");
            sb.AppendLine("/F1 10 Tf");
            sb.AppendLine("0 -22 Td");

            foreach (var line in lines)
            {
                sb.AppendLine($"({Escape(line)}) Tj");
                sb.AppendLine("0 -14 Td");
            }

            sb.AppendLine("ET");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string Escape(string value)
            => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
}

public sealed record PurchaseOrderDocument(int OrderId, string OrderNumber, string XmlContent, byte[] PdfContent);
