using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class InventoryInsightsService
{
    public async Task<InventoryInsightsSnapshot> BuildAsync(
        OpticaDbContext dbContext,
        InventoryInsightsFilters filters,
        CancellationToken cancellationToken = default)
    {
        var endDateTime = filters.CutoffDate.ToDateTime(TimeOnly.MaxValue);
        var sold30Start = endDateTime.AddDays(-30);
        var sold60Start = endDateTime.AddDays(-60);
        var sold90Start = endDateTime.AddDays(-90);

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
        var productIds = products.Select(x => x.id_producto).ToHashSet();
        if (productIds.Count == 0)
        {
            return new InventoryInsightsSnapshot(filters, [], [], [], 0, 0, 0);
        }

        var saleDetails = await dbContext.tbl_detalle_venta
            .AsNoTracking()
            .Include(x => x.id_productoNavigation)
            .Include(x => x.id_ventaNavigation)
            .Where(x =>
                productIds.Contains(x.id_producto) &&
                x.id_ventaNavigation.fecha_venta.HasValue &&
                x.id_ventaNavigation.fecha_venta.Value >= sold90Start &&
                x.id_ventaNavigation.fecha_venta.Value <= endDateTime &&
                !string.Equals(x.id_ventaNavigation.estado, "Anulada", StringComparison.OrdinalIgnoreCase))
            .ToListAsync(cancellationToken);

        var metricsByProduct = saleDetails
            .GroupBy(x => x.id_producto)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var sold30 = group.Where(x => x.id_ventaNavigation.fecha_venta >= sold30Start).Sum(x => x.cantidad);
                    var sold60 = group.Where(x => x.id_ventaNavigation.fecha_venta >= sold60Start).Sum(x => x.cantidad);
                    var sold90 = group.Sum(x => x.cantidad);
                    var revenue30 = group.Where(x => x.id_ventaNavigation.fecha_venta >= sold30Start).Sum(x => x.total_item ?? 0m);
                    var lastSale = group
                        .Where(x => x.id_ventaNavigation.fecha_venta.HasValue)
                        .Max(x => x.id_ventaNavigation.fecha_venta);

                    return new ProductSalesMetrics(sold30, sold60, sold90, revenue30, lastSale);
                });

        var restockSuggestions = new List<RestockSuggestionRow>();
        var stagnantProducts = new List<StagnantProductRow>();
        var hotProducts = new List<HotProductRow>();

        foreach (var product in products)
        {
            metricsByProduct.TryGetValue(product.id_producto, out var metrics);
            metrics ??= ProductSalesMetrics.Empty;
            var stock = product.stock_actual ?? 0;
            var minimum = product.stock_minimo ?? 0;
            var reorderPoint = product.punto_reorden ?? (minimum > 0 ? minimum : Math.Max(1, metrics.Sold30 > 0 ? (int)Math.Ceiling(metrics.Sold30 / 2d) : 1));
            var targetStock = product.stock_maximo ?? Math.Max(reorderPoint * 2, metrics.Sold30);
            var suggestedOrder = Math.Max(
                product.cantidad_pedido_optima ?? 0,
                Math.Max(targetStock - stock, reorderPoint - stock));

            var averageDailyDemand = metrics.Sold30 / 30d;
            var coverageDays = averageDailyDemand > 0 ? Math.Round(stock / averageDailyDemand, 1) : (double?)null;
            var priority = GetPriority(stock, reorderPoint, metrics.Sold30, coverageDays);
            var daysWithoutSales = metrics.LastSaleAt.HasValue
                ? (int)Math.Floor((endDateTime.Date - metrics.LastSaleAt.Value.Date).TotalDays)
                : 999;

            if (stock <= reorderPoint || (metrics.Sold30 > 0 && coverageDays is not null && coverageDays <= 14))
            {
                restockSuggestions.Add(new RestockSuggestionRow(
                    product.id_producto,
                    product.nombre_producto,
                    product.id_categoriaNavigation?.nombre ?? "Sin categoria",
                    product.id_proveedorNavigation?.nombre ?? "Sin proveedor",
                    stock,
                    minimum,
                    reorderPoint,
                    suggestedOrder <= 0 ? Math.Max(1, reorderPoint - stock + Math.Max(1, metrics.Sold30)) : suggestedOrder,
                    metrics.Sold30,
                    coverageDays,
                    priority));
            }

            if (daysWithoutSales >= 60 || (metrics.Sold90 <= 1 && stock > 0))
            {
                stagnantProducts.Add(new StagnantProductRow(
                    product.id_producto,
                    product.nombre_producto,
                    product.id_categoriaNavigation?.nombre ?? "Sin categoria",
                    stock,
                    metrics.Sold90,
                    metrics.LastSaleAt?.ToString("yyyy-MM-dd") ?? "Sin ventas",
                    daysWithoutSales >= 999 ? "Nunca vendido" : $"{daysWithoutSales} dias sin venta",
                    stock > minimum ? "Promocionar o pausar compra" : "Mantener bajo observacion"));
            }

            if (metrics.Sold30 > 0)
            {
                hotProducts.Add(new HotProductRow(
                    product.id_producto,
                    product.nombre_producto,
                    metrics.Sold30,
                    metrics.Revenue30,
                    stock,
                    coverageDays,
                    stock <= reorderPoint ? "Alto riesgo de quiebre" : "Buena salida"));
            }
        }

        restockSuggestions = restockSuggestions
            .OrderByDescending(x => GetPriorityWeight(x.Priority))
            .ThenBy(x => x.CoverageDays ?? double.MaxValue)
            .ThenBy(x => x.CurrentStock)
            .Take(12)
            .ToList();

        stagnantProducts = stagnantProducts
            .OrderByDescending(x => ParseDaysWithoutSales(x.DaysWithoutSalesLabel))
            .ThenByDescending(x => x.CurrentStock)
            .Take(12)
            .ToList();

        hotProducts = hotProducts
            .OrderByDescending(x => x.SoldLast30Days)
            .ThenBy(x => x.CoverageDays ?? double.MaxValue)
            .Take(8)
            .ToList();

        return new InventoryInsightsSnapshot(
            filters,
            restockSuggestions,
            stagnantProducts,
            hotProducts,
            restockSuggestions.Count(x => x.Priority == "Critica"),
            stagnantProducts.Count,
            hotProducts.Count(x => x.CoverageDays is not null && x.CoverageDays <= 14));
    }

    private static string GetPriority(int stock, int reorderPoint, int sold30, double? coverageDays)
    {
        if (stock <= 0)
        {
            return "Critica";
        }

        if (stock <= reorderPoint)
        {
            return "Alta";
        }

        if (sold30 > 0 && coverageDays is not null && coverageDays <= 14)
        {
            return "Media";
        }

        return "Control";
    }

    private static int GetPriorityWeight(string priority) => priority switch
    {
        "Critica" => 4,
        "Alta" => 3,
        "Media" => 2,
        _ => 1
    };

    private static int ParseDaysWithoutSales(string label)
    {
        if (label.StartsWith("Nunca", StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        return int.TryParse(new string(label.TakeWhile(char.IsDigit).ToArray()), out var days) ? days : 0;
    }

    private sealed record ProductSalesMetrics(int Sold30, int Sold60, int Sold90, decimal Revenue30, DateTime? LastSaleAt)
    {
        public static readonly ProductSalesMetrics Empty = new(0, 0, 0, 0m, null);
    }
}

public sealed record InventoryInsightsFilters(DateOnly CutoffDate, int ProductId = 0, int SupplierId = 0, int CategoryId = 0);

public sealed record InventoryInsightsSnapshot(
    InventoryInsightsFilters Filters,
    List<RestockSuggestionRow> RestockSuggestions,
    List<StagnantProductRow> StagnantProducts,
    List<HotProductRow> HotProducts,
    int CriticalRestockCount,
    int StagnantCount,
    int HotRiskCount);

public sealed record RestockSuggestionRow(
    int ProductId,
    string ProductName,
    string Category,
    string Supplier,
    int CurrentStock,
    int MinimumStock,
    int ReorderPoint,
    int SuggestedOrderQty,
    int SoldLast30Days,
    double? CoverageDays,
    string Priority);

public sealed record StagnantProductRow(
    int ProductId,
    string ProductName,
    string Category,
    int CurrentStock,
    int SoldLast90Days,
    string LastSaleDate,
    string DaysWithoutSalesLabel,
    string Recommendation);

public sealed record HotProductRow(
    int ProductId,
    string ProductName,
    int SoldLast30Days,
    decimal RevenueLast30Days,
    int CurrentStock,
    double? CoverageDays,
    string AlertLabel);
