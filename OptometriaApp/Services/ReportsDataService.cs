using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class ReportsDataService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public ReportsDataService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    #region 1. Cuentas por Cobrar y Mora
    public async Task<AccountsReceivableReportResult> GetAccountsReceivableReportAsync(AccountsReceivableReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var query = db.tbl_cta_cobrar
            .AsNoTracking()
            .Include(c => c.id_comprobanteNavigation)
            .Include(c => c.id_ventaNavigation)
            .AsQueryable();

        if (filters.StartDate.HasValue)
        {
            var startDt = filters.StartDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(c => c.fecha_emision >= startDt);
        }
        if (filters.EndDate.HasValue)
        {
            var endDt = filters.EndDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(c => c.fecha_emision <= endDt);
        }

        var rawList = await query.ToListAsync(ct);
        var clientIds = rawList.Select(x => x.id_cliente).Distinct().ToList();
        var clientNames = await db.tbl_pacientes
            .AsNoTracking()
            .Where(c => clientIds.Contains(c.id_paciente))
            .ToDictionaryAsync(c => c.id_paciente, c => (c.nombres + " " + c.apellidos).Trim(), ct);

        var clientPhones = await db.tbl_pacientes
            .AsNoTracking()
            .Where(c => clientIds.Contains(c.id_paciente))
            .ToDictionaryAsync(c => c.id_paciente, c => c.telefono ?? "-", ct);

        var clientEmails = await db.tbl_pacientes
            .AsNoTracking()
            .Where(c => clientIds.Contains(c.id_paciente))
            .ToDictionaryAsync(c => c.id_paciente, c => c.email ?? "-", ct);

        var rows = new List<AccountsReceivableReportRow>();
        foreach (var item in rawList)
        {
            var clientName = clientNames.GetValueOrDefault(item.id_cliente, $"Cliente #{item.id_cliente}");
            var phone = clientPhones.GetValueOrDefault(item.id_cliente, "-");
            var email = clientEmails.GetValueOrDefault(item.id_cliente, "-");
            var dueDate = item.fecha_vencimiento ?? DateOnly.FromDateTime(item.fecha_emision.AddDays(30));
            var daysOverdue = today > dueDate ? today.DayNumber - dueDate.DayNumber : 0;
            var isOverdue = item.saldo > 0 && today > dueDate;
            var invoiceNum = item.id_comprobanteNavigation?.numero_comprobante 
                ?? $"VEN-{item.id_venta ?? 0:D6}";

            var row = new AccountsReceivableReportRow
            {
                IdCtaCobrar = item.id_cta_cobrar,
                ClientName = clientName,
                Phone = phone,
                Email = email,
                DocumentNumber = invoiceNum,
                IssueDate = DateOnly.FromDateTime(item.fecha_emision),
                DueDate = dueDate,
                TotalAmount = item.monto_total,
                Balance = item.saldo,
                PaidAmount = item.monto_total - item.saldo,
                DaysOverdue = daysOverdue,
                Status = item.saldo <= 0 ? "Pagada" : isOverdue ? "Vencida" : "Pendiente"
            };

            if (filters.MinOverdueDays.HasValue && filters.MinOverdueDays.Value > 0)
            {
                if (row.DaysOverdue < filters.MinOverdueDays.Value || row.Balance <= 0)
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(filters.Status) && filters.Status != "Todos")
            {
                if (!string.Equals(row.Status, filters.Status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(filters.SearchTerm))
            {
                var term = filters.SearchTerm.Trim().ToLowerInvariant();
                if (!row.ClientName.ToLowerInvariant().Contains(term) &&
                    !row.DocumentNumber.ToLowerInvariant().Contains(term) &&
                    !row.Phone.ToLowerInvariant().Contains(term))
                {
                    continue;
                }
            }

            rows.Add(row);
        }

        return new AccountsReceivableReportResult
        {
            Rows = rows.OrderByDescending(r => r.DaysOverdue).ThenByDescending(r => r.Balance).ToList(),
            TotalPortfolio = rows.Sum(r => r.TotalAmount),
            TotalBalance = rows.Sum(r => r.Balance),
            TotalCollected = rows.Sum(r => r.PaidAmount),
            TotalOverdueBalance = rows.Where(r => r.DaysOverdue > 0).Sum(r => r.Balance),
            CriticalOverdueCount = rows.Count(r => r.DaysOverdue >= (filters.MinOverdueDays ?? 30) && r.Balance > 0)
        };
    }
    #endregion

    public async Task<List<tbl_usuario>> GetCashierUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var validRoles = new[] { "administrador", "admin", "recepcionista", "cajero", "recepcion" };

        var rawUsers = await db.tbl_usuarios
            .AsNoTracking()
            .Include(u => u.id_rolNavigation)
            .Where(u => (u.activo ?? true) && u.id_rolNavigation != null && validRoles.Contains(u.id_rolNavigation.nombre.ToLower()))
            .OrderBy(u => u.nombres)
            .ThenBy(u => u.apellidos)
            .ToListAsync(ct);

        return rawUsers
            .GroupBy(u => (u.nombres + " " + u.apellidos).Trim().ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
    }

    #region 2. Ingresos, Egresos y Flujo de Caja
    public async Task<CashFlowReportResult> GetCashFlowReportAsync(CashFlowReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var salesQuery = db.tbl_venta
            .AsNoTracking()
            .Include(v => v.id_usuarioNavigation)
            .Where(v => v.fecha_venta >= startDt && v.fecha_venta <= endDt && v.estado != "Anulada");

        if (filters.UserId > 0)
        {
            salesQuery = salesQuery.Where(v => v.id_usuario == filters.UserId);
        }
        else if (filters.UserId == -1)
        {
            salesQuery = salesQuery.Where(v => v.id_usuario == 0 || v.id_usuarioNavigation == null);
        }

        var sales = await salesQuery.ToListAsync(ct);

        var abonos = await db.tbl_abonos
            .AsNoTracking()
            .Include(a => a.metodo_pagoNavigation)
            .Where(a => a.fecha_abono >= startDt && a.fecha_abono <= endDt)
            .ToListAsync(ct);

        var startUtc = startDt.ToUniversalTime();
        var endUtc = endDt.ToUniversalTime();
        var purchasePayments = await db.PurchasePayments.AsNoTracking()
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt <= endUtc && p.Method != "Saldo inicial")
            .Where(p => filters.UserId <= 0 || p.UserId == filters.UserId)
            .ToListAsync(ct);
        var rows = new List<CashFlowMovementRow>();

        foreach (var s in sales)
        {
            rows.Add(new CashFlowMovementRow
            {
                Date = s.fecha_venta ?? startDt,
                Type = "Ingreso",
                Category = "Venta Directa",
                DocumentNumber = $"VEN-{s.id_venta:D6}",
                Description = $"Venta de productos/servicios - {s.forma_pago ?? "Efectivo"}",
                PaymentMethod = s.forma_pago ?? "Efectivo",
                Amount = s.total ?? 0m,
                ResponsibleUser = s.id_usuarioNavigation != null
                    ? $"{s.id_usuarioNavigation.nombres} {s.id_usuarioNavigation.apellidos}".Trim() + (!string.IsNullOrWhiteSpace(s.id_usuarioNavigation.usuario) ? $" ({s.id_usuarioNavigation.usuario})" : "")
                    : "Ventas Tienda Online / Sistema"
            });
        }

        foreach (var a in abonos)
        {
            rows.Add(new CashFlowMovementRow
            {
                Date = a.fecha_abono ?? startDt,
                Type = "Ingreso",
                Category = "Cobro Cartera",
                DocumentNumber = $"ABN-{a.id_abono}",
                Description = $"Abono a cuenta por cobrar #{a.id_cta_cobrar} ({a.metodo_pagoNavigation?.nombre ?? "Efectivo"})",
                PaymentMethod = a.metodo_pagoNavigation?.nombre ?? "Efectivo",
                Amount = a.monto_abono,
                ResponsibleUser = a.usuario_registro ?? "Cajero"
            });
        }

        // Receipts and liquidations are not cash payments. Count the ledger once.
        foreach (var payment in purchasePayments)
        {
            rows.Add(new CashFlowMovementRow
            {
                Date = DateTime.SpecifyKind(payment.CreatedAt, DateTimeKind.Utc).ToLocalTime(),
                Type = "Egreso",
                Category = payment.ReversesId.HasValue ? "Reverso de abono de compra" : "Abono de compra",
                DocumentNumber = $"ABCOMP-{payment.Id}",
                Description = $"Liquidacion #{payment.LiquidationId}: {payment.Reference}",
                PaymentMethod = payment.Method,
                Amount = payment.Amount,
                ResponsibleUser = $"Usuario #{payment.UserId}"
            });
        }
        if (!string.IsNullOrWhiteSpace(filters.TransactionType) && filters.TransactionType != "Todos")
        {
            rows = rows.Where(r => string.Equals(r.Type, filters.TransactionType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var orderedRows = rows.OrderByDescending(r => r.Date).ToList();
        var totalIncome = orderedRows.Where(r => r.Type == "Ingreso").Sum(r => r.Amount);
        var totalExpense = orderedRows.Where(r => r.Type == "Egreso").Sum(r => r.Amount);

        return new CashFlowReportResult
        {
            Rows = orderedRows,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetCashFlow = totalIncome - totalExpense,
            IncomeCash = orderedRows.Where(r => r.Type == "Ingreso" && (r.PaymentMethod ?? "").Contains("Efectivo", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount),
            IncomeElectronic = orderedRows.Where(r => r.Type == "Ingreso" && !(r.PaymentMethod ?? "").Contains("Efectivo", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount)
        };
    }
    #endregion

    #region 3. Cierre de Caja Diario
    public async Task<DailyCashCloseResult> GetDailyCashCloseReportAsync(DateOnly cutoffDate, int userId = 0, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = cutoffDate.ToDateTime(TimeOnly.MinValue);
        var endDt = cutoffDate.ToDateTime(TimeOnly.MaxValue);

        var salesQuery = db.tbl_venta
            .AsNoTracking()
            .Include(v => v.id_usuarioNavigation)
            .Where(v => v.fecha_venta >= startDt && v.fecha_venta <= endDt);

        if (userId > 0)
        {
            salesQuery = salesQuery.Where(v => v.id_usuario == userId);
        }
        else if (userId == -1)
        {
            salesQuery = salesQuery.Where(v => v.id_usuario == 0 || v.id_usuarioNavigation == null);
        }

        var sales = await salesQuery.ToListAsync(ct);

        var abonosQuery = db.tbl_abonos
            .AsNoTracking()
            .Include(a => a.metodo_pagoNavigation)
            .Where(a => a.fecha_abono >= startDt && a.fecha_abono <= endDt);

        var abonos = await abonosQuery.ToListAsync(ct);

        var creditNotesQuery = db.tbl_nota_credito
            .AsNoTracking()
            .Where(nc => nc.fecha_emision >= startDt && nc.fecha_emision <= endDt && nc.estado != "Anulada");

        var creditNotes = await creditNotesQuery.ToListAsync(ct);

        var items = new List<DailyCashCloseItem>();

        foreach (var v in sales)
        {
            var isCancelled = (v.estado == "Anulada");
            var total = v.total ?? 0m;
            items.Add(new DailyCashCloseItem
            {
                Time = TimeOnly.FromDateTime(v.fecha_venta ?? startDt),
                Concept = isCancelled ? "Venta Anulada" : "Venta",
                DocumentNumber = $"VEN-{v.id_venta:D6}",
                PaymentMethod = v.forma_pago ?? "Efectivo",
                GrossAmount = total,
                NetAmount = isCancelled ? 0 : total,
                IsCancelled = isCancelled,
                Cashier = v.id_usuarioNavigation != null
                    ? $"{v.id_usuarioNavigation.nombres} {v.id_usuarioNavigation.apellidos}".Trim() + (!string.IsNullOrWhiteSpace(v.id_usuarioNavigation.usuario) ? $" ({v.id_usuarioNavigation.usuario})" : "")
                    : "Ventas Tienda Online / Sistema"
            });
        }

        foreach (var a in abonos)
        {
            items.Add(new DailyCashCloseItem
            {
                Time = TimeOnly.FromDateTime(a.fecha_abono ?? startDt),
                Concept = "Cobro de Cartera / Abono",
                DocumentNumber = $"ABN-{a.id_abono}",
                PaymentMethod = a.metodo_pagoNavigation?.nombre ?? "Efectivo",
                GrossAmount = a.monto_abono,
                NetAmount = a.monto_abono,
                IsCancelled = false,
                Cashier = a.usuario_registro ?? "Cajero"
            });
        }

        foreach (var nc in creditNotes)
        {
            items.Add(new DailyCashCloseItem
            {
                Time = TimeOnly.FromDateTime(nc.fecha_emision),
                Concept = "Nota de Crédito / Devolución",
                DocumentNumber = nc.numero_nota ?? $"NC-{nc.id_nota_credito}",
                PaymentMethod = "Ajuste",
                GrossAmount = nc.monto_total,
                NetAmount = -nc.monto_total,
                IsCancelled = false,
                Cashier = "Sistema"
            });
        }

        var orderedItems = items.OrderBy(x => x.Time).ToList();

        var validSales = sales.Where(v => v.estado != "Anulada").ToList();
        var totalCash = validSales.Where(v => (v.forma_pago ?? "Efectivo").Contains("Efectivo", StringComparison.OrdinalIgnoreCase)).Sum(v => v.total ?? 0m)
            + abonos.Where(a => (a.metodo_pagoNavigation?.nombre ?? "Efectivo").Contains("Efectivo", StringComparison.OrdinalIgnoreCase)).Sum(a => a.monto_abono);

        var totalTransfer = validSales.Where(v => (v.forma_pago ?? "").Contains("Transferencia", StringComparison.OrdinalIgnoreCase)).Sum(v => v.total ?? 0m)
            + abonos.Where(a => (a.metodo_pagoNavigation?.nombre ?? "").Contains("Transferencia", StringComparison.OrdinalIgnoreCase)).Sum(a => a.monto_abono);

        var totalCard = validSales.Where(v => (v.forma_pago ?? "").Contains("Tarjeta", StringComparison.OrdinalIgnoreCase)).Sum(v => v.total ?? 0m)
            + abonos.Where(a => (a.metodo_pagoNavigation?.nombre ?? "").Contains("Tarjeta", StringComparison.OrdinalIgnoreCase)).Sum(a => a.monto_abono);

        var totalCredit = validSales.Where(v => (v.forma_pago ?? "").Contains("Crédito", StringComparison.OrdinalIgnoreCase)).Sum(v => v.total ?? 0m);
        var totalCancelled = sales.Where(v => v.estado == "Anulada").Sum(v => v.total ?? 0m) + creditNotes.Sum(nc => nc.monto_total);
        var totalNetCollected = totalCash + totalTransfer + totalCard;

        return new DailyCashCloseResult
        {
            CutoffDate = cutoffDate,
            Items = orderedItems,
            TotalGrossSales = sales.Sum(v => v.total ?? 0m),
            TotalCash = totalCash,
            TotalTransfer = totalTransfer,
            TotalCard = totalCard,
            TotalCredit = totalCredit,
            TotalCancelled = totalCancelled,
            TotalNetCollected = totalNetCollected,
            TotalTransactionsCount = orderedItems.Count
        };
    }
    #endregion

    #region 4. Ventas y Facturación Electrónica
    public async Task<SalesBillingReportResult> GetSalesBillingReportAsync(SalesBillingReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.tbl_venta
            .AsNoTracking()
            .Include(v => v.id_usuarioNavigation)
            .Include(v => v.tbl_detalle_venta)
                .ThenInclude(d => d.id_productoNavigation)
                    .ThenInclude(p => p.id_categoriaNavigation)
            .Where(v => v.fecha_venta >= startDt && v.fecha_venta <= endDt && v.estado != "Anulada")
            .AsQueryable();

        if (filters.UserId > 0)
        {
            query = query.Where(v => v.id_usuario == filters.UserId);
        }
        else if (filters.UserId == -1)
        {
            query = query.Where(v => v.id_usuario == 0 || v.id_usuarioNavigation == null);
        }

        var sales = await query.ToListAsync(ct);

        var saleIds = sales.Select(s => s.id_venta).ToList();
        var invoices = await db.tbl_comprobantes
            .AsNoTracking()
            .Where(c => c.id_venta.HasValue && saleIds.Contains(c.id_venta.Value))
            .ToDictionaryAsync(c => c.id_venta!.Value, c => c, ct);

        var rows = new List<SalesBillingReportRow>();
        decimal totalLenses = 0, totalConsultations = 0, totalProducts = 0, totalServices = 0;

        foreach (var s in sales)
        {
            var inv = invoices.GetValueOrDefault(s.id_venta);
            var sriStatus = inv?.estado_sri ?? (s.estado == "Anulada" ? "Anulada" : "Emitida");

            if (!string.IsNullOrWhiteSpace(filters.SriStatus) && filters.SriStatus != "Todos")
            {
                if (!string.Equals(sriStatus, filters.SriStatus, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            decimal saleLenses = 0, saleConsultations = 0, saleProducts = 0, saleServices = 0;
            var detailsSummary = new StringBuilder();

            foreach (var d in s.tbl_detalle_venta)
            {
                var catName = d.id_productoNavigation?.id_categoriaNavigation?.nombre ?? "";
                var prodName = d.id_productoNavigation?.nombre_producto ?? "Producto";
                var lineTotal = d.total_item ?? (d.cantidad * (d.precio_unitario ?? 0m));

                if (catName.Contains("Lente", StringComparison.OrdinalIgnoreCase) || prodName.Contains("Lente", StringComparison.OrdinalIgnoreCase) || prodName.Contains("Armazón", StringComparison.OrdinalIgnoreCase))
                {
                    saleLenses += lineTotal;
                }
                else if (catName.Contains("Consulta", StringComparison.OrdinalIgnoreCase) || prodName.Contains("Consulta", StringComparison.OrdinalIgnoreCase))
                {
                    saleConsultations += lineTotal;
                }
                else if (catName.Contains("Servicio", StringComparison.OrdinalIgnoreCase) || prodName.Contains("Servicio", StringComparison.OrdinalIgnoreCase))
                {
                    saleServices += lineTotal;
                }
                else
                {
                    saleProducts += lineTotal;
                }

                if (detailsSummary.Length > 0) detailsSummary.Append(", ");
                detailsSummary.Append($"{d.cantidad}x {prodName}");
            }

            if (filters.CategoryId > 0)
            {
                var hasCat = s.tbl_detalle_venta.Any(d => d.id_productoNavigation?.id_categoria == filters.CategoryId);
                if (!hasCat) continue;
            }

            totalLenses += saleLenses;
            totalConsultations += saleConsultations;
            totalProducts += saleProducts;
            totalServices += saleServices;

            rows.Add(new SalesBillingReportRow
            {
                IdVenta = s.id_venta,
                SaleCode = $"VEN-{s.id_venta:D6}",
                InvoiceNumber = inv?.numero_comprobante ?? "Sin Factura",
                Date = s.fecha_venta ?? startDt,
                Subtotal = s.subtotal ?? 0m,
                Tax = s.impuesto_total ?? 0m,
                Total = s.total ?? 0m,
                PaymentMethod = s.forma_pago ?? "Efectivo",
                SriStatus = sriStatus,
                ItemsSummary = detailsSummary.ToString(),
                Seller = s.id_usuarioNavigation?.usuario ?? "Vendedor"
            });
        }

        return new SalesBillingReportResult
        {
            Rows = rows.OrderByDescending(r => r.Date).ToList(),
            GrandTotal = rows.Sum(r => r.Total),
            TotalTax = rows.Sum(r => r.Tax),
            TotalLenses = totalLenses,
            TotalConsultations = totalConsultations,
            TotalProducts = totalProducts,
            TotalServices = totalServices,
            AuthorizedCount = rows.Count(r => r.SriStatus.Contains("Autorizada", StringComparison.OrdinalIgnoreCase)),
            TotalInvoicesCount = rows.Count
        };
    }
    #endregion

    #region 5. Citas y Turnos
    public async Task<AppointmentsReportResult> GetAppointmentsReportAsync(AppointmentsReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var query = db.tbl_citas
            .AsNoTracking()
            .Include(c => c.id_pacienteNavigation)
            .Include(c => c.id_medicoNavigation).ThenInclude(m => m.id_usuarioNavigation)
            .Include(c => c.id_estadoNavigation)
            .Where(c => c.fecha_cita >= filters.StartDate && c.fecha_cita <= filters.EndDate)
            .AsQueryable();

        if (filters.DoctorId > 0)
        {
            query = query.Where(c => c.id_medico == filters.DoctorId);
        }

        if (!string.IsNullOrWhiteSpace(filters.State) && filters.State != "Todos")
        {
            query = query.Where(c => c.id_estadoNavigation != null && c.id_estadoNavigation.nombre_estado == filters.State);
        }

        if (!string.IsNullOrWhiteSpace(filters.AppointmentType) && filters.AppointmentType != "Todos")
        {
            query = query.Where(c => c.tipo_cita == filters.AppointmentType);
        }

        var list = await query.OrderByDescending(c => c.fecha_cita).ThenBy(c => c.hora_inicio).ToListAsync(ct);

        var rows = list.Select(c => new AppointmentsReportRow
        {
            IdCita = c.id_cita,
            Date = c.fecha_cita,
            TimeSlot = $"{c.hora_inicio:HH:mm} - {c.hora_fin:HH:mm}",
            PatientName = c.id_pacienteNavigation != null ? $"{c.id_pacienteNavigation.nombres} {c.id_pacienteNavigation.apellidos}".Trim() : "Paciente",
            PatientPhone = c.id_pacienteNavigation?.telefono ?? "-",
            DoctorName = c.id_medicoNavigation?.id_usuarioNavigation != null ? $"{c.id_medicoNavigation.id_usuarioNavigation.nombres} {c.id_medicoNavigation.id_usuarioNavigation.apellidos}".Trim() : "Doctor",
            Type = c.tipo_cita ?? "Presencial",
            State = c.id_estadoNavigation?.nombre_estado ?? "Pendiente",
            Reason = c.motivo_cita ?? "-"
        }).ToList();

        var total = rows.Count;
        var attended = rows.Count(r => r.State.Contains("Realizada", StringComparison.OrdinalIgnoreCase) || r.State.Contains("Atendida", StringComparison.OrdinalIgnoreCase));
        var cancelled = rows.Count(r => r.State.Contains("Cancelada", StringComparison.OrdinalIgnoreCase));
        var pending = rows.Count(r => r.State.Contains("Programada", StringComparison.OrdinalIgnoreCase) || r.State.Contains("Confirmada", StringComparison.OrdinalIgnoreCase) || r.State.Contains("Reprogramada", StringComparison.OrdinalIgnoreCase));
        var fulfillmentRate = total > 0 ? Math.Round((attended / (double)total) * 100, 1) : 0;

        return new AppointmentsReportResult
        {
            Rows = rows,
            TotalAppointments = total,
            AttendedCount = attended,
            CancelledCount = cancelled,
            PendingCount = pending,
            FulfillmentRate = fulfillmentRate
        };
    }
    #endregion

    #region 6. Consultas Optométricas y Diagnósticos
    public async Task<ClinicalConsultationsReportResult> GetClinicalConsultationsReportAsync(ClinicalConsultationsReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.tbl_historia_clinica_optometria_eventos
            .AsNoTracking()
            .Where(e => e.fecha_evento >= startDt && e.fecha_evento <= endDt && e.activo)
            .AsQueryable();

        if (filters.OptometristId > 0)
        {
            query = query.Where(e => e.id_optometra == filters.OptometristId);
        }

        var events = await query.OrderByDescending(e => e.fecha_evento).ToListAsync(ct);

        var patientIds = events.Select(e => e.id_paciente).Distinct().ToList();
        var doctorIds = events.Select(e => e.id_optometra).Distinct().ToList();

        var patients = await db.tbl_pacientes.AsNoTracking()
            .Where(p => patientIds.Contains(p.id_paciente))
            .ToDictionaryAsync(p => p.id_paciente, p => p, ct);

        var doctors = await db.tbl_usuarios.AsNoTracking()
            .Where(u => doctorIds.Contains(u.id_usuario))
            .ToDictionaryAsync(u => u.id_usuario, u => $"{u.nombres} {u.apellidos}".Trim(), ct);

        var rows = new List<ClinicalConsultationReportRow>();
        foreach (var e in events)
        {
            var p = patients.GetValueOrDefault(e.id_paciente);
            var doctorName = doctors.GetValueOrDefault(e.id_optometra, "Optometrista");

            var row = new ClinicalConsultationReportRow
            {
                IdEvento = e.id_historia_evento,
                Date = e.fecha_evento,
                PatientName = p != null ? $"{p.nombres} {p.apellidos}".Trim() : $"Paciente #{e.id_paciente}",
                PatientCedula = p?.cedula ?? "-",
                PatientAge = p?.edad?.ToString() ?? "-",
                OptometristName = doctorName,
                Diagnosis = string.IsNullOrWhiteSpace(e.diagnostico_resumen) ? (e.motivo_consulta ?? "Evaluación refractiva general") : e.diagnostico_resumen,
                Treatment = string.IsNullOrWhiteSpace(e.anamnesis) ? "Prescripción de lentes" : e.anamnesis,
                Observations = e.cie10 ?? "-"
            };

            if (!string.IsNullOrWhiteSpace(filters.SearchTerm))
            {
                var term = filters.SearchTerm.Trim().ToLowerInvariant();
                if (!row.PatientName.ToLowerInvariant().Contains(term) &&
                    !row.PatientCedula.ToLowerInvariant().Contains(term) &&
                    !row.Diagnosis.ToLowerInvariant().Contains(term) &&
                    !row.Treatment.ToLowerInvariant().Contains(term))
                {
                    continue;
                }
            }

            rows.Add(row);
        }

        return new ClinicalConsultationsReportResult
        {
            Rows = rows,
            TotalConsultations = rows.Count,
            DistinctPatientsCount = rows.Select(r => r.PatientName).Distinct().Count(),
            TreatmentsCount = rows.Count(r => !string.IsNullOrWhiteSpace(r.Treatment))
        };
    }
    #endregion

    #region 7. Inventario, Stock y Rotación
    public async Task<InventoryStockReportResult> GetInventoryStockReportAsync(InventoryStockReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var productsQuery = db.tbl_productos
            .AsNoTracking()
            .Include(p => p.id_categoriaNavigation)
            .Include(p => p.id_proveedorNavigation)
            .Where(p => p.activo ?? true)
            .AsQueryable();

        if (filters.CategoryId > 0)
        {
            productsQuery = productsQuery.Where(p => p.id_categoria == filters.CategoryId);
        }
        if (filters.SupplierId > 0)
        {
            productsQuery = productsQuery.Where(p => p.id_proveedor == filters.SupplierId);
        }

        var products = await productsQuery.ToListAsync(ct);

        var ninetyDaysAgo = DateTime.Now.AddDays(-90);
        var salesDetails = await db.tbl_detalle_venta
            .AsNoTracking()
            .Include(d => d.id_ventaNavigation)
            .Where(d => d.id_ventaNavigation != null && d.id_ventaNavigation.fecha_venta >= ninetyDaysAgo && d.id_ventaNavigation.estado != "Anulada")
            .GroupBy(d => d.id_producto)
            .Select(g => new { ProductId = g.Key, UnitsSold = g.Sum(x => x.cantidad) })
            .ToDictionaryAsync(x => x.ProductId, x => x.UnitsSold, ct);

        var rows = new List<InventoryStockReportRow>();
        foreach (var p in products)
        {
            var unitsSold = salesDetails.GetValueOrDefault(p.id_producto, 0);
            var stockMin = p.stock_minimo ?? 5;
            var currentStock = p.stock_actual ?? 0;
            var isDepleted = currentStock <= 0;
            var isLowStock = currentStock > 0 && currentStock <= stockMin;
            var stockStatus = isDepleted ? "Agotado" : isLowStock ? "Stock Crítico" : "Óptimo";
            var cost = p.precio_costo ?? 0m;

            var row = new InventoryStockReportRow
            {
                IdProducto = p.id_producto,
                Code = p.codigo_producto ?? $"PRD-{p.id_producto:D5}",
                Name = p.nombre_producto,
                Category = p.id_categoriaNavigation?.nombre ?? "General",
                Supplier = p.id_proveedorNavigation?.nombre ?? "General",
                CostPrice = cost,
                SalePrice = p.precio_venta,
                CurrentStock = currentStock,
                MinStock = stockMin,
                StockStatus = stockStatus,
                UnitsSold90Days = unitsSold,
                TotalValuation = currentStock * cost,
                RotationLevel = unitsSold >= 20 ? "Alta Rotación" : unitsSold >= 5 ? "Media Rotación" : "Baja / Nula Rotación"
            };

            if (!string.IsNullOrWhiteSpace(filters.StockStatus) && filters.StockStatus != "Todos")
            {
                if (!string.Equals(row.StockStatus, filters.StockStatus, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(filters.RotationFilter) && filters.RotationFilter != "Todos")
            {
                if (filters.RotationFilter == "Alta" && row.UnitsSold90Days < 20) continue;
                if (filters.RotationFilter == "Baja" && row.UnitsSold90Days >= 5) continue;
            }

            if (!string.IsNullOrWhiteSpace(filters.SearchTerm))
            {
                var term = filters.SearchTerm.Trim().ToLowerInvariant();
                if (!row.Name.ToLowerInvariant().Contains(term) && !row.Code.ToLowerInvariant().Contains(term))
                {
                    continue;
                }
            }

            rows.Add(row);
        }

        var orderedRows = filters.SortBy == "MenorRotacion"
            ? rows.OrderBy(r => r.UnitsSold90Days).ThenBy(r => r.CurrentStock).ToList()
            : rows.OrderByDescending(r => r.UnitsSold90Days).ThenBy(r => r.CurrentStock).ToList();

        return new InventoryStockReportResult
        {
            Rows = orderedRows,
            TotalProductsCount = rows.Count,
            TotalValuation = rows.Sum(r => r.TotalValuation),
            DepletedCount = rows.Count(r => r.CurrentStock <= 0),
            CriticalStockCount = rows.Count(r => r.CurrentStock > 0 && r.CurrentStock <= r.MinStock),
            TopSellingProductsCount = rows.Count(r => r.UnitsSold90Days >= 20),
            SlowMovingProductsCount = rows.Count(r => r.UnitsSold90Days <= 1 && r.CurrentStock > 0)
        };
    }
    #endregion

    #region 8. Movimientos de Inventario / Kardex
    public async Task<InventoryMovementsReportResult> GetInventoryMovementsReportAsync(InventoryMovementsReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.tbl_kardex
            .AsNoTracking()
            .Include(k => k.id_productoNavigation).ThenInclude(p => p.id_categoriaNavigation)
            .Include(k => k.id_usuario_movimientoNavigation)
            .Where(k => k.fecha_movimiento >= startDt && k.fecha_movimiento <= endDt)
            .AsQueryable();

        if (filters.ProductId > 0)
        {
            query = query.Where(k => k.id_producto == filters.ProductId);
        }

        if (filters.CategoryId > 0)
        {
            query = query.Where(k => k.id_productoNavigation.id_categoria == filters.CategoryId);
        }

        if (!string.IsNullOrWhiteSpace(filters.MovementType) && filters.MovementType != "Todos")
        {
            query = query.Where(k => k.tipo_movimiento == filters.MovementType);
        }

        var list = await query.OrderByDescending(k => k.fecha_movimiento).ToListAsync(ct);

        var rows = list.Select(k => new InventoryMovementReportRow
        {
            IdKardex = k.id_kardex,
            Date = k.fecha_movimiento ?? startDt,
            ProductName = k.id_productoNavigation?.nombre_producto ?? "Producto",
            Category = k.id_productoNavigation?.id_categoriaNavigation?.nombre ?? "General",
            MovementType = k.tipo_movimiento,
            DocumentReference = k.comprobante_numero ?? "-",
            Quantity = k.cantidad_movimiento,
            UnitCost = k.costo_unitario ?? 0m,
            TotalCost = k.costo_total ?? 0m,
            PreviousStock = k.stock_anterior ?? 0,
            NewStock = k.stock_nuevo ?? 0,
            User = k.id_usuario_movimientoNavigation?.usuario ?? "Sistema",
            Reason = k.descripcion_movimiento ?? "-"
        }).ToList();

        var totalEntries = rows.Where(r => r.MovementType.Contains("Entrada", StringComparison.OrdinalIgnoreCase) || r.MovementType.Contains("Ingreso", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);
        var totalExits = rows.Where(r => r.MovementType.Contains("Salida", StringComparison.OrdinalIgnoreCase) || r.MovementType.Contains("Venta", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);
        var totalAdjustments = rows.Where(r => r.MovementType.Contains("Ajuste", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);

        return new InventoryMovementsReportResult
        {
            Rows = rows,
            TotalMovementsCount = rows.Count,
            TotalEntriesQuantity = totalEntries,
            TotalExitsQuantity = totalExits,
            TotalAdjustmentsQuantity = totalAdjustments,
            TotalMovementsValue = rows.Sum(r => Math.Abs(r.TotalCost))
        };
    }
    #endregion

    #region 9. Compras a Proveedores
    public async Task<SupplierPurchasesReportResult> GetSupplierPurchasesReportAsync(SupplierPurchasesReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var ordersQuery = db.tbl_orden_compra
            .AsNoTracking()
            .Include(o => o.id_proveedorNavigation)
            .Include(o => o.id_usuario_solicitaNavigation)
            .Include(o => o.tbl_detalle_orden_compra)
                .ThenInclude(d => d.id_productoNavigation)
            .Where(o => o.fecha_orden >= startDt && o.fecha_orden <= endDt)
            .AsQueryable();

        if (filters.SupplierId > 0)
        {
            ordersQuery = ordersQuery.Where(o => o.id_proveedor == filters.SupplierId);
        }

        if (!string.IsNullOrWhiteSpace(filters.State) && filters.State != "Todos")
        {
            ordersQuery = ordersQuery.Where(o => o.estado_orden == filters.State);
        }

        var orders = await ordersQuery.OrderByDescending(o => o.fecha_orden).ToListAsync(ct);

        var rows = new List<SupplierPurchaseReportRow>();
        foreach (var o in orders)
        {
            var itemsBuilder = new StringBuilder();
            foreach (var d in o.tbl_detalle_orden_compra)
            {
                if (itemsBuilder.Length > 0) itemsBuilder.Append(", ");
                itemsBuilder.Append($"{d.cantidad_solicitada}x {d.id_productoNavigation?.nombre_producto ?? "Item"} (${d.precio_unitario:F2})");
            }

            rows.Add(new SupplierPurchaseReportRow
            {
                IdOrdenCompra = o.id_orden_compra,
                OrderNumber = o.numero_orden ?? $"OC-{o.id_orden_compra:D5}",
                Date = o.fecha_orden ?? startDt,
                SupplierName = o.id_proveedorNavigation?.nombre ?? "Proveedor General",
                SupplierRuc = o.id_proveedorNavigation?.ruc ?? "-",
                Subtotal = o.subtotal ?? 0m,
                Tax = o.impuesto_total ?? 0m,
                Total = o.total ?? 0m,
                State = o.estado_orden ?? "Emitida",
                ItemsDetails = itemsBuilder.ToString(),
                BuyerUser = o.id_usuario_solicitaNavigation?.usuario ?? "Compras"
            });
        }

        return new SupplierPurchasesReportResult
        {
            Rows = rows,
            TotalPurchasesAmount = rows.Sum(r => r.Total),
            TotalTaxAmount = rows.Sum(r => r.Tax),
            DistinctSuppliersCount = rows.Select(r => r.SupplierName).Distinct().Count(),
            TotalOrdersCount = rows.Count
        };
    }
    #endregion

    #region 10. Historial y Auditoría de Reportes
    public async Task<AuditHistoryReportResult> GetAuditHistoryReportAsync(AuditHistoryReportFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var startDt = filters.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDt = filters.EndDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.tbl_log_auditoria
            .AsNoTracking()
            .Include(a => a.id_usuarioNavigation)
            .Where(a => a.fecha >= startDt && a.fecha <= endDt && (a.modulo == "Reporteria" || (a.accion != null && a.accion.Contains("reporte"))))
            .AsQueryable();

        if (filters.UserId > 0)
        {
            query = query.Where(a => a.id_usuario == filters.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filters.ReportType) && filters.ReportType != "Todos")
        {
            query = query.Where(a => (a.accion != null && a.accion.Contains(filters.ReportType)) || (a.detalle != null && a.detalle.Contains(filters.ReportType)));
        }

        var list = await query.OrderByDescending(a => a.fecha).ToListAsync(ct);

        var rows = list.Select(a => new AuditHistoryReportRow
        {
            IdLog = a.id_log_auditoria,
            Date = a.fecha ?? DateTime.Now,
            User = a.id_usuarioNavigation?.usuario ?? "Sistema",
            Action = a.accion ?? "-",
            Module = a.modulo ?? "-",
            Details = a.detalle ?? "-"
        }).ToList();

        return new AuditHistoryReportResult
        {
            Rows = rows,
            TotalGeneratedCount = rows.Count,
            PdfExportsCount = rows.Count(r => r.Action.Contains("PDF", StringComparison.OrdinalIgnoreCase) || r.Details.Contains("PDF", StringComparison.OrdinalIgnoreCase)),
            ExcelExportsCount = rows.Count(r => r.Action.Contains("Excel", StringComparison.OrdinalIgnoreCase) || r.Details.Contains("CSV", StringComparison.OrdinalIgnoreCase) || r.Details.Contains("Excel", StringComparison.OrdinalIgnoreCase))
        };
    }

    public async Task LogReportActionAsync(int? userId, string reportName, string format, string details, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            int? validUserId = null;
            if (userId.HasValue && userId.Value > 0)
            {
                var userExists = await db.tbl_usuarios.AnyAsync(u => u.id_usuario == userId.Value, ct);
                if (userExists) validUserId = userId.Value;
            }

            db.tbl_log_auditoria.Add(new tbl_log_auditoria
            {
                id_usuario = validUserId,
                accion = $"Exportar {reportName} {format.ToUpperInvariant()}",
                modulo = "Reporteria",
                fecha = DateTime.Now,
                detalle = $"Reporte={reportName}; Formato={format}; {details}"
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Log failure should not break export
        }
    }
    #endregion

    #region Export Utilities (Excel & PDF)
    public byte[] GenerateCsvBytes(string reportTitle, string[] headers, List<string[]> rows)
    {
        return GenerateExcelBytes(reportTitle, headers, rows);
    }

    public byte[] GenerateExcelBytes(string reportTitle, string[] headers, List<string[]> rows)
    {
        var columnCount = Math.Max(1, headers.Length);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; color: #3c342e; background: #ffffff; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 5px; }");
        sb.AppendLine("th { background: #5DA181; color: #ffffff; font-weight: 700; border: 1px solid #4b8d70; padding: 10px; text-align: center; }");
        sb.AppendLine("td { border: 1px solid #d9cec0; padding: 8px; font-size: 13px; mso-number-format:'\\@'; text-align: left; }");
        sb.AppendLine("tr:nth-child(even) td { background: #F4F0E4; }");
        sb.AppendLine("tr:nth-child(odd) td { background: #ffffff; }");
        sb.AppendLine(".title { background: #7F6951; color: #ffffff; font-size: 18px; font-weight: 700; text-align: center; border: 1px solid #7F6951; padding: 12px; }");
        sb.AppendLine(".subtitle { background: #FEBC64; color: #3c342e; font-size: 13px; font-weight: 700; text-align: center; border: 1px solid #e3a84e; padding: 8px; }");
        sb.AppendLine(".numeric { text-align: right; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<table>");

        sb.Append("<tr><td class=\"title\" colspan=\"").Append(columnCount).Append("\">")
          .Append(WebUtility.HtmlEncode(reportTitle)).AppendLine("</td></tr>");
        sb.Append("<tr><td class=\"subtitle\" colspan=\"").Append(columnCount).Append("\">Generado el ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine("</td></tr>");

        if (headers.Length > 0)
        {
            sb.AppendLine("<tr>");
            foreach (var header in headers)
            {
                sb.Append("<th>").Append(WebUtility.HtmlEncode(header)).AppendLine("</th>");
            }
            sb.AppendLine("</tr>");
        }

        foreach (var row in rows)
        {
            sb.AppendLine("<tr>");
            for (int i = 0; i < headers.Length; i++)
            {
                var val = i < row.Length ? row[i] : "";
                var cleanVal = val.Replace("$", "").Replace("+", "").Trim();
                var isNumeric = decimal.TryParse(cleanVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _);
                var alignClass = (isNumeric && !val.StartsWith("0") && val.Length < 15) ? " class=\"numeric\"" : "";
                sb.Append("<td").Append(alignClass).Append(">").Append(WebUtility.HtmlEncode(val)).AppendLine("</td>");
            }
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public string GeneratePdfHtml(string reportTitle, Dictionary<string, string> summaryKpis, string[] headers, List<string[]> rows, bool isLandscape = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(reportTitle)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"@page {{ size: A4 {(isLandscape ? "landscape" : "portrait")}; margin: 10mm 10mm 10mm 10mm; }}");
        sb.AppendLine("@media print { body { -webkit-print-color-adjust: exact; print-color-adjust: exact; } .no-print { display: none !important; } }");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #3c342e; background: #ffffff; margin: 0; padding: 15px; }");
        sb.AppendLine(".print-btn { background: #5DA181; color: white; border: none; padding: 8px 16px; border-radius: 6px; font-weight: bold; cursor: pointer; margin-bottom: 15px; }");
        sb.AppendLine(".report-header { background: #7F6951; color: #ffffff; padding: 12px 18px; border-radius: 8px 8px 0 0; text-align: center; }");
        sb.AppendLine(".report-header h1 { margin: 0; font-size: 20px; font-weight: 700; }");
        sb.AppendLine(".report-subheader { background: #FEBC64; color: #3c342e; padding: 6px 15px; font-size: 12px; font-weight: 700; text-align: center; border-radius: 0 0 8px 8px; margin-bottom: 15px; }");
        sb.AppendLine(".kpi-container { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 15px; }");
        sb.AppendLine(".kpi-box { flex: 1; min-width: 120px; border: 1px solid rgba(127,105,81,0.25); background: #fdfbf7; border-radius: 6px; padding: 8px 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.04); }");
        sb.AppendLine(".kpi-title { font-size: 10px; font-weight: 700; text-transform: uppercase; color: #7F6951; letter-spacing: 0.5px; }");
        sb.AppendLine(".kpi-val { font-size: 14px; font-weight: 700; color: #1e293b; margin-top: 2px; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 5px; font-size: 11px; }");
        sb.AppendLine("th { background: #5DA181; color: #ffffff; font-weight: 700; border: 1px solid #4b8d70; padding: 8px 10px; text-align: left; }");
        sb.AppendLine("td { border: 1px solid #d9cec0; padding: 6px 10px; text-align: left; word-break: break-word; }");
        sb.AppendLine("tr:nth-child(even) td { background: #F4F0E4; }");
        sb.AppendLine("tr:nth-child(odd) td { background: #ffffff; }");
        sb.AppendLine(".numeric { text-align: right; }");
        sb.AppendLine(".footer { margin-top: 20px; font-size: 10px; color: #888888; text-align: right; border-top: 1px solid #eee; padding-top: 8px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("<div class=\"no-print\" style=\"text-align: right;\"><button class=\"print-btn\" onclick=\"window.print();\">🖨️ Imprimir / Guardar como PDF</button></div>");

        sb.AppendLine("<div class=\"report-header\">");
        sb.AppendLine($"<h1>{WebUtility.HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"report-subheader\">");
        sb.AppendLine($"ÓPTICA - SISTEMA DE GESTIÓN INTEGRAL &nbsp;|&nbsp; Emitido el {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("</div>");

        if (summaryKpis != null && summaryKpis.Count > 0)
        {
            sb.AppendLine("<div class=\"kpi-container\">");
            foreach (var kvp in summaryKpis)
            {
                sb.AppendLine("<div class=\"kpi-box\">");
                sb.AppendLine($"<div class=\"kpi-title\">{WebUtility.HtmlEncode(kvp.Key)}</div>");
                sb.AppendLine($"<div class=\"kpi-val\">{WebUtility.HtmlEncode(kvp.Value)}</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<table>");
        if (headers.Length > 0)
        {
            sb.AppendLine("<thead><tr>");
            foreach (var header in headers)
            {
                sb.AppendLine($"<th>{WebUtility.HtmlEncode(header)}</th>");
            }
            sb.AppendLine("</tr></thead>");
        }

        sb.AppendLine("<tbody>");
        foreach (var row in rows)
        {
            sb.AppendLine("<tr>");
            for (int i = 0; i < headers.Length; i++)
            {
                var val = i < row.Length ? row[i] : "";
                var cleanVal = val.Replace("$", "").Replace("+", "").Trim();
                var isNumeric = decimal.TryParse(cleanVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _);
                var alignClass = (isNumeric && !val.StartsWith("0") && val.Length < 15) ? " class=\"numeric\"" : "";
                sb.AppendLine($"<td{alignClass}>{WebUtility.HtmlEncode(val)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        sb.AppendLine($"<div class=\"footer\">Reporte generado automáticamente por Optometria App &nbsp;|&nbsp; Total registros: {rows.Count}</div>");

        sb.AppendLine("<script>");
        sb.AppendLine("function autoPrint() { setTimeout(function() { window.print(); }, 400); }");
        sb.AppendLine("if (document.readyState === 'complete') { autoPrint(); } else { window.addEventListener('load', autoPrint); }");
        sb.AppendLine("</script>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    public byte[] GeneratePdfBytes(string reportTitle, Dictionary<string, string> summaryKpis, string[] headers, List<string[]> rows)
    {
        return PdfReportBuilder.Build(reportTitle, summaryKpis, headers, rows, isLandscape: true);
    }
    #endregion
}

internal static class PdfReportBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Build(string reportTitle, Dictionary<string, string>? summaryKpis, string[] headers, List<string[]> rows, bool isLandscape = true)
    {
        var pageWidth = isLandscape ? 792f : 612f;
        var pageHeight = isLandscape ? 612f : 792f;
        var marginX = 36f;
        var marginTop = pageHeight - 36f;
        var marginBottom = 36f;
        var usableWidth = pageWidth - (marginX * 2f);

        int colCount = Math.Max(1, headers.Length);
        float[] colWidths = new float[colCount];
        float totalWeight = 0;
        for (int i = 0; i < colCount; i++)
        {
            var headerLen = i < headers.Length ? headers[i].Length : 5;
            float maxLen = headerLen;
            for (int r = 0; r < Math.Min(rows.Count, 60); r++)
            {
                if (i < rows[r].Length && rows[r][i] != null)
                {
                    maxLen = Math.Max(maxLen, rows[r][i].Length);
                }
            }
            float weight = Math.Clamp(maxLen, 8, 30);
            colWidths[i] = weight;
            totalWeight += weight;
        }
        for (int i = 0; i < colCount; i++)
        {
            colWidths[i] = (colWidths[i] / totalWeight) * usableWidth;
        }

        var pageStreamBuilders = new List<StringBuilder>();
        var currentPageSb = new StringBuilder();
        int currentPageNumber = 1;
        float currentY = marginTop;

        void StartNewPage(bool isContinuation = false)
        {
            if (currentPageSb.Length > 0)
            {
                pageStreamBuilders.Add(currentPageSb);
                currentPageSb = new StringBuilder();
                currentPageNumber++;
            }
            currentY = marginTop;

            // Draw Header Banner on top of page
            float headerHeight = isContinuation ? 20f : 24f;
            float headerY = currentY - headerHeight;
            // Banner background #7F6951
            currentPageSb.AppendLine(string.Format(Inv, "0.498 0.412 0.318 rg {0:0.##} {1:0.##} {2:0.##} {3:0.##} re f", marginX, headerY, usableWidth, headerHeight));
            // Title Text in white
            currentPageSb.AppendLine("BT\n1 1 1 rg");
            currentPageSb.AppendLine(string.Format(Inv, "/F2 {0} Tf 1 0 0 1 {1:0.##} {2:0.##} Tm ({3}) Tj\nET", isContinuation ? 10 : 12, marginX + 8f, headerY + (isContinuation ? 5.5f : 7f), EscapePdf(isContinuation ? $"{reportTitle} (Continuación)" : reportTitle)));
            currentY = headerY;

            // Subtitle Banner #FEBC64
            float subHeight = 15f;
            float subY = currentY - subHeight;
            currentPageSb.AppendLine(string.Format(Inv, "0.996 0.737 0.392 rg {0:0.##} {1:0.##} {2:0.##} {3:0.##} re f", marginX, subY, usableWidth, subHeight));
            currentPageSb.AppendLine("BT\n0.173 0.141 0.118 rg /F2 7.5 Tf");
            currentPageSb.AppendLine(string.Format(Inv, "1 0 0 1 {0:0.##} {1:0.##} Tm (OPTICA - SISTEMA DE GESTION INTEGRAL  |  Emitido el {2:yyyy-MM-dd HH:mm}) Tj\nET", marginX + 8f, subY + 4f, DateTime.Now));
            currentY = subY - 10f;

            if (!isContinuation && summaryKpis != null && summaryKpis.Count > 0)
            {
                var kpiList = summaryKpis.ToList();
                int cardsPerRow = isLandscape ? Math.Min(kpiList.Count, 5) : Math.Min(kpiList.Count, 4);
                cardsPerRow = Math.Max(1, cardsPerRow);
                float cardGap = 6f;
                float cardW = (usableWidth - (cardsPerRow - 1) * cardGap) / cardsPerRow;
                float cardH = 26f;

                int kpiRows = (int)Math.Ceiling((double)kpiList.Count / cardsPerRow);
                for (int r = 0; r < kpiRows; r++)
                {
                    float rowY = currentY - cardH;
                    for (int c = 0; c < cardsPerRow; c++)
                    {
                        int idx = r * cardsPerRow + c;
                        if (idx >= kpiList.Count) break;

                        float cardX = marginX + c * (cardW + cardGap);
                        // Card background #FDFBF7 and border #D9CEC0
                        currentPageSb.AppendLine(string.Format(Inv, "0.992 0.984 0.969 rg 0.851 0.808 0.753 RG 0.75 w {0:0.##} {1:0.##} {2:0.##} {3:0.##} re B", cardX, rowY, cardW, cardH));
                        // Accent left border #7F6951
                        currentPageSb.AppendLine(string.Format(Inv, "0.498 0.412 0.318 rg {0:0.##} {1:0.##} 3 {2:0.##} re f", cardX, rowY, cardH));

                        // KPI label
                        var kpiKey = kpiList[idx].Key.ToUpper();
                        var maxKeyChars = (int)(cardW / 4.5f);
                        if (kpiKey.Length > maxKeyChars && maxKeyChars > 3) kpiKey = kpiKey.Substring(0, maxKeyChars - 2) + "..";
                        currentPageSb.AppendLine(string.Format(Inv, "BT\n0.498 0.412 0.318 rg /F2 6.5 Tf 1 0 0 1 {0:0.##} {1:0.##} Tm ({2}) Tj\nET", cardX + 6f, rowY + 15f, EscapePdf(kpiKey)));

                        // KPI value
                        var kpiVal = kpiList[idx].Value;
                        var maxValChars = (int)(cardW / 5.2f);
                        if (kpiVal.Length > maxValChars && maxValChars > 3) kpiVal = kpiVal.Substring(0, maxValChars - 2) + "..";
                        currentPageSb.AppendLine(string.Format(Inv, "BT\n0.118 0.161 0.231 rg /F2 9 Tf 1 0 0 1 {0:0.##} {1:0.##} Tm ({2}) Tj\nET", cardX + 6f, rowY + 5f, EscapePdf(kpiVal)));
                    }
                    currentY = rowY - cardGap;
                }
                currentY -= 4f;
            }

            DrawTableHeader();
        }

        void DrawTableHeader()
        {
            float headerH = 18f;
            float rowY = currentY - headerH;

            // Table Header Background #5DA181 and border #4B8D70
            currentPageSb.AppendLine(string.Format(Inv, "0.365 0.631 0.506 rg 0.294 0.553 0.439 RG 0.75 w {0:0.##} {1:0.##} {2:0.##} {3:0.##} re B", marginX, rowY, usableWidth, headerH));

            // Vertical column dividers
            currentPageSb.AppendLine("0.294 0.553 0.439 RG 0.5 w");
            float curDividerX = marginX;
            for (int i = 0; i < colCount - 1; i++)
            {
                curDividerX += colWidths[i];
                currentPageSb.AppendLine(string.Format(Inv, "{0:0.##} {1:0.##} m {0:0.##} {2:0.##} l S", curDividerX, rowY, currentY));
            }

            // Header Texts
            currentPageSb.AppendLine("BT\n1 1 1 rg /F2 7.5 Tf");
            float curTextX = marginX;
            for (int i = 0; i < colCount; i++)
            {
                var hText = i < headers.Length ? headers[i] : "";
                var maxChars = (int)(colWidths[i] / 4.6f);
                if (hText.Length > maxChars && maxChars > 3) hText = hText.Substring(0, maxChars - 2) + "..";
                currentPageSb.AppendLine(string.Format(Inv, "1 0 0 1 {0:0.##} {1:0.##} Tm ({2}) Tj", curTextX + 4f, rowY + 5.5f, EscapePdf(hText)));
                curTextX += colWidths[i];
            }
            currentPageSb.AppendLine("ET");

            currentY = rowY;
        }

        StartNewPage(isContinuation: false);

        float rowH = 15f;
        if (rows.Count == 0)
        {
            float emptyY = currentY - 22f;
            currentPageSb.AppendLine(string.Format(Inv, "1 1 1 rg 0.851 0.808 0.753 RG 0.5 w {0:0.##} {1:0.##} {2:0.##} 22 re B", marginX, emptyY, usableWidth));
            currentPageSb.AppendLine(string.Format(Inv, "BT\n0.498 0.412 0.318 rg /F1 8 Tf 1 0 0 1 {0:0.##} {1:0.##} Tm (No se encontraron registros para los filtros seleccionados.) Tj\nET", marginX + 12f, emptyY + 7.5f));
            currentY = emptyY;
        }
        else
        {
            for (int r = 0; r < rows.Count; r++)
            {
                if (currentY - rowH < marginBottom + 24f)
                {
                    StartNewPage(isContinuation: true);
                }

                float rowY = currentY - rowH;
                var row = rows[r];

                // Zebra row background #F4F0E4 (even) / #FFFFFF (odd) + border #D9CEC0
                string fillRgb = (r % 2 == 0) ? "0.957 0.941 0.894" : "1 1 1";
                currentPageSb.AppendLine(string.Format(Inv, "{0} rg 0.851 0.808 0.753 RG 0.5 w {1:0.##} {2:0.##} {3:0.##} {4:0.##} re B", fillRgb, marginX, rowY, usableWidth, rowH));

                // Vertical column dividers
                currentPageSb.AppendLine("0.851 0.808 0.753 RG 0.5 w");
                float curDividerX = marginX;
                for (int i = 0; i < colCount - 1; i++)
                {
                    curDividerX += colWidths[i];
                    currentPageSb.AppendLine(string.Format(Inv, "{0:0.##} {1:0.##} m {0:0.##} {2:0.##} l S", curDividerX, rowY, currentY));
                }

                // Row Cell Texts
                currentPageSb.AppendLine("BT\n0.173 0.141 0.118 rg /F1 7.5 Tf");
                float curCellX = marginX;
                for (int i = 0; i < colCount; i++)
                {
                    var cellVal = i < row.Length ? (row[i] ?? "") : "";
                    var maxChars = (int)(colWidths[i] / 4.4f);
                    if (cellVal.Length > maxChars && maxChars > 3) cellVal = cellVal.Substring(0, maxChars - 2) + "..";

                    var cleanVal = cellVal.Replace("$", "").Replace("+", "").Trim();
                    var isNumeric = decimal.TryParse(cleanVal, NumberStyles.Any, Inv, out _);

                    float textX = curCellX + 4f;
                    if (isNumeric && !cellVal.StartsWith("0") && cellVal.Length < 15)
                    {
                        float approxTextWidth = cellVal.Length * 4.2f;
                        textX = Math.Max(curCellX + 4f, curCellX + colWidths[i] - approxTextWidth - 4f);
                    }

                    currentPageSb.AppendLine(string.Format(Inv, "1 0 0 1 {0:0.##} {1:0.##} Tm ({2}) Tj", textX, rowY + 4.5f, EscapePdf(cellVal)));
                    curCellX += colWidths[i];
                }
                currentPageSb.AppendLine("ET");

                currentY = rowY;
            }
        }

        pageStreamBuilders.Add(currentPageSb);

        int totalPages = pageStreamBuilders.Count;
        for (int p = 0; p < totalPages; p++)
        {
            var sb = pageStreamBuilders[p];
            // Footer bottom line
            sb.AppendLine(string.Format(Inv, "0.851 0.808 0.753 RG 0.5 w {0:0.##} 24 m {1:0.##} 24 l S", marginX, marginX + usableWidth));
            sb.AppendLine(string.Format(Inv, "BT\n0.498 0.412 0.318 rg /F1 7 Tf 1 0 0 1 {0:0.##} 14 Tm (Pagina {1} de {2}  |  Total registros: {3}  |  Optica - Gestion Integral) Tj\nET", marginX, p + 1, totalPages, rows.Count));
        }

        var encoding = Encoding.Latin1;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"
        };

        var pageObjectIds = new List<int>();
        for (int p = 0; p < totalPages; p++)
        {
            var pageId = objects.Count + 1;
            var contentId = pageId + 1;
            pageObjectIds.Add(pageId);

            objects.Add(string.Format(Inv, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.##} {1:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {2} 0 R >>", pageWidth, pageHeight, contentId));
            var streamContent = pageStreamBuilders[p].ToString();
            var streamBytes = encoding.GetBytes(streamContent);
            objects.Add(string.Format(Inv, "<< /Length {0} >>\nstream\n{1}\nendstream", streamBytes.Length, streamContent));
        }

        objects[1] = string.Format(Inv, "<< /Type /Pages /Count {0} /Kids [{1}] >>", totalPages, string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R")));

        var pdfBuilder = new StringBuilder();
        pdfBuilder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };

        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(encoding.GetByteCount(pdfBuilder.ToString()));
            pdfBuilder.Append(string.Format(Inv, "{0} 0 obj\n{1}\nendobj\n", i + 1, objects[i]));
        }

        var xrefPos = encoding.GetByteCount(pdfBuilder.ToString());
        pdfBuilder.Append(string.Format(Inv, "xref\n0 {0}\n", objects.Count + 1));
        pdfBuilder.Append("0000000000 65535 f \n");
        foreach (var off in offsets.Skip(1))
        {
            pdfBuilder.Append(string.Format(Inv, "{0:D10} 00000 n \n", off));
        }
        pdfBuilder.Append(string.Format(Inv, "trailer << /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", objects.Count + 1, xrefPos));

        return encoding.GetBytes(pdfBuilder.ToString());
    }

    private static string EscapePdf(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }
}

#region Models & DTOs
public sealed class AccountsReceivableReportFilters
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? MinOverdueDays { get; set; } = 30;
    public string Status { get; set; } = "Todos";
    public string SearchTerm { get; set; } = string.Empty;
}

public sealed class AccountsReceivableReportRow
{
    public int IdCtaCobrar { get; set; }
    public string ClientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Balance { get; set; }
    public decimal PaidAmount { get; set; }
    public int DaysOverdue { get; set; }
    public string Status { get; set; } = "";
}

public sealed class AccountsReceivableReportResult
{
    public List<AccountsReceivableReportRow> Rows { get; set; } = [];
    public decimal TotalPortfolio { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalOverdueBalance { get; set; }
    public int CriticalOverdueCount { get; set; }
}

public sealed class CashFlowReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int UserId { get; set; }
    public string TransactionType { get; set; } = "Todos";
}

public sealed class CashFlowMovementRow
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
    public string ResponsibleUser { get; set; } = "";
}

public sealed class CashFlowReportResult
{
    public List<CashFlowMovementRow> Rows { get; set; } = [];
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetCashFlow { get; set; }
    public decimal IncomeCash { get; set; }
    public decimal IncomeElectronic { get; set; }
}

public sealed class DailyCashCloseItem
{
    public TimeOnly Time { get; set; }
    public string Concept { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public bool IsCancelled { get; set; }
    public string Cashier { get; set; } = "";
}

public sealed class DailyCashCloseResult
{
    public DateOnly CutoffDate { get; set; }
    public List<DailyCashCloseItem> Items { get; set; } = [];
    public decimal TotalGrossSales { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalTransfer { get; set; }
    public decimal TotalCard { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal TotalCancelled { get; set; }
    public decimal TotalNetCollected { get; set; }
    public int TotalTransactionsCount { get; set; }
}

public sealed class SalesBillingReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int UserId { get; set; }
    public int CategoryId { get; set; }
    public string SriStatus { get; set; } = "Todos";
}

public sealed class SalesBillingReportRow
{
    public int IdVenta { get; set; }
    public string SaleCode { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string SriStatus { get; set; } = "";
    public string ItemsSummary { get; set; } = "";
    public string Seller { get; set; } = "";
}

public sealed class SalesBillingReportResult
{
    public List<SalesBillingReportRow> Rows { get; set; } = [];
    public decimal GrandTotal { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalLenses { get; set; }
    public decimal TotalConsultations { get; set; }
    public decimal TotalProducts { get; set; }
    public decimal TotalServices { get; set; }
    public int AuthorizedCount { get; set; }
    public int TotalInvoicesCount { get; set; }
}

public sealed class AppointmentsReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public int DoctorId { get; set; }
    public string State { get; set; } = "Todos";
    public string AppointmentType { get; set; } = "Todos";
}

public sealed class AppointmentsReportRow
{
    public int IdCita { get; set; }
    public DateOnly Date { get; set; }
    public string TimeSlot { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientPhone { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class AppointmentsReportResult
{
    public List<AppointmentsReportRow> Rows { get; set; } = [];
    public int TotalAppointments { get; set; }
    public int AttendedCount { get; set; }
    public int CancelledCount { get; set; }
    public int PendingCount { get; set; }
    public double FulfillmentRate { get; set; }
}

public sealed class ClinicalConsultationsReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-60));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int OptometristId { get; set; }
    public string SearchTerm { get; set; } = string.Empty;
}

public sealed class ClinicalConsultationReportRow
{
    public int IdEvento { get; set; }
    public DateTime Date { get; set; }
    public string PatientName { get; set; } = "";
    public string PatientCedula { get; set; } = "";
    public string PatientAge { get; set; } = "";
    public string OptometristName { get; set; } = "";
    public string Diagnosis { get; set; } = "";
    public string Treatment { get; set; } = "";
    public string Observations { get; set; } = "";
}

public sealed class ClinicalConsultationsReportResult
{
    public List<ClinicalConsultationReportRow> Rows { get; set; } = [];
    public int TotalConsultations { get; set; }
    public int DistinctPatientsCount { get; set; }
    public int TreatmentsCount { get; set; }
}

public sealed class InventoryStockReportFilters
{
    public int CategoryId { get; set; }
    public int SupplierId { get; set; }
    public string StockStatus { get; set; } = "Todos";
    public string RotationFilter { get; set; } = "Todos";
    public string SortBy { get; set; } = "MayorRotacion";
    public string SearchTerm { get; set; } = string.Empty;
}

public sealed class InventoryStockReportRow
{
    public int IdProducto { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Supplier { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public int CurrentStock { get; set; }
    public int MinStock { get; set; }
    public string StockStatus { get; set; } = "";
    public int UnitsSold90Days { get; set; }
    public decimal TotalValuation { get; set; }
    public string RotationLevel { get; set; } = "";
}

public sealed class InventoryStockReportResult
{
    public List<InventoryStockReportRow> Rows { get; set; } = [];
    public int TotalProductsCount { get; set; }
    public decimal TotalValuation { get; set; }
    public int DepletedCount { get; set; }
    public int CriticalStockCount { get; set; }
    public int TopSellingProductsCount { get; set; }
    public int SlowMovingProductsCount { get; set; }
}

public sealed class InventoryMovementsReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int ProductId { get; set; }
    public int CategoryId { get; set; }
    public string MovementType { get; set; } = "Todos";
}

public sealed class InventoryMovementReportRow
{
    public int IdKardex { get; set; }
    public DateTime Date { get; set; }
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public string MovementType { get; set; } = "";
    public string DocumentReference { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public int PreviousStock { get; set; }
    public int NewStock { get; set; }
    public string User { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class InventoryMovementsReportResult
{
    public List<InventoryMovementReportRow> Rows { get; set; } = [];
    public int TotalMovementsCount { get; set; }
    public int TotalEntriesQuantity { get; set; }
    public int TotalExitsQuantity { get; set; }
    public int TotalAdjustmentsQuantity { get; set; }
    public decimal TotalMovementsValue { get; set; }
}

public sealed class SupplierPurchasesReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-60));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int SupplierId { get; set; }
    public string State { get; set; } = "Todos";
}

public sealed class SupplierPurchaseReportRow
{
    public int IdOrdenCompra { get; set; }
    public string OrderNumber { get; set; } = "";
    public DateTime Date { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierRuc { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string State { get; set; } = "";
    public string ItemsDetails { get; set; } = "";
    public string BuyerUser { get; set; } = "";
}

public sealed class SupplierPurchasesReportResult
{
    public List<SupplierPurchaseReportRow> Rows { get; set; } = [];
    public decimal TotalPurchasesAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public int DistinctSuppliersCount { get; set; }
    public int TotalOrdersCount { get; set; }
}

public sealed class AuditHistoryReportFilters
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int UserId { get; set; }
    public string ReportType { get; set; } = "Todos";
}

public sealed class AuditHistoryReportRow
{
    public int IdLog { get; set; }
    public DateTime Date { get; set; }
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string Module { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class AuditHistoryReportResult
{
    public List<AuditHistoryReportRow> Rows { get; set; } = [];
    public int TotalGeneratedCount { get; set; }
    public int PdfExportsCount { get; set; }
    public int ExcelExportsCount { get; set; }
}

internal static class SimplePdfGenerator
{
    public static byte[] Build(string title, List<string> lines)
    {
        var builder = new StringBuilder();
        var objects = new List<string>();

        builder.Append("%PDF-1.4\n");
        builder.Append("%\u00e2\u00e3\u00cf\u00d3\n");

        objects.Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        objects.Add("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        objects.Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
        objects.Add("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var sb = new StringBuilder();
        sb.Append("BT\n");
        sb.Append("/F1 9 Tf\n");

        var currentY = 745;
        foreach (var line in lines)
        {
            var sanitized = EscapePdf(line);
            if (sanitized.Length > 110)
            {
                sanitized = sanitized.Substring(0, 107) + "...";
            }

            sb.Append($"1 0 0 1 40 {currentY} Tm ({sanitized}) Tj\n");
            currentY -= 13;
            if (currentY < 40)
            {
                break;
            }
        }

        sb.Append("ET\n");
        var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());

        objects.Add($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{sb}endstream\nendobj\n");

        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(builder.Length);
            builder.Append(obj);
        }

        var xrefOffset = builder.Length;
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i <= objects.Count; i++)
        {
            builder.Append($"{offsets[i]:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string EscapePdf(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var unaccented = value
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
            .Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U")
            .Replace("ñ", "n").Replace("Ñ", "N");

        return unaccented
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }
}
#endregion
