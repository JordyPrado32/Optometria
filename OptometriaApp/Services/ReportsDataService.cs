using System.Globalization;
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

        var sales = await salesQuery.ToListAsync(ct);

        var abonos = await db.tbl_abonos
            .AsNoTracking()
            .Include(a => a.metodo_pagoNavigation)
            .Where(a => a.fecha_abono >= startDt && a.fecha_abono <= endDt)
            .ToListAsync(ct);

        var receptions = await db.tbl_recepcion_compra
            .AsNoTracking()
            .Include(r => r.id_orden_compraNavigation).ThenInclude(o => o.id_proveedorNavigation)
            .Include(r => r.id_usuario_recibeNavigation)
            .Where(r => r.fecha_recepcion >= startDt && r.fecha_recepcion <= endDt && (r.activo ?? true))
            .ToListAsync(ct);

        var liquidations = await db.tbl_liquidacion_compra
            .AsNoTracking()
            .Include(l => l.id_orden_compraNavigation).ThenInclude(o => o.id_proveedorNavigation)
            .Include(l => l.id_usuario_registroNavigation)
            .Where(l => l.fecha_liquidacion >= startDt && l.fecha_liquidacion <= endDt && (l.activo ?? true))
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
                ResponsibleUser = s.id_usuarioNavigation?.usuario ?? "Sistema"
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

        foreach (var r in receptions)
        {
            var amount = r.id_orden_compraNavigation?.total ?? 0m;
            rows.Add(new CashFlowMovementRow
            {
                Date = r.fecha_recepcion ?? startDt,
                Type = "Egreso",
                Category = "Compra Proveedor",
                DocumentNumber = r.numero_recepcion ?? $"REC-{r.id_recepcion}",
                Description = $"Recepción de compra - Proveedor: {r.id_orden_compraNavigation?.id_proveedorNavigation?.nombre ?? "General"}",
                PaymentMethod = "Transferencia/Crédito",
                Amount = amount,
                ResponsibleUser = r.id_usuario_recibeNavigation?.usuario ?? "Bodega"
            });
        }

        foreach (var l in liquidations)
        {
            var amount = l.total ?? 0m;
            rows.Add(new CashFlowMovementRow
            {
                Date = l.fecha_liquidacion ?? startDt,
                Type = "Egreso",
                Category = "Liquidación de Compra",
                DocumentNumber = l.numero_liquidacion ?? $"LIQ-{l.id_liquidacion_compra}",
                Description = $"Liquidación de bienes/servicios - Proveedor: {l.id_orden_compraNavigation?.id_proveedorNavigation?.nombre ?? "General"}",
                PaymentMethod = "Liquidación",
                Amount = amount,
                ResponsibleUser = l.id_usuario_registroNavigation?.usuario ?? "Administración"
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
                Cashier = v.id_usuarioNavigation?.usuario ?? "Cajero"
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

    #region Export Utilities (CSV & PDF)
    public byte[] GenerateCsvBytes(string reportTitle, string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine($"\"{reportTitle}\"");
        sb.AppendLine($"\"Fecha de Generación: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine();

        sb.AppendLine(string.Join(";", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(";", row.Select(EscapeCsv)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public byte[] GeneratePdfBytes(string reportTitle, Dictionary<string, string> summaryKpis, string[] headers, List<string[]> rows)
    {
        var lines = new List<string>
        {
            "OPTICA - SISTEMA DE GESTION INTEGRAL",
            reportTitle.ToUpperInvariant(),
            $"Fecha y hora de emision: {DateTime.Now:yyyy-MM-dd HH:mm}",
            "----------------------------------------------------------------------------------"
        };

        if (summaryKpis != null && summaryKpis.Count > 0)
        {
            lines.Add("RESUMEN GENERAL DEL REPORTE:");
            foreach (var kvp in summaryKpis)
            {
                lines.Add($"  * {kvp.Key}: {kvp.Value}");
            }
            lines.Add("----------------------------------------------------------------------------------");
        }

        lines.Add("DETALLE DEL REPORTE:");
        lines.Add(string.Join(" | ", headers));
        lines.Add("----------------------------------------------------------------------------------");

        var maxRows = Math.Min(rows.Count, 120);
        for (int i = 0; i < maxRows; i++)
        {
            var r = rows[i];
            lines.Add(string.Join(" | ", r));
        }

        if (rows.Count > 120)
        {
            lines.Add($"... y {rows.Count - 120} registros adicionales no mostrados en la vista previa impresa.");
        }

        lines.Add("----------------------------------------------------------------------------------");
        lines.Add("Fin del informe oficial - Optometria App");

        return SimplePdfGenerator.Build(reportTitle, lines);
    }
    #endregion
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
