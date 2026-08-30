using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OptometriaApp.Services;

/// <summary>
/// Cliente del esquema off-line de comprobantes electronicos del SRI.
/// Encapsula recepcion, consulta de autorizacion y normalizacion de respuestas SOAP.
/// </summary>
public sealed class SriElectronicDocumentClient
{
    private const string TestBaseUrl = "https://celcer.sri.gob.ec/comprobantes-electronicos-ws";
    private const string ProductionBaseUrl = "https://cel.sri.gob.ec/comprobantes-electronicos-ws";
    private readonly HttpClient httpClient;
    private readonly IConfiguration configuration;
    private readonly ILogger<SriElectronicDocumentClient> logger;

    public SriElectronicDocumentClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SriElectronicDocumentClient> logger)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task<SriProcessingResult> ProcessAsync(
        string signedXml,
        string accessKey,
        string environmentCode,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(signedXml) > 320 * 1024)
        {
            return SriProcessingResult.Failed(
                "ERROR_XML",
                "El comprobante firmado supera el limite de 320 KB admitido por el SRI para envios individuales.");
        }

        try
        {
            var reception = await SendForReceptionAsync(signedXml, environmentCode, cancellationToken);
            var alreadyRegistered = reception.Messages.Any(message => message.Contains("Codigo 43", StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(reception.Status, "RECIBIDA", StringComparison.OrdinalIgnoreCase) && !alreadyRegistered)
            {
                return SriProcessingResult.Failed(
                    string.Equals(reception.Status, "DEVUELTA", StringComparison.OrdinalIgnoreCase) ? "DEVUELTA" : "ERROR_SRI",
                    reception.Messages.Count == 0
                        ? $"El SRI respondio {reception.Status}."
                        : string.Join(Environment.NewLine, reception.Messages));
            }

            var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) };
            SriAuthorizationResponse? authorization = null;
            foreach (var delay in delays)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                authorization = await QueryAuthorizationAsync(accessKey, environmentCode, cancellationToken);
                if (authorization.Status is "AUTORIZADO" or "NO AUTORIZADO")
                {
                    break;
                }
            }

            if (authorization is null || authorization.Status is null or "PPR")
            {
                return new SriProcessingResult
                {
                    StatusCode = "EN_PROCESO",
                    Message = "El SRI recibio el comprobante y continua procesandolo (PPR). Debe consultarse nuevamente con la misma clave de acceso."
                };
            }

            var messages = authorization.Messages.Count == 0
                ? authorization.Status
                : $"{authorization.Status}: {string.Join(Environment.NewLine, authorization.Messages)}";

            return new SriProcessingResult
            {
                StatusCode = authorization.Status == "AUTORIZADO" ? "AUTORIZADO" : "NO_AUTORIZADO",
                Message = messages,
                AuthorizationNumber = authorization.AuthorizationNumber,
                AuthorizedAt = authorization.AuthorizedAt,
                AuthorizedXml = authorization.AuthorizationXml
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SriProcessingResult.Failed("PENDIENTE_SRI", "El SRI no respondio dentro del tiempo configurado. El comprobante puede reenviarse con la misma clave de acceso.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No fue posible conectar con los servicios del SRI para {AccessKey}", accessKey);
            return SriProcessingResult.Failed("PENDIENTE_SRI", $"No fue posible conectar con el SRI: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Respuesta inesperada del SRI para {AccessKey}", accessKey);
            return SriProcessingResult.Failed("ERROR_SRI", $"No se pudo procesar la respuesta del SRI: {ex.Message}");
        }
    }

    public async Task<SriProcessingResult> QueryAsync(
        string accessKey,
        string environmentCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await QueryAuthorizationAsync(accessKey, environmentCode, cancellationToken);
            if (authorization.Status is null or "PPR")
            {
                return SriProcessingResult.Failed("EN_PROCESO", "El comprobante aun se encuentra en procesamiento (PPR) o no registra respuesta de autorizacion.");
            }

            return new SriProcessingResult
            {
                StatusCode = authorization.Status == "AUTORIZADO" ? "AUTORIZADO" : "NO_AUTORIZADO",
                Message = authorization.Messages.Count == 0
                    ? authorization.Status
                    : $"{authorization.Status}: {string.Join(Environment.NewLine, authorization.Messages)}",
                AuthorizationNumber = authorization.AuthorizationNumber,
                AuthorizedAt = authorization.AuthorizedAt,
                AuthorizedXml = authorization.AuthorizationXml
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "No fue posible consultar la autorizacion SRI de {AccessKey}", accessKey);
            return SriProcessingResult.Failed("PENDIENTE_SRI", $"No fue posible consultar el SRI: {ex.Message}");
        }
    }

    private async Task<SriReceptionResponse> SendForReceptionAsync(
        string signedXml,
        string environmentCode,
        CancellationToken cancellationToken)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace reception = "http://ec.gob.sri.ws.recepcion";
        var envelope = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "ec", reception),
                new XElement(soap + "Header"),
                new XElement(soap + "Body",
                    new XElement(reception + "validarComprobante",
                        new XElement("xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml)))))));

        var response = await PostSoapAsync(GetEndpoint(environmentCode, "ReceptionUrl", "RecepcionComprobantesOffline"), envelope, cancellationToken);
        ThrowIfSoapFault(response);
        var result = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "RespuestaRecepcionComprobante")
            ?? throw new InvalidOperationException("La respuesta de recepcion del SRI no contiene RespuestaRecepcionComprobante.");

        return new SriReceptionResponse(
            ChildValue(result, "estado") ?? "DESCONOCIDA",
            ExtractMessages(result));
    }

    private async Task<SriAuthorizationResponse> QueryAuthorizationAsync(
        string accessKey,
        string environmentCode,
        CancellationToken cancellationToken)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace authorization = "http://ec.gob.sri.ws.autorizacion";
        var envelope = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap),
                new XAttribute(XNamespace.Xmlns + "ec", authorization),
                new XElement(soap + "Header"),
                new XElement(soap + "Body",
                    new XElement(authorization + "autorizacionComprobante",
                        new XElement("claveAccesoComprobante", accessKey)))));

        var response = await PostSoapAsync(GetEndpoint(environmentCode, "AuthorizationUrl", "AutorizacionComprobantesOffline"), envelope, cancellationToken);
        ThrowIfSoapFault(response);
        var root = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "RespuestaAutorizacionComprobante")
            ?? throw new InvalidOperationException("La respuesta del SRI no contiene RespuestaAutorizacionComprobante.");
        var authorizationElement = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "autorizacion");
        if (authorizationElement is null)
        {
            return new SriAuthorizationResponse(null, null, null, null, []);
        }

        var status = ChildValue(authorizationElement, "estado")?.Trim().ToUpperInvariant();
        var dateText = ChildValue(authorizationElement, "fechaAutorizacion");
        DateTime? authorizedAt = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate)
            ? parsedDate.LocalDateTime
            : null;

        return new SriAuthorizationResponse(
            status,
            ChildValue(authorizationElement, "numeroAutorizacion"),
            authorizedAt,
            authorizationElement.ToString(SaveOptions.DisableFormatting),
            ExtractMessages(authorizationElement));
    }

    private async Task<XDocument> PostSoapAsync(string url, XDocument envelope, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return XDocument.Parse(content, LoadOptions.PreserveWhitespace);
    }

    private string GetEndpoint(string environmentCode, string settingName, string serviceName)
    {
        var environmentName = environmentCode == "2" ? "Production" : "Test";
        var configured = configuration[$"Sri:{environmentName}:{settingName}"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var baseUrl = environmentCode == "2" ? ProductionBaseUrl : TestBaseUrl;
        return $"{baseUrl}/{serviceName}";
    }

    private static void ThrowIfSoapFault(XDocument response)
    {
        var fault = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "Fault");
        if (fault is null)
        {
            return;
        }

        var detail = ChildValue(fault, "faultstring") ?? fault.Value;
        throw new InvalidOperationException($"El servicio SOAP del SRI devolvio un error: {detail.Trim()}");
    }

    private static List<string> ExtractMessages(XElement root)
    {
        return root.Descendants()
            .Where(x => x.Name.LocalName == "mensaje" && x.Elements().Any())
            .Select(x =>
            {
                var identifier = ChildValue(x, "identificador");
                var message = ChildValue(x, "mensaje");
                var additional = ChildValue(x, "informacionAdicional");
                var type = ChildValue(x, "tipo");
                return string.Join(" | ", new[]
                {
                    string.IsNullOrWhiteSpace(identifier) ? null : $"Codigo {identifier}",
                    type,
                    message,
                    additional
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value;
    }

    private sealed record SriReceptionResponse(string Status, List<string> Messages);
    private sealed record SriAuthorizationResponse(
        string? Status,
        string? AuthorizationNumber,
        DateTime? AuthorizedAt,
        string? AuthorizationXml,
        List<string> Messages);
}

public sealed class SriProcessingResult
{
    public string StatusCode { get; init; } = "PENDIENTE_SRI";
    public string Message { get; init; } = string.Empty;
    public string? AuthorizationNumber { get; init; }
    public DateTime? AuthorizedAt { get; init; }
    public string? AuthorizedXml { get; init; }

    public static SriProcessingResult Failed(string statusCode, string message) => new()
    {
        StatusCode = statusCode,
        Message = message
    };
}
