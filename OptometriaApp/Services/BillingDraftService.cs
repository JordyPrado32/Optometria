using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class BillingDraftService
{
    public async Task EnsureAppointmentBillingEntriesAsync(OpticaDbContext dbContext, int appointmentId, int actorUserId, string actorLabel)
    {
        var appointment = await dbContext.tbl_citas
            .Include(x => x.id_medicoNavigation)
            .Include(x => x.id_pacienteNavigation)
            .Include(x => x.id_consultaNavigation)
            .FirstOrDefaultAsync(x => x.id_cita == appointmentId);

        if (appointment is null)
        {
            return;
        }

        var clientId = await EnsureBillingClientAsync(dbContext, appointment.id_pacienteNavigation, actorUserId);
        var draft = await GetOrCreateDraftSaleAsync(dbContext, appointment.id_paciente, clientId, actorUserId);
        var appointmentProduct = await GetOrCreateServiceProductAsync(
            dbContext,
            "SRV-CITA-COMPLETADA",
            "Consulta optometrica",
            "Servicio autogenerado al completar una cita.");

        var appointmentPrice = appointment.id_medicoNavigation.precio_consulta_base ?? 0m;
        await EnsureSourceLineAsync(
            dbContext,
            draft,
            appointmentProduct.id_producto,
            "Cita",
            appointment.id_cita,
            BuildAppointmentConcept(appointment),
            1,
            appointmentPrice);

        if (appointment.id_consulta.HasValue)
        {
            await EnsureConsultationBillingEntriesAsync(dbContext, appointment.id_consulta.Value, actorUserId, draft.id_venta);
        }

        draft.concepto = $"Pendientes de facturacion - {appointment.id_pacienteNavigation.nombres} {appointment.id_pacienteNavigation.apellidos}".Trim();
        draft.id_cliente_facturacion = clientId;
        draft.id_usuario = actorUserId;
        draft.fecha_venta ??= DateTime.Now;
        draft.forma_pago ??= "Efectivo";
        draft.dias_credito ??= 0;
        await RecalculateSaleAsync(dbContext, draft);
    }

    public async Task EnsureConsultationBillingEntriesAsync(OpticaDbContext dbContext, int consultationId, int actorUserId, int? existingDraftSaleId = null)
    {
        var consultation = await dbContext.tbl_consulta
            .Include(x => x.id_pacienteNavigation)
            .Include(x => x.id_optometraNavigation)
            .FirstOrDefaultAsync(x => x.id_consulta == consultationId);

        if (consultation is null || !HasExamContent(consultation))
        {
            return;
        }

        var clientId = await EnsureBillingClientAsync(dbContext, consultation.id_pacienteNavigation, actorUserId);
        var draft = existingDraftSaleId.HasValue
            ? await dbContext.tbl_venta.Include(x => x.tbl_detalle_venta).FirstOrDefaultAsync(x => x.id_venta == existingDraftSaleId.Value)
            : null;

        draft ??= await GetOrCreateDraftSaleAsync(dbContext, consultation.id_paciente, clientId, actorUserId);

        var examProduct = await GetOrCreateServiceProductAsync(
            dbContext,
            "SRV-EXAMEN-OPTICO",
            "Examen optometrico",
            "Servicio autogenerado desde consulta o examen.");

        await EnsureSourceLineAsync(
            dbContext,
            draft,
            examProduct.id_producto,
            "Consulta",
            consultation.id_consulta,
            BuildConsultationConcept(consultation),
            1,
            0m);

        draft.id_cliente_facturacion = clientId;
        draft.id_usuario = actorUserId;
        draft.fecha_venta ??= DateTime.Now;
        draft.forma_pago ??= "Efectivo";
        draft.dias_credito ??= 0;
        await RecalculateSaleAsync(dbContext, draft);
    }

    public async Task<int> EnsureBillingClientAsync(OpticaDbContext dbContext, tbl_paciente patient, int actorUserId)
    {
        var identification = patient.cedula.Trim();
        var existingClient = await dbContext.clients
            .FirstOrDefaultAsync(x => x.id_usuario_creacion == actorUserId && x.numero_identificacion == identification);

        if (existingClient is not null)
        {
            existingClient.razon_social = $"{patient.nombres} {patient.apellidos}".Trim();
            existingClient.nombres = patient.nombres;
            existingClient.apellidos = patient.apellidos;
            existingClient.direccion = patient.direccion;
            existingClient.telefono = patient.telefono;
            existingClient.correo_electronico = patient.email;
            existingClient.fecha_actualizacion = DateTime.Now;
            existingClient.id_usuario_actualizacion = actorUserId;
            existingClient.condicion_pago ??= "Contado";
            return existingClient.cliente_id;
        }

        var client = new ClientEntity
        {
            tipo_cliente = "Natural",
            tipo_identificacion = InferIdentificationType(identification),
            numero_identificacion = identification,
            razon_social = $"{patient.nombres} {patient.apellidos}".Trim(),
            nombres = patient.nombres,
            apellidos = patient.apellidos,
            direccion = patient.direccion,
            telefono = patient.telefono,
            correo_electronico = patient.email,
            condicion_pago = "Contado",
            dias_plazo = 0,
            limite_credito = 0,
            saldo_deudor = 0,
            estado = true,
            es_consumidor_final = false,
            id_usuario_creacion = actorUserId,
            fecha_creacion = DateTime.Now,
            fecha_actualizacion = DateTime.Now
        };

        dbContext.clients.Add(client);
        await dbContext.SaveChangesAsync();
        return client.cliente_id;
    }

    public async Task<tbl_venta> GetOrCreateDraftSaleAsync(OpticaDbContext dbContext, int? patientId, int? clientId, int actorUserId)
    {
        tbl_venta? draft = null;

        if (clientId.HasValue && clientId.Value > 0)
        {
            draft = await dbContext.tbl_venta
                .Include(x => x.tbl_detalle_venta)
                .FirstOrDefaultAsync(x => x.id_cliente_facturacion == clientId.Value && (x.estado == "Pendiente" || x.estado == "Borrador"));
        }

        if (draft is null && patientId.HasValue && patientId.Value > 0)
        {
            draft = await dbContext.tbl_venta
                .Include(x => x.tbl_detalle_venta)
                .FirstOrDefaultAsync(x => x.id_paciente == patientId.Value && (x.estado == "Pendiente" || x.estado == "Borrador"));
        }

        if (draft is not null)
        {
            if (clientId.HasValue)
            {
                draft.id_cliente_facturacion = clientId.Value;
            }

             if (draft.id_paciente <= 0 && patientId.HasValue && patientId.Value > 0)
            {
                draft.id_paciente = patientId.Value;
            }

            draft.id_usuario = actorUserId;
            draft.fecha_venta ??= DateTime.Now;
            draft.estado = "Pendiente";
            draft.forma_pago ??= "Efectivo";
            draft.dias_credito ??= 0;
            draft.porcentaje_impuesto ??= 0;
            return draft;
        }

        if (!patientId.HasValue || patientId.Value <= 0)
        {
            throw new InvalidOperationException("Selecciona un paciente valido antes de iniciar un borrador de factura. La base actual requiere asociar cada venta a un paciente.");
        }

        if (!clientId.HasValue || clientId.Value <= 0)
        {
            throw new InvalidOperationException("Selecciona un cliente valido antes de iniciar un nuevo borrador de factura.");
        }

        draft = new tbl_venta
        {
            id_paciente = patientId.Value,
            id_usuario = actorUserId,
            id_cliente_facturacion = clientId,
            fecha_venta = DateTime.Now,
            subtotal = 0,
            porcentaje_impuesto = 0,
            impuesto_total = 0,
            descuento_total = 0,
            total = 0,
            valor_cobrado = 0,
            saldo_pendiente = 0,
            estado = "Pendiente",
            concepto = "Pendientes de facturacion",
            forma_pago = "Efectivo",
            dias_credito = 0
        };

        dbContext.tbl_venta.Add(draft);
        await dbContext.SaveChangesAsync();
        return draft;
    }

    public async Task RecalculateSaleAsync(OpticaDbContext dbContext, tbl_venta sale)
    {
        var lineSnapshots = await dbContext.tbl_detalle_venta
            .Where(x => x.id_venta == sale.id_venta)
            .Select(x => new
            {
                BaseAmount = x.total_item ?? ((x.precio_unitario ?? 0m) * x.cantidad) - (x.descuento ?? 0m),
                Discount = x.descuento ?? 0m,
                HasTax = x.id_productoNavigation.tiene_iva ?? false,
                TaxRate = x.id_productoNavigation.porcentaje_iva ?? 0m
            })
            .ToListAsync();

        var subtotal = lineSnapshots.Sum(x => x.BaseAmount);
        var discount = lineSnapshots.Sum(x => x.Discount);
        var taxableBase = Math.Max(0m, subtotal);
        var tax = lineSnapshots.Sum(x =>
        {
            if (!x.HasTax || x.BaseAmount <= 0m)
            {
                return 0m;
            }

            return Math.Round(x.BaseAmount * (x.TaxRate / 100m), 2, MidpointRounding.AwayFromZero);
        });
        var total = taxableBase + tax;
        var collected = Math.Min(sale.valor_cobrado ?? 0m, total);
        var effectiveTaxRate = taxableBase > 0m
            ? Math.Round((tax * 100m) / taxableBase, 2, MidpointRounding.AwayFromZero)
            : 0m;

        sale.subtotal = taxableBase;
        sale.descuento_total = discount;
        sale.porcentaje_impuesto = effectiveTaxRate;
        sale.impuesto_total = tax;
        sale.total = total;
        sale.valor_cobrado = collected;
        sale.saldo_pendiente = Math.Max(0m, total - collected);
    }

    public async Task<long> GenerateNextInvoiceSequenceAsync(OpticaDbContext dbContext, EmisorEntity issuer)
    {
        var nextSequence = (await GetLastInvoiceSequenceAsync(dbContext, issuer) ?? 0) + 1;

        return nextSequence;
    }

    public async Task<long?> GetLastInvoiceSequenceAsync(OpticaDbContext dbContext, EmisorEntity issuer)
    {
        var prefix = GetInvoicePrefix(issuer);
        return await dbContext.tbl_comprobantes
            .Where(x =>
                x.tipo_comprobante == "Factura" &&
                x.id_emisor == issuer.emisor_id &&
                x.numero_comprobante != null &&
                x.numero_comprobante.StartsWith(prefix + "-"))
            .MaxAsync(x => (long?)x.secuencial);
    }

    public async Task<string> GenerateNextInvoiceNumberAsync(OpticaDbContext dbContext, EmisorEntity issuer)
    {
        var nextSequence = await GenerateNextInvoiceSequenceAsync(dbContext, issuer);
        return BuildInvoiceNumber(issuer, nextSequence);
    }

    public static string BuildInvoiceNumber(EmisorEntity issuer, long sequence)
    {
        return $"{GetInvoicePrefix(issuer)}-{sequence:D9}";
    }

    public static string GetInvoicePrefix(EmisorEntity issuer)
    {
        return $"{NormalizeEmissionCode(issuer.establecimiento_codigo)}-{NormalizeEmissionCode(issuer.punto_emision_codigo)}";
    }

    private async Task EnsureSourceLineAsync(
        OpticaDbContext dbContext,
        tbl_venta sale,
        int productId,
        string sourceType,
        int sourceId,
        string concept,
        int quantity,
        decimal unitPrice)
    {
        if (!dbContext.Entry(sale).Collection(x => x.tbl_detalle_venta).IsLoaded)
        {
            await dbContext.Entry(sale).Collection(x => x.tbl_detalle_venta).LoadAsync();
        }

        var existingLine = sale.tbl_detalle_venta.FirstOrDefault(x => x.origen_tipo == sourceType && x.origen_id == sourceId);
        if (existingLine is not null)
        {
            existingLine.id_producto = productId;
            existingLine.cantidad = quantity;
            existingLine.precio_unitario = unitPrice;
            existingLine.concepto_item = concept;
            existingLine.total_item = Math.Max(0m, (unitPrice * quantity) - (existingLine.descuento ?? 0m));
            return;
        }

        var newLine = new tbl_detalle_venta
        {
            id_venta = sale.id_venta,
            id_producto = productId,
            cantidad = quantity,
            precio_unitario = unitPrice,
            descuento = 0,
            concepto_item = concept,
            total_item = unitPrice * quantity,
            origen_tipo = sourceType,
            origen_id = sourceId
        };

        sale.tbl_detalle_venta.Add(newLine);
        dbContext.tbl_detalle_venta.Add(newLine);
    }

    private async Task<tbl_producto> GetOrCreateServiceProductAsync(OpticaDbContext dbContext, string code, string name, string description)
    {
        var product = await dbContext.tbl_productos.FirstOrDefaultAsync(x => x.codigo_producto == code);
        if (product is not null)
        {
            if (product.activo != true)
            {
                product.activo = true;
            }

            product.nombre_producto = name;
            product.descripcion = description;
            product.tipo_item = "Servicio";
            product.naturaleza_item = "Servicio";
            return product;
        }

        product = new tbl_producto
        {
            codigo_producto = code,
            nombre_producto = name,
            descripcion = description,
            tipo_item = "Servicio",
            naturaleza_item = "Servicio",
            precio_venta = 0,
            tiene_iva = false,
            porcentaje_iva = 0,
            stock_actual = 0,
            activo = true,
            fecha_creacion = DateTime.Now,
            usuario_creacion = "SYSTEM_BILLING"
        };

        dbContext.tbl_productos.Add(product);
        await dbContext.SaveChangesAsync();
        return product;
    }

    private static bool HasExamContent(tbl_consulta consultation)
    {
        return !string.IsNullOrWhiteSpace(consultation.examenes_preliminares) ||
               !string.IsNullOrWhiteSpace(consultation.examenes_varios) ||
               !string.IsNullOrWhiteSpace(consultation.evaluaciones);
    }

    private static string BuildAppointmentConcept(tbl_citas appointment)
    {
        var patientName = $"{appointment.id_pacienteNavigation.nombres} {appointment.id_pacienteNavigation.apellidos}".Trim();
        return $"Consulta optometrica {appointment.fecha_cita:yyyy-MM-dd} - {patientName}";
    }

    private static string BuildConsultationConcept(tbl_consulta consultation)
    {
        var patientName = $"{consultation.id_pacienteNavigation.nombres} {consultation.id_pacienteNavigation.apellidos}".Trim();
        return $"Examen pendiente de facturar - {patientName} - Consulta #{consultation.id_consulta}";
    }

    private static string InferIdentificationType(string identification)
    {
        return identification.Length switch
        {
            13 => "04",
            10 => "05",
            _ => "06"
        };
    }

    private static string NormalizeEmissionCode(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return "000";
        }

        return digits.Length >= 3 ? digits[^3..] : digits.PadLeft(3, '0');
    }
}
