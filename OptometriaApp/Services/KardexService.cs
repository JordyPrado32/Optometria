using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class KardexService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public KardexService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<KardexRebuildResult> RebuildForProductAsync(
        int productId,
        int? actorUserId = null,
        string reason = "Recalculo individual",
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await RebuildForProductInternalAsync(dbContext, productId, actorUserId, reason, cancellationToken);
    }

    public async Task<KardexRebuildResult> RebuildForProductsAsync(
        IEnumerable<int> productIds,
        int? actorUserId = null,
        string reason = "Recalculo multiple",
        CancellationToken cancellationToken = default)
    {
        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new KardexRebuildResult();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var total = new KardexRebuildResult();
        foreach (var productId in ids)
        {
            var partial = await RebuildForProductInternalAsync(dbContext, productId, actorUserId, reason, cancellationToken);
            total = total.Combine(partial);
        }

        return total;
    }

    public async Task<KardexRebuildResult> RebuildForUserScopeAsync(
        int userId,
        int? actorUserId = null,
        string reason = "Recalculo por usuario",
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var productIds = await dbContext.tbl_movimiento_inventarios
            .AsNoTracking()
            .Where(x => x.id_usuario == userId)
            .Select(x => x.id_producto)
            .Distinct()
            .ToListAsync(cancellationToken);

        var total = new KardexRebuildResult();
        foreach (var productId in productIds)
        {
            var partial = await RebuildForProductInternalAsync(dbContext, productId, actorUserId ?? userId, reason, cancellationToken);
            total = total.Combine(partial);
        }

        return total;
    }

    private static async Task<KardexRebuildResult> RebuildForProductInternalAsync(
        OpticaDbContext dbContext,
        int productId,
        int? actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.tbl_productos.FirstOrDefaultAsync(x => x.id_producto == productId, cancellationToken);
        if (product is null)
        {
            return new KardexRebuildResult();
        }

        var movements = await dbContext.tbl_movimiento_inventarios
            .AsNoTracking()
            .Where(x => x.id_producto == productId)
            .OrderBy(x => x.fecha_movimiento ?? DateTime.MinValue)
            .ThenBy(x => x.id_movimiento_inventario)
            .ToListAsync(cancellationToken);

        var existingKardex = await dbContext.tbl_kardex
            .Where(x => x.id_producto == productId)
            .ToListAsync(cancellationToken);

        if (existingKardex.Count > 0)
        {
            dbContext.tbl_kardex.RemoveRange(existingKardex);
        }

        var currentStock = 0;
        var currentBalance = 0m;
        var currentAverage = product.precio_costo ?? 0m;
        var generatedLines = 0;
        var observedLines = 0;

        var firstMovement = movements.FirstOrDefault();
        if (firstMovement?.stock_anterior is > 0)
        {
            currentStock = firstMovement.stock_anterior.Value;
            if (currentAverage <= 0)
            {
                currentAverage = ResolveIncomingUnitCost(firstMovement, product, 0m);
            }

            currentBalance = DecimalRound(currentStock * currentAverage);
        }

        foreach (var movement in movements)
        {
            var signedQuantity = ResolveSignedQuantity(movement);
            var movementQuantity = Math.Abs(signedQuantity);
            if (movementQuantity == 0)
            {
                continue;
            }

            var previousStock = currentStock;
            var previousBalance = currentBalance;
            var previousAverage = currentAverage > 0 ? currentAverage : (product.precio_costo ?? 0m);
            decimal unitCost;
            decimal movementTotal;
            var newStock = currentStock;
            var newBalance = currentBalance;
            var status = "Registrado";

            if (signedQuantity > 0)
            {
                unitCost = ResolveIncomingUnitCost(movement, product, previousAverage);
                movementTotal = DecimalRound(unitCost * movementQuantity);
                newStock += movementQuantity;
                newBalance = DecimalRound(previousBalance + movementTotal);
                currentAverage = newStock > 0 ? DecimalRound(newBalance / newStock) : 0m;
            }
            else
            {
                unitCost = previousAverage > 0 ? previousAverage : ResolveIncomingUnitCost(movement, product, previousAverage);
                movementTotal = DecimalRound(unitCost * movementQuantity);
                newStock -= movementQuantity;
                newBalance = DecimalRound(previousBalance - movementTotal);

                if (newStock < 0)
                {
                    status = "Observado";
                    observedLines++;
                }

                currentAverage = newStock > 0 ? DecimalRound(newBalance / newStock) : 0m;
            }

            currentStock = newStock;
            currentBalance = newBalance;

            dbContext.tbl_kardex.Add(new tbl_kardex
            {
                id_producto = movement.id_producto,
                id_lote = movement.id_lote,
                numero_lote = movement.numero_lote,
                fecha_movimiento = movement.fecha_movimiento ?? DateTime.Now,
                tipo_movimiento = ResolveKardexMovementType(movement, signedQuantity),
                id_referencia = movement.id_referencia_documento ?? movement.id_movimiento_inventario,
                tipo_referencia = movement.tipo_documento_referencia ?? "Inventario",
                comprobante_numero = movement.comprobante_numero,
                cantidad_movimiento = movementQuantity,
                costo_unitario = unitCost,
                costo_total = movementTotal,
                stock_anterior = previousStock,
                stock_nuevo = newStock,
                saldo_anterior_dinero = previousBalance,
                saldo_nuevo_dinero = newBalance,
                precio_promedio_ponderado = currentAverage,
                metodo_valuacion = "Promedio Ponderado",
                id_usuario_movimiento = movement.id_usuario,
                descripcion_movimiento = BuildDescription(movement, signedQuantity),
                glosa_contable = $"Kardex {reason}",
                cuenta_contable_debito = signedQuantity > 0 ? product.cuenta_contable : null,
                cuenta_contable_credito = signedQuantity < 0 ? product.cuenta_contable : null,
                centro_costo = product.centro_costo,
                estado_kardex = status,
                observaciones = movement.observaciones,
                fecha_creacion = DateTime.Now
            });

            generatedLines++;
        }

        product.stock_actual = currentStock;
        if (currentAverage > 0)
        {
            product.precio_costo = currentAverage;
            product.fecha_actualizacion_precio = DateTime.Now;
        }

        if (actorUserId.HasValue)
        {
            dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
            {
                id_usuario = actorUserId,
                accion = "Recalcular kardex",
                modulo = "Kardex",
                fecha = DateTime.Now,
                detalle = $"ProductoId={productId}; Registros={generatedLines}; Observados={observedLines}; Motivo={reason}"
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new KardexRebuildResult
        {
            ProductsProcessed = 1,
            LinesGenerated = generatedLines,
            ObservedLines = observedLines
        };
    }

    private static int ResolveSignedQuantity(tbl_movimiento_inventario movement)
    {
        if (string.Equals(movement.tipo_movimiento, "Entrada", StringComparison.OrdinalIgnoreCase))
        {
            return movement.cantidad;
        }

        if (string.Equals(movement.tipo_movimiento, "Salida", StringComparison.OrdinalIgnoreCase))
        {
            return movement.cantidad * -1;
        }

        if (movement.stock_anterior.HasValue && movement.stock_resultante.HasValue)
        {
            return movement.stock_resultante.Value - movement.stock_anterior.Value;
        }

        return movement.cantidad;
    }

    private static decimal ResolveIncomingUnitCost(tbl_movimiento_inventario movement, tbl_producto product, decimal fallbackAverage)
    {
        if (movement.costo_unitario.HasValue && movement.costo_unitario.Value > 0)
        {
            return DecimalRound(movement.costo_unitario.Value);
        }

        if (movement.costo_total_movimiento.HasValue && movement.cantidad > 0)
        {
            return DecimalRound(movement.costo_total_movimiento.Value / movement.cantidad);
        }

        if (product.precio_costo.HasValue && product.precio_costo.Value > 0)
        {
            return DecimalRound(product.precio_costo.Value);
        }

        return DecimalRound(fallbackAverage);
    }

    private static string ResolveKardexMovementType(tbl_movimiento_inventario movement, int signedQuantity)
    {
        if (!string.Equals(movement.tipo_movimiento, "Ajuste", StringComparison.OrdinalIgnoreCase))
        {
            return movement.tipo_movimiento ?? "Movimiento";
        }

        return signedQuantity >= 0 ? "Ajuste Entrada" : "Ajuste Salida";
    }

    private static string BuildDescription(tbl_movimiento_inventario movement, int signedQuantity)
    {
        var direction = signedQuantity >= 0 ? "incrementa" : "reduce";
        var reference = string.IsNullOrWhiteSpace(movement.tipo_documento_referencia)
            ? "manual"
            : movement.tipo_documento_referencia;

        return $"{reference}: {direction} stock en {Math.Abs(signedQuantity)} unidades";
    }

    private static decimal DecimalRound(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class KardexRebuildResult
{
    public int ProductsProcessed { get; init; }
    public int LinesGenerated { get; init; }
    public int ObservedLines { get; init; }

    public KardexRebuildResult Combine(KardexRebuildResult other)
    {
        return new KardexRebuildResult
        {
            ProductsProcessed = ProductsProcessed + other.ProductsProcessed,
            LinesGenerated = LinesGenerated + other.LinesGenerated,
            ObservedLines = ObservedLines + other.ObservedLines
        };
    }
}
