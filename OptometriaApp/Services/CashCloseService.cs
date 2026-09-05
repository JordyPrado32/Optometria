using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class CashCloseService(IDbContextFactory<OpticaDbContext> factory)
{
    public static decimal InitialCollection(decimal collected, decimal subsequentPayments, bool cancelled)
        => cancelled ? 0m : Math.Max(0m, collected - subsequentPayments);

    public static async Task<bool> CanAccessAsync(OpticaDbContext db, int userId)
    {
        var user = await db.tbl_usuarios.SingleOrDefaultAsync(x => x.id_usuario == userId && x.activo == true && x.bloqueado != true);
        return user != null && (user.id_rol == 1 || await db.tbl_rol_menu_permisos.AnyAsync(p => p.id_rol == user.id_rol && p.puede_ver && p.puede_editar
            && db.tbl_menu_apps.Any(m => m.id_menu == p.id_menu && m.activo && m.ruta == "/reportes/cierre-caja")));
    }

    public static async Task<CashClose> CalculateAsync(OpticaDbContext db)
    {
        var previous = await db.CashCloses.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var saleId = previous?.LastSaleId ?? 0;
        var paymentId = previous?.LastPaymentId ?? 0;
        var sales = await db.tbl_venta.ToListAsync();
        var payments = await db.tbl_abonos.Include(x => x.metodo_pagoNavigation).Where(x => x.id_abono > paymentId).ToListAsync();
        var saleIds = sales.Select(x => x.id_venta).ToList();
        var applied = await db.tbl_abonos.Where(x => saleIds.Contains(x.id_venta)).GroupBy(x => x.id_venta)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.monto_abono) }).ToDictionaryAsync(x => x.Id, x => x.Total);
        var nonCashIds = await db.tbl_abonos.Where(x => x.tipo_movimiento == "AbonoNotaCredito").Select(x => x.id_abono).ToListAsync();
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        void Add(string? method, decimal amount)
        {
            var key = string.IsNullOrWhiteSpace(method) ? "Sin identificar" : method.Trim();
            totals[key] = totals.GetValueOrDefault(key) + amount;
        }
        var priorSales = JsonSerializer.Deserialize<Dictionary<int, SaleCashSnapshot>>(previous?.SalePaymentsJson ?? "{}")!;
        var currentSales = new Dictionary<int, SaleCashSnapshot>();
        foreach (var sale in sales)
        {
            // valor_cobrado includes subsequent receivable payments; remove them to avoid counting twice.
            var initial = InitialCollection(sale.valor_cobrado ?? 0m, applied.GetValueOrDefault(sale.id_venta), sale.estado == "Anulada");
            var snapshot = new SaleCashSnapshot(sale.forma_pago ?? "Sin identificar", initial);
            currentSales[sale.id_venta] = snapshot;
            var prior = priorSales.GetValueOrDefault(sale.id_venta);
            if (prior != snapshot)
            {
                if (prior is not null && prior.Amount != 0) Add(prior.Method, -prior.Amount);
                if (initial != 0) Add(snapshot.Method, initial);
            }
        }
        foreach (var payment in payments.Where(x => x.tipo_movimiento != "AbonoNotaCredito" &&
                     (!x.id_abono_referencia.HasValue || !nonCashIds.Contains(x.id_abono_referencia.Value))))
            Add(payment.metodo_pagoNavigation?.nombre, payment.monto_abono);
        var opening = previous?.RetainedCash ?? 0m;
        return new CashClose
        {
            OpeningCash = opening, Collected = totals.Values.Sum(),
            ExpectedCash = opening + totals.Where(x => x.Key.Contains("Efectivo", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Value),
            LastSaleId = sales.Count == 0 ? saleId : sales.Max(x => x.id_venta),
            LastPaymentId = payments.Count == 0 ? paymentId : payments.Max(x => x.id_abono),
            PaymentsJson = JsonSerializer.Serialize(totals), SalePaymentsJson = JsonSerializer.Serialize(currentSales)
        };
    }

    public async Task CloseAsync(int userId, CashClose input, int expectedPreviousId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await CanAccessAsync(db, userId)) throw new InvalidOperationException("No tienes permiso para cerrar caja.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (await db.CashCloses.AnyAsync(x => x.OperationId == input.OperationId)) return;
        if ((await db.CashCloses.MaxAsync(x => (int?)x.Id) ?? 0) != expectedPreviousId)
            throw new InvalidOperationException("Otro usuario cerró la caja. Actualiza antes de continuar.");
        Validate(input);
        var record = await CalculateAsync(db);
        if (record.PaymentsJson != input.PaymentsJson || record.ExpectedCash != input.ExpectedCash)
            throw new InvalidOperationException("Los cobros cambiaron. Actualiza los importes y verifica el efectivo antes de cerrar.");
        record.OperationId = input.OperationId;
        record.ClosedAt = DateTime.UtcNow;
        record.UserId = userId;
        record.CountedCash = input.CountedCash;
        record.BankWithdrawal = input.BankWithdrawal;
        record.OtherWithdrawal = input.OtherWithdrawal;
        record.RetainedCash = input.CountedCash - input.BankWithdrawal - input.OtherWithdrawal;
        record.BankReference = input.BankReference.Trim();
        record.Observation = input.Observation.Trim();
        db.CashCloses.Add(record);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public static void Validate(CashClose input)
    {
        if (input.OperationId == Guid.Empty) throw new InvalidOperationException("Operación inválida.");
        if (new[] { input.CountedCash, input.BankWithdrawal, input.OtherWithdrawal }.Any(x => x < 0 || x > 9999999999999999.99m || decimal.Round(x, 2) != x))
            throw new InvalidOperationException("Ingresa importes positivos con máximo dos decimales.");
        if (input.BankWithdrawal + input.OtherWithdrawal > input.CountedCash)
            throw new InvalidOperationException("Los retiros no pueden superar el efectivo contado.");
        if (string.IsNullOrWhiteSpace(input.Observation) || input.Observation.Length > 1000)
            throw new InvalidOperationException("Ingresa una observación de hasta 1000 caracteres, explicando retiros y diferencias.");
        if (input.BankReference.Length > 200 || (input.BankWithdrawal > 0 && string.IsNullOrWhiteSpace(input.BankReference)))
            throw new InvalidOperationException("Indica el banco destino y la referencia del retiro (máximo 200 caracteres).");
    }
}

public sealed record SaleCashSnapshot(string Method, decimal Amount);
