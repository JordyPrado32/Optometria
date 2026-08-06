using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class NotificationService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public NotificationService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return 0;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.tbl_notificaciones
            .AsNoTracking()
            .CountAsync(x => x.id_usuario_destino == userId && !x.leida, cancellationToken);
    }

    public async Task<List<tbl_notificacion>> GetRecentForUserAsync(int userId, int take = 8, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.tbl_notificaciones
            .AsNoTracking()
            .Where(x => x.id_usuario_destino == userId)
            .OrderByDescending(x => x.fecha_creacion)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsReadAsync(int userId, int notificationId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.tbl_notificaciones
            .FirstOrDefaultAsync(x => x.id_notificacion == notificationId && x.id_usuario_destino == userId, cancellationToken);

        if (entity is null || entity.leida)
        {
            return;
        }

        entity.leida = true;
        entity.fecha_lectura = DateTime.Now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await dbContext.tbl_notificaciones
            .Where(x => x.id_usuario_destino == userId && !x.leida)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var item in pending)
        {
            item.leida = true;
            item.fecha_lectura = DateTime.Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int userId, int notificationId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.tbl_notificaciones
            .FirstOrDefaultAsync(x => x.id_notificacion == notificationId && x.id_usuario_destino == userId, cancellationToken);

        if (entity is null)
        {
            return;
        }

        dbContext.tbl_notificaciones.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task NotifyTransferRequestedAsync(int transferRequestId, CancellationToken cancellationToken = default)
        => NotifyTransferRequestedInternalAsync(transferRequestId, cancellationToken);

    public Task NotifyTransferApprovedAsync(int transferRequestId, bool approved, CancellationToken cancellationToken = default)
        => NotifyTransferResolvedInternalAsync(transferRequestId, approved, cancellationToken);

    public Task NotifyPaymentAppliedAsync(int accountId, decimal amount, string sourceLabel, int? originUserId, CancellationToken cancellationToken = default)
        => NotifyPaymentAppliedInternalAsync(accountId, amount, sourceLabel, originUserId, cancellationToken);

    public Task NotifyAppointmentCreatedAsync(int appointmentId, int? patientUserId, int? doctorUserId, int? actorUserId, string patientLabel, string doctorLabel, DateOnly appointmentDate, TimeOnly startTime, CancellationToken cancellationToken = default)
        => NotifyAppointmentCreatedInternalAsync(appointmentId, patientUserId, doctorUserId, actorUserId, patientLabel, doctorLabel, appointmentDate, startTime, cancellationToken);

    public Task NotifyAppointmentRescheduledAsync(int appointmentId, int? patientUserId, int? doctorUserId, int? actorUserId, string patientLabel, string doctorLabel, DateOnly appointmentDate, TimeOnly startTime, string updatedByLabel, CancellationToken cancellationToken = default)
        => NotifyAppointmentRescheduledInternalAsync(appointmentId, patientUserId, doctorUserId, actorUserId, patientLabel, doctorLabel, appointmentDate, startTime, updatedByLabel, cancellationToken);

    private async Task NotifyTransferRequestedInternalAsync(int transferRequestId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await dbContext.tbl_cobro_transferencias
            .AsNoTracking()
            .Include(x => x.id_comprobanteNavigation)
            .Include(x => x.id_usuario_solicitaNavigation)
            .FirstOrDefaultAsync(x => x.id_cobro_transferencia == transferRequestId, cancellationToken);

        if (request is null)
        {
            return;
        }

        var requesterName = $"{request.id_usuario_solicitaNavigation.nombres} {request.id_usuario_solicitaNavigation.apellidos}".Trim();
        var sourceLabel = GetTransferSourceLabel(request);

        await CreateNotificationsAsync(
            dbContext,
            [request.id_usuario_solicita],
            request.id_usuario_solicita,
            "Compra online recibida",
            "Recibimos tu comprobante. Desde tu historial podras seguir la aprobacion y el retiro de tus productos.",
            "Gestion tienda online",
            "/mis-compras-online",
            "CobroTransferencia",
            request.id_cobro_transferencia,
            "GestionTiendaOnline",
            cancellationToken);

        var approverIds = await GetUsersWithRouteAccessAsync(dbContext, "/gestion-tienda-online", cancellationToken);
        approverIds.Remove(request.id_usuario_solicita);

        await CreateNotificationsAsync(
            dbContext,
            approverIds,
            request.id_usuario_solicita,
            "Compra online por aprobar",
            $"{requesterName} reporto un comprobante por {request.monto:0.00} para {sourceLabel}. Revisa el pago y define cuando estaran listos los productos.",
            "Gestion tienda online",
            "/gestion-tienda-online",
            "CobroTransferencia",
            request.id_cobro_transferencia,
            "GestionTiendaOnline",
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyTransferResolvedInternalAsync(int transferRequestId, bool approved, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var request = await dbContext.tbl_cobro_transferencias
            .AsNoTracking()
            .Include(x => x.id_comprobanteNavigation)
            .FirstOrDefaultAsync(x => x.id_cobro_transferencia == transferRequestId, cancellationToken);

        if (request is null)
        {
            return;
        }

        var sourceLabel = GetTransferSourceLabel(request);
        var title = approved ? "Compra online aprobada" : "Compra online rechazada";
        var pickupMessage = request.fecha_retiro_estimada.HasValue
            ? $" Puedes retirar en tienda fisica desde {request.fecha_retiro_estimada.Value:dd/MM/yyyy HH:mm}."
            : " Te avisaremos cuando tus productos esten listos para retirar en tienda fisica.";
        var message = approved
            ? $"Tu compra online para {sourceLabel} fue aprobada.{pickupMessage}"
            : $"Tu compra online para {sourceLabel} fue rechazada. Revisa la referencia o contacta al equipo.";

        await CreateNotificationsAsync(
            dbContext,
            [request.id_usuario_solicita],
            request.id_usuario_aprueba,
            title,
            message,
            approved ? "Exito" : "Error",
            "/mis-compras-online",
            "CobroTransferencia",
            request.id_cobro_transferencia,
            "GestionTiendaOnline",
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetTransferSourceLabel(tbl_cobro_transferencia request)
    {
        if (request.id_comprobanteNavigation is not null)
        {
            return request.id_comprobanteNavigation.numero_comprobante ?? "comprobante";
        }

        return request.id_cta_cobrar.HasValue
            ? $"Cuenta {request.id_cta_cobrar.Value}"
            : "compra online";
    }

    private async Task NotifyPaymentAppliedInternalAsync(int accountId, decimal amount, string sourceLabel, int? originUserId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await dbContext.tbl_cta_cobrar
            .AsNoTracking()
            .Include(x => x.id_comprobanteNavigation)
            .FirstOrDefaultAsync(x => x.id_cta_cobrar == accountId, cancellationToken);

        if (account is null)
        {
            return;
        }

        var invoiceNumber = account.id_comprobanteNavigation?.numero_comprobante ?? $"Cuenta {account.id_cta_cobrar}";
        var viewers = await GetUsersWithRouteAccessAsync(dbContext, "/cuentas-por-cobrar", cancellationToken);
        var transferApprovers = await GetUsersWithRouteAccessAsync(dbContext, "/gestion-tienda-online", cancellationToken);
        viewers.UnionWith(transferApprovers);

        await CreateNotificationsAsync(
            dbContext,
            viewers,
            originUserId,
            "Pago registrado",
            $"Se registro un pago de {amount:0.00} para {invoiceNumber} mediante {sourceLabel}.",
            "Pago",
            "/cuentas-por-cobrar",
            "CuentaPorCobrar",
            account.id_cta_cobrar,
            "CuentasPorCobrar",
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAppointmentCreatedInternalAsync(int appointmentId, int? patientUserId, int? doctorUserId, int? actorUserId, string patientLabel, string doctorLabel, DateOnly appointmentDate, TimeOnly startTime, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var recipients = await GetUsersWithRouteAccessAsync(dbContext, "/citas", cancellationToken);
        if (patientUserId.HasValue && patientUserId.Value > 0)
        {
            recipients.Add(patientUserId.Value);
        }

        if (doctorUserId.HasValue && doctorUserId.Value > 0)
        {
            recipients.Add(doctorUserId.Value);
        }

        await CreateNotificationsAsync(
            dbContext,
            recipients,
            actorUserId,
            "Nueva cita registrada",
            $"Se agendo una cita para {patientLabel} con {doctorLabel} el {appointmentDate:yyyy-MM-dd} a las {startTime:HH\\:mm}.",
            "Cita",
            "/citas",
            "Cita",
            appointmentId,
            "Citas",
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAppointmentRescheduledInternalAsync(int appointmentId, int? patientUserId, int? doctorUserId, int? actorUserId, string patientLabel, string doctorLabel, DateOnly appointmentDate, TimeOnly startTime, string updatedByLabel, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var recipients = await GetUsersWithRouteAccessAsync(dbContext, "/citas", cancellationToken);
        if (patientUserId.HasValue && patientUserId.Value > 0)
        {
            recipients.Add(patientUserId.Value);
        }

        if (doctorUserId.HasValue && doctorUserId.Value > 0)
        {
            recipients.Add(doctorUserId.Value);
        }

        await CreateNotificationsAsync(
            dbContext,
            recipients,
            actorUserId,
            "Cita reprogramada",
            $"La cita de {patientLabel} con {doctorLabel} fue reprogramada para el {appointmentDate:yyyy-MM-dd} a las {startTime:HH\\:mm}. Cambio realizado por {updatedByLabel}.",
            "Cita",
            "/citas",
            "Cita",
            appointmentId,
            "Citas",
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> GetUsersWithRouteAccessAsync(OpticaDbContext dbContext, string route, CancellationToken cancellationToken)
    {
        var userIds = await (
            from user in dbContext.tbl_usuarios.AsNoTracking()
            where user.activo == true
            join permission in dbContext.tbl_rol_menu_permisos.AsNoTracking() on user.id_rol equals permission.id_rol
            join menu in dbContext.tbl_menu_apps.AsNoTracking() on permission.id_menu equals menu.id_menu
            where permission.puede_ver && menu.ruta == route
            select user.id_usuario)
            .Distinct()
            .ToListAsync(cancellationToken);

        return userIds.ToHashSet();
    }

    private static Task CreateNotificationsAsync(
        OpticaDbContext dbContext,
        IEnumerable<int> userIds,
        int? originUserId,
        string title,
        string message,
        string type,
        string route,
        string entityType,
        int entityId,
        string moduleName,
        CancellationToken cancellationToken)
    {
        var distinctUserIds = userIds.Where(x => x > 0).Distinct().ToList();
        foreach (var userId in distinctUserIds)
        {
            dbContext.tbl_notificaciones.Add(new tbl_notificacion
            {
                id_usuario_destino = userId,
                id_usuario_origen = originUserId > 0 ? originUserId : null,
                titulo = title,
                mensaje = message,
                tipo = type,
                ruta_destino = route,
                entidad_tipo = entityType,
                entidad_id = entityId,
                modulo_origen = moduleName,
                leida = false,
                fecha_creacion = DateTime.Now
            });
        }

        return Task.CompletedTask;
    }
}
