using OptometriaApp.Models;

namespace OptometriaApp.Services;

public static class ProductInventoryRules
{
    public const string ProductType = "Producto";
    public const string GoodNature = "Bien";
    public const string ServiceNature = "Servicio";

    public static string NormalizeNature(string? naturalezaItem)
    {
        return string.Equals(naturalezaItem?.Trim(), ServiceNature, StringComparison.OrdinalIgnoreCase)
            ? ServiceNature
            : GoodNature;
    }

    public static bool IsService(tbl_producto product)
    {
        return string.Equals(NormalizeNature(product.naturaleza_item), ServiceNature, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStockManaged(tbl_producto product)
    {
        return !IsService(product);
    }

    public static bool IsGoodProduct(tbl_producto product)
    {
        return !IsService(product);
    }

    public static bool IsStoreVisible(tbl_producto product)
    {
        return IsGoodProduct(product) &&
               product.activo != false &&
               product.precio_venta > 0;
    }

    public static IQueryable<tbl_producto> FilterGoods(IQueryable<tbl_producto> query)
    {
        return query.Where(product =>
            (product.tipo_item ?? ProductType) == ProductType &&
            (product.naturaleza_item == null || product.naturaleza_item == "" || product.naturaleza_item == GoodNature));
    }

    public static IQueryable<tbl_producto> FilterStoreVisible(IQueryable<tbl_producto> query)
    {
        return FilterGoods(query)
            .Where(product => (product.activo ?? true) && product.precio_venta > 0);
    }

    public static void NormalizeInventoryFields(tbl_producto product)
    {
        product.tipo_item = ProductType;
        product.naturaleza_item = NormalizeNature(product.naturaleza_item);

        if (IsGoodProduct(product))
        {
            if ((product.cantidad_empaque ?? 0) <= 0)
            {
                product.cantidad_empaque = 1;
            }

            return;
        }

        product.stock_actual = 0;
        product.stock_minimo = 0;
        product.stock_maximo = 0;
        product.punto_reorden = 0;
        product.cantidad_empaque = 0;
        product.almacen = null;
        product.pasillo = null;
        product.estante = null;
        product.nivel = null;
        product.peso_unitario = 0;
        product.dimensiones_largo = 0;
        product.dimensiones_ancho = 0;
        product.dimensiones_alto = 0;
        product.volumen_m3 = 0;
        product.requiere_lote = false;
        product.requiere_fecha_vencimiento = false;
        product.dias_vencimiento = 0;
        product.fecha_ultima_compra = null;
        product.cantidad_movimientos_mes = 0;
    }

    public static IReadOnlyList<int> FindNonInventoryProductIds(IEnumerable<tbl_producto> products, IEnumerable<int> selectedProductIds)
    {
        var selectedIds = selectedProductIds.Distinct().ToHashSet();
        return products
            .Where(product => selectedIds.Contains(product.id_producto) && !IsGoodProduct(product))
            .Select(product => product.id_producto)
            .Distinct()
            .ToList();
    }
}
