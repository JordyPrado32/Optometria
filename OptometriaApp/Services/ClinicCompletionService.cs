using System.Data;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class ClinicCompletionService(IDbContextFactory<OpticaDbContext> factory)
{
    public static async Task<bool> CanPurchaseAsync(OpticaDbContext db, int userId, bool edit)
    {
        var user = await db.tbl_usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.id_usuario == userId && x.activo == true && x.bloqueado != true);
        return user != null && await db.tbl_rol_menu_permisos.AnyAsync(p => p.id_rol == user.id_rol && p.puede_ver && (!edit || p.puede_editar)
            && db.tbl_menu_apps.Any(m => m.id_menu == p.id_menu && m.activo && m.ruta == "/liquidaciones-de-compra"));
    }

    public static async Task<bool> CanReadConsultationAsync(OpticaDbContext db, int userId, int consultationId)
    {
        if (!await db.tbl_usuarios.AnyAsync(x => x.id_usuario == userId && x.activo == true && x.bloqueado != true)) return false;
        return await db.tbl_consulta.AnyAsync(x => x.id_consulta == consultationId &&
            (x.id_pacienteNavigation.id_usuario == userId ||
             (x.id_optometra == userId && db.tbl_medico.Any(m => m.id_usuario == userId && m.activo == true && m.puede_gestionar_historia_clinica == true)
                && db.tbl_rol_menu_permisos.Any(p => p.puede_ver && db.tbl_usuarios.Any(u => u.id_usuario == userId && u.id_rol == p.id_rol)
                    && db.tbl_menu_apps.Any(m => m.id_menu == p.id_menu && m.activo && m.ruta == "/doctor/historia-clinica")))));
    }

    public static async Task<bool> CanIssueAsync(OpticaDbContext db, int userId, int consultationId) =>
        await CanReadConsultationAsync(db, userId, consultationId) &&
        await db.tbl_medico.AnyAsync(x => x.id_usuario == userId && x.activo == true && x.puede_gestionar_historia_clinica == true) &&
        await db.tbl_rol_menu_permisos.AnyAsync(p => p.puede_ver && (p.puede_crear || p.puede_editar)
            && db.tbl_usuarios.Any(u => u.id_usuario == userId && u.id_rol == p.id_rol)
            && db.tbl_menu_apps.Any(m => m.id_menu == p.id_menu && m.activo && m.ruta == "/doctor/historia-clinica")) &&
        await db.tbl_consulta.AnyAsync(x => x.id_consulta == consultationId && x.id_optometra == userId) &&
        await db.tbl_historia_clinica_optometria_eventos.AnyAsync(x => x.id_consulta == consultationId && x.activo && x.estado == "Cerrada");

    public async Task RecordPaymentAsync(int userId, int liquidationId, decimal amount, string method, string reference, Guid operationId, int? reversesId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await CanPurchaseAsync(db, userId, true)) throw new InvalidOperationException("No tienes permiso para registrar abonos.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var liquidation = await db.tbl_liquidacion_compra.FromSqlInterpolated($"SELECT * FROM dbo.tbl_liquidacion_compra WITH (UPDLOCK, HOLDLOCK) WHERE id_liquidacion_compra = {liquidationId}")
            .FirstOrDefaultAsync(x => x.id_usuario_registro == userId && x.activo == true)
            ?? throw new InvalidOperationException("La liquidacion no esta disponible.");
        if (await db.PurchasePayments.AnyAsync(x => x.OperationId == operationId)) return;
        reference = reference.Trim();
        if (reference.Length is < 3 or > 300) throw new InvalidOperationException("Escribe una referencia o motivo de 3 a 300 caracteres.");
        var paid = await db.PurchasePayments.Where(x => x.LiquidationId == liquidationId).SumAsync(x => (decimal?)x.Amount) ?? 0;
        if (paid != (liquidation.saldo_pagado ?? 0)) throw new InvalidOperationException("El historial no coincide con el saldo. Requiere conciliacion antes de registrar movimientos.");
        if (reversesId.HasValue)
        {
            var original = await db.PurchasePayments.FirstOrDefaultAsync(x => x.Id == reversesId && x.LiquidationId == liquidationId)
                ?? throw new InvalidOperationException("No se encontro el abono.");
            if (original.Amount <= 0 || original.Method == "Saldo inicial" || await db.PurchasePayments.AnyAsync(x => x.ReversesId == original.Id))
                throw new InvalidOperationException("Este movimiento no se puede revertir.");
            amount = -original.Amount;
            method = "Reverso";
        }
        else
        {
            if (!new[] { "Efectivo", "Transferencia", "Tarjeta", "Cheque" }.Contains(method)) throw new InvalidOperationException("Selecciona un metodo valido.");
            PurchasePaymentRules.ValidatePayment(liquidation.total ?? 0, paid, amount);
        }
        db.PurchasePayments.Add(new PurchasePayment { LiquidationId = liquidationId, UserId = userId, OperationId = operationId,
            Amount = amount, CreatedAt = DateTime.UtcNow, Method = method, Reference = reference, ReversesId = reversesId });
        liquidation.saldo_pagado = paid + amount;
        liquidation.saldo_pendiente = (liquidation.total ?? 0) - liquidation.saldo_pagado;
        liquidation.estado_liquidacion = PurchasePaymentRules.State(liquidation.total ?? 0, paid + amount);
        liquidation.fecha_actualizacion = DateTime.Now;
        Audit(db, userId, "Abono compra", $"Liquidacion={liquidationId}; Operacion={operationId}; Importe={amount}; ReversoDe={reversesId}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task IssueCertificateAsync(int userId, int consultationId, string statement, Guid number)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await CanIssueAsync(db, userId, consultationId)) throw new InvalidOperationException("Solo el profesional tratante puede emitir un certificado de una consulta cerrada.");
        if (await db.MedicalCertificates.AnyAsync(x => x.Number == number)) return;
        statement = statement.Trim();
        if (statement.Length is < 20 or > 2000) throw new InvalidOperationException("Redacta el certificado entre 20 y 2000 caracteres.");
        var visit = await db.tbl_consulta.Include(x => x.id_pacienteNavigation).Include(x => x.id_optometraNavigation).SingleAsync(x => x.id_consulta == consultationId);
        var doctor = await db.tbl_medico.FirstAsync(x => x.id_usuario == userId && x.activo == true);
        if (string.IsNullOrWhiteSpace(doctor.numero_licencia) || !visit.fecha_consulta.HasValue) throw new InvalidOperationException("Completa la licencia profesional y la fecha de consulta.");
        db.MedicalCertificates.Add(new MedicalCertificate { ConsultationId = consultationId, UserId = userId, Number = number,
            CreatedAt = DateTime.UtcNow, ConsultationDate = visit.fecha_consulta.Value, Statement = statement,
            PatientName = $"{visit.id_pacienteNavigation.nombres} {visit.id_pacienteNavigation.apellidos}", PatientIdentification = visit.id_pacienteNavigation.cedula ?? "",
            DoctorName = $"{visit.id_optometraNavigation.nombres} {visit.id_optometraNavigation.apellidos}", License = doctor.numero_licencia });
        Audit(db, userId, "Emitir certificado", $"Consulta={consultationId}; Numero={number}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RevokeCertificateAsync(int userId, int id, string reason)
    {
        await using var db = await factory.CreateDbContextAsync();
        var certificate = await db.MedicalCertificates.SingleAsync(x => x.Id == id && x.UserId == userId);
        if (!await CanIssueAsync(db, userId, certificate.ConsultationId)) throw new InvalidOperationException("No tienes permiso para anular este certificado.");
        if (reason.Trim().Length is < 5 or > 300) throw new InvalidOperationException("Escribe el motivo de anulacion entre 5 y 300 caracteres.");
        if (certificate.RevocationReason != null) throw new InvalidOperationException("El certificado ya esta anulado.");
        certificate.RevocationReason = reason.Trim();
        Audit(db, userId, "Anular certificado", $"Certificado={id}; Motivo={reason.Trim()}");
        await db.SaveChangesAsync();
    }

    private static void Audit(OpticaDbContext db, int userId, string action, string detail) => db.tbl_log_auditoria.Add(new tbl_log_auditoria
        { id_usuario = userId, accion = action, modulo = "Clinica", fecha = DateTime.Now, detalle = detail });
}
