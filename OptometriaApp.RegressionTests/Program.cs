using OptometriaApp.Models;
using OptometriaApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

var failures = new List<string>();

Run("FilterGoods excludes services", () =>
{
    var products = new List<tbl_producto>
    {
        new() { id_producto = 1, nombre_producto = "Marco", tipo_item = ProductInventoryRules.ProductType, naturaleza_item = ProductInventoryRules.GoodNature },
        new() { id_producto = 2, nombre_producto = "Consulta", tipo_item = ProductInventoryRules.ProductType, naturaleza_item = ProductInventoryRules.ServiceNature },
        new() { id_producto = 3, nombre_producto = "Lente", tipo_item = ProductInventoryRules.ProductType, naturaleza_item = null }
    };

    var result = ProductInventoryRules.FilterGoods(products.AsQueryable())
        .Select(x => x.id_producto)
        .OrderBy(x => x)
        .ToList();

    AssertSequence(result, [1, 3], "Solo los bienes deben pasar el filtro inventariable.");
});

Run("NormalizeInventoryFields clears service inventory data", () =>
{
    var service = new tbl_producto
    {
        id_producto = 5,
        tipo_item = ProductInventoryRules.ProductType,
        naturaleza_item = ProductInventoryRules.ServiceNature,
        stock_actual = 12,
        stock_minimo = 2,
        stock_maximo = 20,
        punto_reorden = 4,
        cantidad_empaque = 1,
        almacen = "A1",
        pasillo = "P2",
        estante = "E3",
        nivel = "N4",
        requiere_lote = true,
        requiere_fecha_vencimiento = true,
        dias_vencimiento = 30
    };

    ProductInventoryRules.NormalizeInventoryFields(service);

    Assert(service.stock_actual == 0, "El stock actual de un servicio debe quedar en 0.");
    Assert(service.stock_minimo == 0, "El stock minimo de un servicio debe quedar en 0.");
    Assert(service.stock_maximo == 0, "El stock maximo de un servicio debe quedar en 0.");
    Assert(service.punto_reorden == 0, "El punto de reorden de un servicio debe quedar en 0.");
    Assert(service.cantidad_empaque == 0, "La cantidad de empaque de un servicio debe quedar en 0.");
    Assert(service.requiere_lote == false, "Un servicio no debe requerir lote.");
    Assert(service.requiere_fecha_vencimiento == false, "Un servicio no debe requerir vencimiento.");
    Assert(string.IsNullOrEmpty(service.almacen), "Un servicio no debe conservar ubicacion de almacen.");
});

Run("IsStoreVisible requires active priced goods", () =>
{
    var visibleGood = new tbl_producto
    {
        tipo_item = ProductInventoryRules.ProductType,
        naturaleza_item = ProductInventoryRules.GoodNature,
        activo = true,
        precio_venta = 10
    };

    var invisibleService = new tbl_producto
    {
        tipo_item = ProductInventoryRules.ProductType,
        naturaleza_item = ProductInventoryRules.ServiceNature,
        activo = true,
        precio_venta = 10
    };

    var invisibleFreeGood = new tbl_producto
    {
        tipo_item = ProductInventoryRules.ProductType,
        naturaleza_item = ProductInventoryRules.GoodNature,
        activo = true,
        precio_venta = 0
    };

    Assert(ProductInventoryRules.IsStoreVisible(visibleGood), "Un bien activo con precio valido debe mostrarse en tienda.");
    Assert(!ProductInventoryRules.IsStoreVisible(invisibleService), "Un servicio no debe mostrarse en tienda.");
    Assert(!ProductInventoryRules.IsStoreVisible(invisibleFreeGood), "Un bien sin precio valido no debe mostrarse en tienda.");
});

Run("FindNonInventoryProductIds returns service ids", () =>
{
    var products = new List<tbl_producto>
    {
        new() { id_producto = 7, naturaleza_item = ProductInventoryRules.GoodNature },
        new() { id_producto = 8, naturaleza_item = ProductInventoryRules.ServiceNature }
    };

    var result = ProductInventoryRules.FindNonInventoryProductIds(products, [7, 8, 8]);
    AssertSequence(result, [8], "Solo los ids de servicios deben marcarse como invalidos para inventario.");
});

await RunAsync("SRI SOAP flow parses authorized documents", async () =>
{
    var handler = new QueuedHttpMessageHandler(
        SoapReception("RECIBIDA"),
        SoapAuthorization("AUTORIZADO", "1234567890123456789012345678901234567890123456789"));
    var client = CreateSriClient(handler);

    var result = await client.ProcessAsync("<factura id=\"comprobante\"/>", "1234567890123456789012345678901234567890123456789", "1");

    Assert(result.StatusCode == "AUTORIZADO", "El estado autorizado del SRI debe conservarse.");
    Assert(result.AuthorizationNumber?.Length == 49, "Debe recuperarse el numero de autorizacion.");
    Assert(result.AuthorizedXml?.Contains("<estado>AUTORIZADO</estado>", StringComparison.Ordinal) == true, "Debe conservarse el XML de autorizacion.");
});

await RunAsync("SRI SOAP flow preserves rejection details", async () =>
{
    var handler = new QueuedHttpMessageHandler(SoapRejectedReception());
    var client = CreateSriClient(handler);

    var result = await client.ProcessAsync("<factura id=\"comprobante\"/>", "1234567890123456789012345678901234567890123456789", "1");

    Assert(result.StatusCode == "DEVUELTA", "Una recepcion devuelta no debe tratarse como pendiente.");
    Assert(result.Message.Contains("Codigo 35", StringComparison.Ordinal), "Debe preservarse el codigo de error del SRI.");
    Assert(result.Message.Contains("DOCUMENTO INVALIDO", StringComparison.Ordinal), "Debe preservarse el detalle del rechazo.");
});

await RunAsync("SRI retry queries an already registered access key", async () =>
{
    var handler = new QueuedHttpMessageHandler(
        SoapRegisteredReception(),
        SoapAuthorization("AUTORIZADO", "1234567890123456789012345678901234567890123456789"));
    var client = CreateSriClient(handler);

    var result = await client.ProcessAsync("<factura id=\"comprobante\"/>", "1234567890123456789012345678901234567890123456789", "1");

    Assert(result.StatusCode == "AUTORIZADO", "La clave ya registrada debe consultarse en autorizacion y no quedar como devuelta.");
});

if (failures.Count > 0)
{
    Console.Error.WriteLine("Regression checks failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($" - {failure}");
    }

    return 1;
}

Console.WriteLine("All regression checks passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

SriElectronicDocumentClient CreateSriClient(HttpMessageHandler handler)
{
    var configuration = new ConfigurationBuilder().Build();
    return new SriElectronicDocumentClient(new HttpClient(handler), configuration, NullLogger<SriElectronicDocumentClient>.Instance);
}

string SoapReception(string status) => $"""
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body><validarComprobanteResponse><RespuestaRecepcionComprobante><estado>{status}</estado><comprobantes /></RespuestaRecepcionComprobante></validarComprobanteResponse></soap:Body>
</soap:Envelope>
""";

string SoapAuthorization(string status, string authorizationNumber) => $"""
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body><autorizacionComprobanteResponse><RespuestaAutorizacionComprobante><numeroComprobantes>1</numeroComprobantes><autorizaciones><autorizacion><estado>{status}</estado><numeroAutorizacion>{authorizationNumber}</numeroAutorizacion><fechaAutorizacion>2026-08-16T10:30:00-05:00</fechaAutorizacion><comprobante>&lt;factura/&gt;</comprobante><mensajes /></autorizacion></autorizaciones></RespuestaAutorizacionComprobante></autorizacionComprobanteResponse></soap:Body>
</soap:Envelope>
""";

string SoapRejectedReception() => """
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body><validarComprobanteResponse><RespuestaRecepcionComprobante><estado>DEVUELTA</estado><comprobantes><comprobante><mensajes><mensaje><identificador>35</identificador><mensaje>DOCUMENTO INVALIDO</mensaje><informacionAdicional>Error de esquema</informacionAdicional><tipo>ERROR</tipo></mensaje></mensajes></comprobante></comprobantes></RespuestaRecepcionComprobante></validarComprobanteResponse></soap:Body>
</soap:Envelope>
""";

string SoapRegisteredReception() => """
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body><validarComprobanteResponse><RespuestaRecepcionComprobante><estado>DEVUELTA</estado><comprobantes><comprobante><mensajes><mensaje><identificador>43</identificador><mensaje>CLAVE ACCESO REGISTRADA</mensaje><tipo>ERROR</tipo></mensaje></mensajes></comprobante></comprobantes></RespuestaRecepcionComprobante></validarComprobanteResponse></soap:Body>
</soap:Envelope>
""";

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertSequence(IReadOnlyList<int> actual, IReadOnlyList<int> expected, string message)
{
    if (actual.Count != expected.Count)
    {
        throw new InvalidOperationException($"{message} Esperado={string.Join(",", expected)} Actual={string.Join(",", actual)}");
    }

    for (var i = 0; i < actual.Count; i++)
    {
        if (actual[i] != expected[i])
        {
            throw new InvalidOperationException($"{message} Esperado={string.Join(",", expected)} Actual={string.Join(",", actual)}");
        }
    }
}

sealed class QueuedHttpMessageHandler(params string[] responses) : HttpMessageHandler
{
    private readonly Queue<string> responses = new(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No hay una respuesta SOAP preparada para la solicitud.");
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/xml")
        });
    }
}
