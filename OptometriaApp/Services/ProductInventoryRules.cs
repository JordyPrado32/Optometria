using OptometriaApp.Models;

namespace OptometriaApp.Services;

public static class ProductInventoryRules
{
    public static bool IsStockManaged(tbl_producto product)
    {
        return !string.Equals(product.naturaleza_item, "Servicio", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGoodProduct(tbl_producto product)
    {
        return string.IsNullOrWhiteSpace(product.naturaleza_item) ||
               string.Equals(product.naturaleza_item, "Bien", StringComparison.OrdinalIgnoreCase);
    }
}
