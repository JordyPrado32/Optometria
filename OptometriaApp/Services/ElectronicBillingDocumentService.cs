using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class ElectronicBillingDocumentService
{
    private readonly IWebHostEnvironment environment;

    public ElectronicBillingDocumentService(IWebHostEnvironment environment)
    {
        this.environment = environment;
    }

    public async Task<ElectronicDocumentResult> GenerateInvoiceDocumentAsync(
        OpticaDbContext dbContext,
        tbl_comprobante comprobante,
        CancellationToken cancellationToken = default)
    {
        var sale = await dbContext.tbl_venta
            .Include(x => x.id_pacienteNavigation)
            .Include(x => x.tbl_detalle_venta)
            .ThenInclude(x => x.id_productoNavigation)
            .FirstOrDefaultAsync(x => x.id_venta == comprobante.id_venta, cancellationToken);

        var issuer = await ResolveIssuerAsync(dbContext, comprobante.id_emisor, cancellationToken);
        var client = sale?.id_cliente_facturacion is > 0
            ? await dbContext.clients.AsNoTracking().FirstOrDefaultAsync(x => x.cliente_id == sale.id_cliente_facturacion!.Value, cancellationToken)
            : null;

        if (sale is null || issuer is null)
        {
            return ElectronicDocumentResult.Error("No se pudo resolver la venta o el emisor para generar el XML de factura.");
        }

        if (sale.id_pacienteNavigation is null)
        {
            return ElectronicDocumentResult.Error("La venta no tiene un paciente asociado valido para generar el XML de factura.");
        }

        var issueDate = comprobante.fecha_emision ?? DateTime.Now;
        var accessKey = BuildAccessKey(issueDate, "01", issuer, comprobante.secuencial ?? 0, BuildNumericCode(comprobante.id_comprobante, sale.id_venta));
        var xml = BuildInvoiceXml(issuer, client, sale, comprobante, issueDate, accessKey);

        return await PersistAndSignAsync(
            xml,
            issuer,
            "factura",
            comprobante.numero_comprobante ?? $"factura-{comprobante.id_comprobante}",
            issueDate,
            applyResult: result =>
            {
                comprobante.clave_acceso = accessKey;
                comprobante.codigo_numerico = ExtractNumericCode(accessKey);
                comprobante.ambiente_sri = issuer.ambiente_codigo;
                comprobante.tipo_emision_sri = issuer.tipo_emision_codigo;
                comprobante.version_xml = "1.0.0";
                comprobante.xml_no_firmado = result.UnsignedXml;
                comprobante.xml_firmado = result.SignedXml;
                comprobante.hash_xml = result.HashHex;
                comprobante.fecha_firma = result.SignedAt;
                comprobante.ruta_xml = result.XmlPath;
                comprobante.estado_sri = result.StatusCode;
                comprobante.mensajes_sri = result.Message;
                comprobante.estado_comprobante = result.StatusCode switch
                {
                    "PENDIENTE_FIRMA" => "PendienteFirma",
                    "ERROR_XML" => "ErrorXML",
                    _ => "PendienteSRI"
                };
            });
    }

    public async Task<ElectronicDocumentResult> GenerateCreditNoteDocumentAsync(
        OpticaDbContext dbContext,
        tbl_nota_credito creditNote,
        CancellationToken cancellationToken = default)
    {
        var relatedInvoice = await dbContext.tbl_comprobantes
            .AsNoTracking()
            .Include(x => x.id_ventaNavigation!)
            .ThenInclude(x => x.id_pacienteNavigation)
            .Include(x => x.id_ventaNavigation!)
            .ThenInclude(x => x.tbl_detalle_venta)
            .ThenInclude(x => x.id_productoNavigation)
            .FirstOrDefaultAsync(x => x.id_comprobante == creditNote.id_comprobante_relacionado, cancellationToken);

        var issuer = await ResolveIssuerAsync(dbContext, relatedInvoice?.id_emisor, cancellationToken);
        var client = relatedInvoice?.id_ventaNavigation?.id_cliente_facturacion is > 0
            ? await dbContext.clients.AsNoTracking().FirstOrDefaultAsync(x => x.cliente_id == relatedInvoice.id_ventaNavigation.id_cliente_facturacion!.Value, cancellationToken)
            : null;

        var detailRows = await dbContext.tbl_detalle_nota_credito
            .AsNoTracking()
            .Include(x => x.id_detalle_ventaNavigation)
            .ThenInclude(x => x.id_productoNavigation)
            .Where(x => x.id_nota_credito == creditNote.id_nota_credito)
            .ToListAsync(cancellationToken);

        if (relatedInvoice?.id_ventaNavigation is null || issuer is null)
        {
            return ElectronicDocumentResult.Error("No se pudo resolver el comprobante base o el emisor para generar el XML de nota de crédito.");
        }

        if (relatedInvoice.id_ventaNavigation.id_pacienteNavigation is null)
        {
            return ElectronicDocumentResult.Error("La factura origen no tiene un paciente asociado valido para generar el XML de nota de credito.");
        }

        if (detailRows.Count == 0)
        {
            return ElectronicDocumentResult.Error("La nota de credito no tiene detalle suficiente para generar el XML.");
        }

        if (detailRows.Any(x => x.id_detalle_ventaNavigation?.id_productoNavigation is null))
        {
            return ElectronicDocumentResult.Error("Una o mas lineas de la nota de credito no tienen producto asociado y no pueden exportarse al XML.");
        }

        var issueDate = creditNote.fecha_emision;
        var sequence = creditNote.secuencial ?? ParseTrailingSequence(creditNote.numero_nota) ?? 0;
        var accessKey = BuildAccessKey(issueDate, "04", issuer, sequence, BuildNumericCode(creditNote.id_nota_credito, relatedInvoice.id_comprobante));
        var xml = BuildCreditNoteXml(issuer, client, relatedInvoice, creditNote, detailRows, issueDate, accessKey, sequence);

        return await PersistAndSignAsync(
            xml,
            issuer,
            "nota-credito",
            creditNote.numero_nota,
            issueDate,
            applyResult: result =>
            {
                creditNote.secuencial = sequence;
                creditNote.clave_acceso = accessKey;
                creditNote.codigo_numerico = ExtractNumericCode(accessKey);
                creditNote.ambiente_sri = issuer.ambiente_codigo;
                creditNote.tipo_emision_sri = issuer.tipo_emision_codigo;
                creditNote.version_xml = "1.0.0";
                creditNote.xml_no_firmado = result.UnsignedXml;
                creditNote.xml_firmado = result.SignedXml;
                creditNote.hash_xml = result.HashHex;
                creditNote.fecha_firma = result.SignedAt;
                creditNote.ruta_xml = result.XmlPath;
                creditNote.estado_sri = result.StatusCode;
                creditNote.mensajes_sri = result.Message;
            });
    }

    private async Task<ElectronicDocumentResult> PersistAndSignAsync(
        XDocument document,
        EmisorEntity issuer,
        string folderName,
        string documentNumber,
        DateTime issueDate,
        Action<ElectronicDocumentResult> applyResult)
    {
        var unsignedXml = document.Declaration + Environment.NewLine + document;
        var result = new ElectronicDocumentResult
        {
            UnsignedXml = unsignedXml,
            SignedXml = unsignedXml,
            SignedAt = null,
            StatusCode = "PENDIENTE_FIRMA",
            Message = "XML generado correctamente. Falta configurar o validar el certificado digital del emisor."
        };

        var certificateOutcome = TrySignXml(unsignedXml, issuer);
        if (certificateOutcome.Success)
        {
            result.SignedXml = certificateOutcome.SignedXml!;
            result.SignedAt = DateTime.Now;
            result.StatusCode = "PENDIENTE_SRI";
            result.Message = "XML generado y firmado localmente. Queda pendiente el envío al SRI.";
        }
        else if (!string.IsNullOrWhiteSpace(certificateOutcome.ErrorMessage))
        {
            result.Message = certificateOutcome.ErrorMessage!;
        }

        result.HashHex = ComputeSha256Hex(result.SignedXml);
        result.XmlPath = SaveXml(folderName, documentNumber, issueDate, result.SignedXml);
        applyResult(result);
        return result;
    }

    private static XDocument BuildInvoiceXml(
        EmisorEntity issuer,
        ClientEntity? client,
        tbl_venta sale,
        tbl_comprobante comprobante,
        DateTime issueDate,
        string accessKey)
    {
        var details = sale.tbl_detalle_venta
            .Where(x => x.id_productoNavigation is not null)
            .Select(x => BuildInvoiceLineElement(x))
            .ToList();

        var totalWithoutTaxes = Round2(sale.subtotal ?? 0m);
        var totalDiscount = Round2(sale.descuento_total ?? 0m);
        var totalTax = Round2(sale.impuesto_total ?? 0m);
        var totalAmount = Round2(sale.total ?? 0m);

        var root = new XElement("factura",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.0.0"),
            BuildInfoTributaria(issuer, "01", accessKey, comprobante.secuencial ?? 0),
            new XElement("infoFactura",
                new XElement("fechaEmision", issueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                OptionalElement("dirEstablecimiento", issuer.direccion_establecimiento ?? issuer.direccion),
                OptionalElement(issuer.es_contribuyente_especial ? "contribuyenteEspecial" : null, issuer.numero_contribuyente_especial),
                new XElement("obligadoContabilidad", issuer.obligado_contabilidad ? "SI" : "NO"),
                new XElement("tipoIdentificacionComprador", NormalizeBuyerIdentificationType(client)),
                new XElement("razonSocialComprador", NormalizeBuyerName(client, sale.id_pacienteNavigation)),
                new XElement("identificacionComprador", NormalizeBuyerIdentification(client, sale.id_pacienteNavigation)),
                OptionalElement("direccionComprador", client?.direccion ?? sale.id_pacienteNavigation.direccion),
                new XElement("totalSinImpuestos", DecimalText(totalWithoutTaxes)),
                new XElement("totalDescuento", DecimalText(totalDiscount)),
                new XElement("totalConImpuestos", BuildTaxTotalsElement(sale.tbl_detalle_venta)),
                new XElement("propina", DecimalText(0m)),
                new XElement("importeTotal", DecimalText(totalAmount)),
                new XElement("moneda", "DOLAR"),
                new XElement("pagos",
                    new XElement("pago",
                        new XElement("formaPago", MapPaymentCode(sale.forma_pago)),
                        new XElement("total", DecimalText(totalAmount)),
                        new XElement("plazo", Math.Max(0, sale.dias_credito ?? 0)),
                        new XElement("unidadTiempo", "dias")))),
            new XElement("detalles", details),
            BuildInvoiceAdditionalInfo(client, sale));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XDocument BuildCreditNoteXml(
        EmisorEntity issuer,
        ClientEntity? client,
        tbl_comprobante relatedInvoice,
        tbl_nota_credito creditNote,
        IReadOnlyList<tbl_detalle_nota_credito> detailRows,
        DateTime issueDate,
        string accessKey,
        long sequence)
    {
        var sale = relatedInvoice.id_ventaNavigation!;
        var root = new XElement("notaCredito",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.0.0"),
            BuildInfoTributaria(issuer, "04", accessKey, sequence),
            new XElement("infoNotaCredito",
                new XElement("fechaEmision", issueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                OptionalElement("dirEstablecimiento", issuer.direccion_establecimiento ?? issuer.direccion),
                new XElement("tipoIdentificacionComprador", NormalizeBuyerIdentificationType(client)),
                new XElement("razonSocialComprador", NormalizeBuyerName(client, sale.id_pacienteNavigation)),
                new XElement("identificacionComprador", NormalizeBuyerIdentification(client, sale.id_pacienteNavigation)),
                OptionalElement(issuer.es_contribuyente_especial ? "contribuyenteEspecial" : null, issuer.numero_contribuyente_especial),
                new XElement("obligadoContabilidad", issuer.obligado_contabilidad ? "SI" : "NO"),
                new XElement("codDocModificado", "01"),
                new XElement("numDocModificado", SanitizeDocumentNumber(relatedInvoice.numero_comprobante)),
                new XElement("fechaEmisionDocSustento", (relatedInvoice.fecha_emision ?? issueDate).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                new XElement("totalSinImpuestos", DecimalText(detailRows.Sum(x => x.monto_subtotal))),
                new XElement("valorModificacion", DecimalText(creditNote.monto_total)),
                new XElement("moneda", "DOLAR"),
            new XElement("totalConImpuestos", BuildCreditNoteTaxTotalsElement(detailRows)),
            new XElement("motivo", SafeText(creditNote.motivo, "AJUSTE COMERCIAL"))),
            new XElement("detalles", detailRows.Select(BuildCreditNoteLineElement)),
            BuildCreditNoteAdditionalInfo(client, sale.id_pacienteNavigation));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildInfoTributaria(EmisorEntity issuer, string documentCode, string accessKey, long sequence)
    {
        return new XElement("infoTributaria",
            new XElement("ambiente", NormalizeCode(issuer.ambiente_codigo, 1, "1")),
            new XElement("tipoEmision", NormalizeCode(issuer.tipo_emision_codigo, 1, "1")),
            new XElement("razonSocial", SafeText(issuer.razon_social, "EMISOR")),
            OptionalElement("nombreComercial", issuer.nombre_comercial),
            new XElement("ruc", DigitsOnly(issuer.ruc, 13)),
            new XElement("claveAcceso", accessKey),
            new XElement("codDoc", documentCode),
            new XElement("estab", NormalizeCode(issuer.establecimiento_codigo, 3, "001")),
            new XElement("ptoEmi", NormalizeCode(issuer.punto_emision_codigo, 3, "001")),
            new XElement("secuencial", sequence.ToString("D9", CultureInfo.InvariantCulture)),
            new XElement("dirMatriz", SafeText(issuer.direccion, "SIN DIRECCION")));
    }

    private static XElement BuildTaxTotalsElement(IEnumerable<tbl_detalle_venta> lines)
    {
        var groups = lines
            .GroupBy(x => ResolveVatPercentage(x.id_productoNavigation?.porcentaje_iva))
            .Select(g =>
            {
                var baseAmount = Round2(g.Sum(x => x.total_item ?? 0m));
                var vatRate = g.Key;
                var vatValue = Round2(baseAmount * vatRate / 100m);
                return new { VatRate = vatRate, Base = baseAmount, Tax = vatValue };
            })
            .Where(x => x.Base > 0m)
            .ToList();

        return new XElement("totalConImpuestos",
            groups.Select(x => new XElement("totalImpuesto",
                new XElement("codigo", "2"),
                new XElement("codigoPorcentaje", MapVatCode(x.VatRate)),
                new XElement("baseImponible", DecimalText(x.Base)),
                new XElement("valor", DecimalText(x.Tax)))));
    }

    private static XElement BuildCreditNoteTaxTotalsElement(IEnumerable<tbl_detalle_nota_credito> lines)
    {
        var groups = lines
            .GroupBy(x => ResolveVatPercentage(x.porcentaje_impuesto))
            .Select(g => new
            {
                VatRate = g.Key,
                Base = Round2(g.Sum(x => x.monto_subtotal)),
                Tax = Round2(g.Sum(x => x.monto_impuesto))
            })
            .Where(x => x.Base > 0m || x.Tax > 0m)
            .ToList();

        return new XElement("totalConImpuestos",
            groups.Select(x => new XElement("totalImpuesto",
                new XElement("codigo", "2"),
                new XElement("codigoPorcentaje", MapVatCode(x.VatRate)),
                new XElement("baseImponible", DecimalText(x.Base)),
                new XElement("valor", DecimalText(x.Tax)))));
    }

    private static XElement BuildInvoiceLineElement(tbl_detalle_venta line)
    {
        var product = line.id_productoNavigation ?? new tbl_producto();
        var subtotal = Round2(line.total_item ?? 0m);
        var unitPrice = Round2(line.precio_unitario ?? 0m);
        var discount = Round2(line.descuento ?? 0m);
        var vatRate = ResolveVatPercentage(product.porcentaje_iva);
        var taxValue = Round2(subtotal * vatRate / 100m);

        return new XElement("detalle",
            new XElement("codigoPrincipal", SafeText(product.codigo_producto, $"ITEM-{line.id_producto}")),
            OptionalElement("codigoAuxiliar", product.sku_alterno),
            new XElement("descripcion", SafeText(line.concepto_item ?? product.nombre_producto, "ITEM")),
            new XElement("cantidad", DecimalText(line.cantidad)),
            new XElement("precioUnitario", DecimalText(unitPrice)),
            new XElement("descuento", DecimalText(discount)),
            new XElement("precioTotalSinImpuesto", DecimalText(subtotal)),
            new XElement("impuestos",
                new XElement("impuesto",
                    new XElement("codigo", "2"),
                    new XElement("codigoPorcentaje", MapVatCode(vatRate)),
                    new XElement("tarifa", DecimalText(vatRate)),
                    new XElement("baseImponible", DecimalText(subtotal)),
                    new XElement("valor", DecimalText(taxValue)))));
    }

    private static XElement BuildCreditNoteLineElement(tbl_detalle_nota_credito line)
    {
        var product = line.id_detalle_ventaNavigation.id_productoNavigation;
        var vatRate = ResolveVatPercentage(line.porcentaje_impuesto);
        var quantity = line.cantidad_acreditada.GetValueOrDefault(1);
        var unitPrice = quantity > 0 ? line.monto_subtotal / quantity : line.monto_subtotal;

        return new XElement("detalle",
            new XElement("codigoInterno", SafeText(product.codigo_producto, $"ITEM-{line.id_detalle_venta}")),
            OptionalElement("codigoAdicional", product.sku_alterno),
            new XElement("descripcion", SafeText(line.descripcion_item ?? product.nombre_producto, "ITEM")),
            new XElement("cantidad", DecimalText(quantity)),
            new XElement("precioUnitario", DecimalText(unitPrice)),
            new XElement("descuento", DecimalText(0m)),
            new XElement("precioTotalSinImpuesto", DecimalText(line.monto_subtotal)),
            new XElement("impuestos",
                new XElement("impuesto",
                    new XElement("codigo", "2"),
                    new XElement("codigoPorcentaje", MapVatCode(vatRate)),
                    new XElement("tarifa", DecimalText(vatRate)),
                    new XElement("baseImponible", DecimalText(line.monto_subtotal)),
                    new XElement("valor", DecimalText(line.monto_impuesto)))));
    }

    private static XElement BuildInvoiceAdditionalInfo(ClientEntity? client, tbl_venta sale)
    {
        var items = new List<XElement>();
        AddAdditionalField(items, "Email", client?.correo_electronico ?? sale.id_pacienteNavigation.email);
        AddAdditionalField(items, "Telefono", client?.telefono ?? sale.id_pacienteNavigation.telefono);
        AddAdditionalField(items, "Paciente", $"{sale.id_pacienteNavigation.nombres} {sale.id_pacienteNavigation.apellidos}".Trim());
        AddAdditionalField(items, "FormaPago", sale.forma_pago);

        return items.Count == 0 ? new XElement("infoAdicional") : new XElement("infoAdicional", items);
    }

    private static XElement BuildCreditNoteAdditionalInfo(ClientEntity? client, tbl_paciente patient)
    {
        var items = new List<XElement>();
        AddAdditionalField(items, "Email", client?.correo_electronico ?? patient.email);
        AddAdditionalField(items, "Telefono", client?.telefono ?? patient.telefono);
        AddAdditionalField(items, "Paciente", $"{patient.nombres} {patient.apellidos}".Trim());
        return items.Count == 0 ? new XElement("infoAdicional") : new XElement("infoAdicional", items);
    }

    private static void AddAdditionalField(ICollection<XElement> fields, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields.Add(new XElement("campoAdicional", new XAttribute("nombre", name), SafeText(value, string.Empty)));
    }

    private static SignOutcome TrySignXml(string xml, EmisorEntity issuer)
    {
        var certificatePath = issuer.certificado_digital_ruta?.Trim();
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return SignOutcome.NoCertificate("No hay ruta de certificado configurada para el emisor.");
        }

        if (!Path.IsPathRooted(certificatePath))
        {
            return SignOutcome.NoCertificate("La ruta del certificado digital debe ser absoluta.");
        }

        if (!File.Exists(certificatePath))
        {
            return SignOutcome.NoCertificate("No se encontró el certificado digital configurado para el emisor.");
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                issuer.certificado_digital_clave,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

            var xmlDocument = new XmlDocument { PreserveWhitespace = true };
            xmlDocument.LoadXml(xml);

            var signatureId = $"Signature-{Guid.NewGuid():N}";
            var signedPropertiesId = $"{signatureId}-SignedProperties";
            var documentReferenceId = $"Reference-{Guid.NewGuid():N}";

            var signedXml = new FlexibleIdSignedXml(xmlDocument)
            {
                Signature = { Id = signatureId }
            };

            signedXml.SigningKey = certificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("El certificado no contiene una clave privada RSA utilizable.");

            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

            var documentReference = new Reference
            {
                Uri = "#comprobante",
                Id = documentReferenceId,
                DigestMethod = SignedXml.XmlDsigSHA256Url
            };
            documentReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            documentReference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(documentReference);

            var qualifyingProperties = BuildQualifyingProperties(xmlDocument, signatureId, signedPropertiesId, documentReferenceId, certificate);
            signedXml.AddObject(new DataObject
            {
                Data = qualifyingProperties.ChildNodes,
                Id = $"{signatureId}-Object"
            });

            var signedPropertiesReference = new Reference
            {
                Uri = $"#{signedPropertiesId}",
                Type = "http://uri.etsi.org/01903#SignedProperties",
                DigestMethod = SignedXml.XmlDsigSHA256Url
            };
            signedPropertiesReference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(signedPropertiesReference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();
            var xmlSignature = signedXml.GetXml();

            xmlDocument.DocumentElement?.AppendChild(xmlDocument.ImportNode(xmlSignature, true));
            return SignOutcome.Successful(xmlDocument.OuterXml);
        }
        catch (Exception ex)
        {
            return SignOutcome.NoCertificate($"No se pudo firmar el XML con el certificado configurado: {ex.Message}");
        }
    }

    private static XmlElement BuildQualifyingProperties(
        XmlDocument document,
        string signatureId,
        string signedPropertiesId,
        string documentReferenceId,
        X509Certificate2 certificate)
    {
        const string dsNs = SignedXml.XmlDsigNamespaceUrl;
        const string etsiNs = "http://uri.etsi.org/01903/v1.3.2#";

        var objectElement = document.CreateElement("ds", "Object", dsNs);
        var qualifyingProperties = document.CreateElement("etsi", "QualifyingProperties", etsiNs);
        qualifyingProperties.SetAttribute("Target", $"#{signatureId}");

        var signedProperties = document.CreateElement("etsi", "SignedProperties", etsiNs);
        signedProperties.SetAttribute("Id", signedPropertiesId);

        var signedSignatureProperties = document.CreateElement("etsi", "SignedSignatureProperties", etsiNs);
        var signingTime = document.CreateElement("etsi", "SigningTime", etsiNs);
        signingTime.InnerText = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        signedSignatureProperties.AppendChild(signingTime);

        var signingCertificate = document.CreateElement("etsi", "SigningCertificate", etsiNs);
        var cert = document.CreateElement("etsi", "Cert", etsiNs);
        var certDigest = document.CreateElement("etsi", "CertDigest", etsiNs);
        var digestMethod = document.CreateElement("ds", "DigestMethod", dsNs);
        digestMethod.SetAttribute("Algorithm", SignedXml.XmlDsigSHA256Url);
        var digestValue = document.CreateElement("ds", "DigestValue", dsNs);
        digestValue.InnerText = Convert.ToBase64String(SHA256.HashData(certificate.RawData));
        certDigest.AppendChild(digestMethod);
        certDigest.AppendChild(digestValue);

        var issuerSerial = document.CreateElement("etsi", "IssuerSerial", etsiNs);
        var issuerName = document.CreateElement("ds", "X509IssuerName", dsNs);
        issuerName.InnerText = certificate.Issuer;
        var serialNumber = document.CreateElement("ds", "X509SerialNumber", dsNs);
        serialNumber.InnerText = certificate.SerialNumber;
        issuerSerial.AppendChild(issuerName);
        issuerSerial.AppendChild(serialNumber);

        cert.AppendChild(certDigest);
        cert.AppendChild(issuerSerial);
        signingCertificate.AppendChild(cert);
        signedSignatureProperties.AppendChild(signingCertificate);

        var signedDataObjectProperties = document.CreateElement("etsi", "SignedDataObjectProperties", etsiNs);
        var dataObjectFormat = document.CreateElement("etsi", "DataObjectFormat", etsiNs);
        dataObjectFormat.SetAttribute("ObjectReference", $"#{documentReferenceId}");
        var description = document.CreateElement("etsi", "Description", etsiNs);
        description.InnerText = "contenido comprobante";
        var mimeType = document.CreateElement("etsi", "MimeType", etsiNs);
        mimeType.InnerText = "text/xml";
        dataObjectFormat.AppendChild(description);
        dataObjectFormat.AppendChild(mimeType);
        signedDataObjectProperties.AppendChild(dataObjectFormat);

        signedProperties.AppendChild(signedSignatureProperties);
        signedProperties.AppendChild(signedDataObjectProperties);
        qualifyingProperties.AppendChild(signedProperties);
        objectElement.AppendChild(qualifyingProperties);
        return objectElement;
    }

    private string SaveXml(string folderName, string documentNumber, DateTime issueDate, string xml)
    {
        var safeDocumentNumber = new string(documentNumber.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(safeDocumentNumber))
        {
            safeDocumentNumber = $"{folderName}-{issueDate:yyyyMMddHHmmss}";
        }

        var relativeFolder = Path.Combine("generated", "electronic", folderName, issueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var absoluteFolder = Path.Combine(environment.WebRootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var fileName = $"{safeDocumentNumber}.xml";
        var absolutePath = Path.Combine(absoluteFolder, fileName);
        File.WriteAllText(absolutePath, xml, new UTF8Encoding(false));
        return "/" + Path.Combine(relativeFolder, fileName).Replace('\\', '/');
    }

    private static async Task<EmisorEntity?> ResolveIssuerAsync(OpticaDbContext dbContext, int? issuerId, CancellationToken cancellationToken)
    {
        if (issuerId.HasValue && issuerId.Value > 0)
        {
            return await dbContext.emisor.FirstOrDefaultAsync(x => x.emisor_id == issuerId.Value, cancellationToken);
        }

        return await dbContext.emisor
            .OrderByDescending(x => x.fecha_actualizacion ?? x.fecha_creacion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string BuildAccessKey(DateTime issueDate, string documentCode, EmisorEntity issuer, long sequence, string numericCode)
    {
        var raw = string.Concat(
            issueDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
            documentCode,
            DigitsOnly(issuer.ruc, 13),
            NormalizeCode(issuer.ambiente_codigo, 1, "1"),
            NormalizeCode(issuer.establecimiento_codigo, 3, "001"),
            NormalizeCode(issuer.punto_emision_codigo, 3, "001"),
            sequence.ToString("D9", CultureInfo.InvariantCulture),
            numericCode,
            NormalizeCode(issuer.tipo_emision_codigo, 1, "1"));

        var verifier = ComputeModulo11(raw);
        return raw + verifier.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildNumericCode(int firstSeed, int secondSeed)
    {
        var value = Math.Abs(HashCode.Combine(firstSeed, secondSeed, DateTime.UtcNow.Minute)) % 100000000;
        return value.ToString("D8", CultureInfo.InvariantCulture);
    }

    private static string ExtractNumericCode(string accessKey)
    {
        return accessKey.Length >= 47 ? accessKey.Substring(39, 8) : "00000000";
    }

    private static int ComputeModulo11(string raw)
    {
        var factor = 2;
        var total = 0;
        for (var index = raw.Length - 1; index >= 0; index--)
        {
            total += (raw[index] - '0') * factor;
            factor++;
            if (factor > 7)
            {
                factor = 2;
            }
        }

        var remainder = total % 11;
        var digit = 11 - remainder;
        return digit switch
        {
            11 => 0,
            10 => 1,
            _ => digit
        };
    }

    private static string ComputeSha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeBuyerIdentificationType(ClientEntity? client)
    {
        var type = client?.tipo_identificacion?.Trim();
        return type switch
        {
            "04" or "05" or "06" or "07" or "08" => type,
            _ => "05"
        };
    }

    private static string NormalizeBuyerName(ClientEntity? client, tbl_paciente patient)
    {
        if (!string.IsNullOrWhiteSpace(client?.razon_social))
        {
            return SafeText(client.razon_social, "CONSUMIDOR FINAL");
        }

        return SafeText($"{patient.nombres} {patient.apellidos}".Trim(), "CONSUMIDOR FINAL");
    }

    private static string NormalizeBuyerIdentification(ClientEntity? client, tbl_paciente patient)
    {
        var raw = client?.numero_identificacion ?? patient.cedula;
        var digits = new string((raw ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? "9999999999999" : digits;
    }

    private static string MapPaymentCode(string? paymentMethod)
    {
        var normalized = paymentMethod?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "efectivo" => "01",
            "cheque" => "20",
            "transferencia" => "20",
            "tarjeta debito" => "16",
            "tarjeta credito" => "19",
            "credito" => "20",
            _ => "20"
        };
    }

    private static string MapVatCode(decimal vatRate)
    {
        vatRate = Round2(vatRate);
        return vatRate switch
        {
            0m => "0",
            5m => "5",
            12m => "2",
            13m => "3",
            14m => "3",
            15m => "4",
            _ => "0"
        };
    }

    private static decimal ResolveVatPercentage(decimal? vatRate)
    {
        return Round2(vatRate ?? 0m);
    }

    private static string DecimalText(decimal? value)
    {
        return Round2(value ?? 0m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal Round2(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string SafeText(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > 300 ? normalized[..300] : normalized;
    }

    private static string DigitsOnly(string? value, int expectedLength)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= expectedLength ? digits[..expectedLength] : digits.PadLeft(expectedLength, '0');
    }

    private static string NormalizeCode(string? value, int expectedLength, string fallback)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            digits = fallback;
        }

        return digits.Length >= expectedLength ? digits[^expectedLength..] : digits.PadLeft(expectedLength, '0');
    }

    private static XElement? OptionalElement(string? name, string? value)
    {
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)
            ? null
            : new XElement(name, SafeText(value, string.Empty));
    }

    private static string SanitizeDocumentNumber(string? number)
    {
        var clean = new string((number ?? string.Empty).Where(ch => char.IsDigit(ch) || ch == '-').ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "001-001-000000001" : clean;
    }

    private static long? ParseTrailingSequence(string? documentNumber)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return null;
        }

        var digits = new string(documentNumber.Split('-').Last().Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var sequence) ? sequence : null;
    }

    public sealed class ElectronicDocumentResult
    {
        public string UnsignedXml { get; set; } = string.Empty;
        public string SignedXml { get; set; } = string.Empty;
        public string XmlPath { get; set; } = string.Empty;
        public string HashHex { get; set; } = string.Empty;
        public string StatusCode { get; set; } = "PENDIENTE_FIRMA";
        public string Message { get; set; } = string.Empty;
        public DateTime? SignedAt { get; set; }

        public static ElectronicDocumentResult Error(string message)
        {
            return new ElectronicDocumentResult
            {
                StatusCode = "ERROR_XML",
                Message = message
            };
        }
    }

    private sealed class SignOutcome
    {
        public bool Success { get; private init; }
        public string? SignedXml { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static SignOutcome Successful(string signedXml) => new() { Success = true, SignedXml = signedXml };
        public static SignOutcome NoCertificate(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
    }

    private sealed class FlexibleIdSignedXml : SignedXml
    {
        public FlexibleIdSignedXml(XmlDocument document) : base(document)
        {
        }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            var fromBase = base.GetIdElement(document, idValue);
            if (fromBase is not null || document is null)
            {
                return fromBase;
            }

            return document.SelectSingleNode($"//*[@Id='{idValue}' or @id='{idValue}']") as XmlElement;
        }
    }
}
