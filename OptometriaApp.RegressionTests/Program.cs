using OptometriaApp.Models;
using OptometriaApp.Services;

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
