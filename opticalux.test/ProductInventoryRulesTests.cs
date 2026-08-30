using OptometriaApp.Models;
using OptometriaApp.Services;
using Xunit;

namespace Opticalux.Test;

public class ProductInventoryRulesTests
{
    [Theory]
    [InlineData(null, ProductInventoryRules.GoodNature)]
    [InlineData("", ProductInventoryRules.GoodNature)]
    [InlineData("Bien", ProductInventoryRules.GoodNature)]
    [InlineData("Servicio", ProductInventoryRules.ServiceNature)]
    [InlineData(" servicio ", ProductInventoryRules.ServiceNature)]
    public void NormalizeNature_ReturnsExpectedNature(string? nature, string expected)
    {
        var result = ProductInventoryRules.NormalizeNature(nature);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FilterGoods_ExcludesServices()
    {
        var products = new List<tbl_producto>
        {
            new() { id_producto = 1, tipo_item = ProductInventoryRules.ProductType, naturaleza_item = ProductInventoryRules.GoodNature },
            new() { id_producto = 2, tipo_item = ProductInventoryRules.ProductType, naturaleza_item = ProductInventoryRules.ServiceNature },
            new() { id_producto = 3, tipo_item = ProductInventoryRules.ProductType, naturaleza_item = null }
        };

        var result = ProductInventoryRules.FilterGoods(products.AsQueryable())
            .Select(product => product.id_producto)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([1, 3], result);
    }

    [Fact]
    public void NormalizeInventoryFields_ClearsInventoryDataForServices()
    {
        var service = new tbl_producto
        {
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
            peso_unitario = 2,
            dimensiones_largo = 3,
            dimensiones_ancho = 4,
            dimensiones_alto = 5,
            volumen_m3 = 6,
            requiere_lote = true,
            requiere_fecha_vencimiento = true,
            dias_vencimiento = 30,
            fecha_ultima_compra = DateTime.Today,
            cantidad_movimientos_mes = 9
        };

        ProductInventoryRules.NormalizeInventoryFields(service);

        Assert.Equal(0, service.stock_actual);
        Assert.Equal(0, service.stock_minimo);
        Assert.Equal(0, service.stock_maximo);
        Assert.Equal(0, service.punto_reorden);
        Assert.Equal(0, service.cantidad_empaque);
        Assert.Null(service.almacen);
        Assert.Null(service.pasillo);
        Assert.Null(service.estante);
        Assert.Null(service.nivel);
        Assert.Equal(0, service.peso_unitario);
        Assert.Equal(0, service.dimensiones_largo);
        Assert.Equal(0, service.dimensiones_ancho);
        Assert.Equal(0, service.dimensiones_alto);
        Assert.Equal(0, service.volumen_m3);
        Assert.False(service.requiere_lote);
        Assert.False(service.requiere_fecha_vencimiento);
        Assert.Equal(0, service.dias_vencimiento);
        Assert.Null(service.fecha_ultima_compra);
        Assert.Equal(0, service.cantidad_movimientos_mes);
    }

    [Fact]
    public void NormalizeInventoryFields_AssignsDefaultPackageQuantityForGoods()
    {
        var product = new tbl_producto
        {
            tipo_item = ProductInventoryRules.ProductType,
            naturaleza_item = ProductInventoryRules.GoodNature,
            cantidad_empaque = 0
        };

        ProductInventoryRules.NormalizeInventoryFields(product);

        Assert.Equal(ProductInventoryRules.ProductType, product.tipo_item);
        Assert.Equal(ProductInventoryRules.GoodNature, product.naturaleza_item);
        Assert.Equal(1, product.cantidad_empaque);
    }

    [Fact]
    public void IsStoreVisible_RequiresActivePricedGoods()
    {
        var visibleGood = new tbl_producto
        {
            naturaleza_item = ProductInventoryRules.GoodNature,
            activo = true,
            precio_venta = 10
        };

        var invisibleService = new tbl_producto
        {
            naturaleza_item = ProductInventoryRules.ServiceNature,
            activo = true,
            precio_venta = 10
        };

        var invisibleFreeGood = new tbl_producto
        {
            naturaleza_item = ProductInventoryRules.GoodNature,
            activo = true,
            precio_venta = 0
        };

        Assert.True(ProductInventoryRules.IsStoreVisible(visibleGood));
        Assert.False(ProductInventoryRules.IsStoreVisible(invisibleService));
        Assert.False(ProductInventoryRules.IsStoreVisible(invisibleFreeGood));
    }

    [Fact]
    public void FindNonInventoryProductIds_ReturnsDistinctServiceIds()
    {
        var products = new List<tbl_producto>
        {
            new() { id_producto = 7, naturaleza_item = ProductInventoryRules.GoodNature },
            new() { id_producto = 8, naturaleza_item = ProductInventoryRules.ServiceNature },
            new() { id_producto = 9, naturaleza_item = ProductInventoryRules.ServiceNature }
        };

        var result = ProductInventoryRules.FindNonInventoryProductIds(products, [7, 8, 8]);

        Assert.Equal([8], result);
    }
}
