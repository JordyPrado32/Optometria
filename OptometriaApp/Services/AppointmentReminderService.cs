using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class AppointmentReminderService : BackgroundService
{
    private static readonly string[] EligibleStates = ["Programada", "Confirmada", "Reprogramada"];
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<AppointmentReminderService> logger;

    public AppointmentReminderService(IServiceScopeFactory scopeFactory, ILogger<AppointmentReminderService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando recordatorios de citas.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<EmailSender>();
        var customizationService = scope.ServiceProvider.GetRequiredService<OpticaCustomizationService>();

        if (!sender.IsConfigured())
        {
            return;
        }

        var now = DateTime.Now;
        var upcomingAppointments = await dbContext.tbl_citas
            .Include(c => c.id_medicoNavigation)
                .ThenInclude(m => m.id_usuarioNavigation)
            .Include(c => c.id_pacienteNavigation)
            .Include(c => c.id_estadoNavigation)
            .Where(c =>
                c.id_estadoNavigation != null &&
                EligibleStates.Contains(c.id_estadoNavigation.nombre_estado) &&
                c.id_pacienteNavigation.email != null)
            .ToListAsync(cancellationToken);

        var hasChanges = false;
        var emailTemplate = await customizationService.GetTemplateContentAsync(
            "Email",
            OpticaCustomizationService.PatientReminderEmailTemplateType,
            OpticaCustomizationService.DefaultPatientReminderEmailTemplate,
            cancellationToken);

        foreach (var appointment in upcomingAppointments)
        {
            var appointmentDateTime = appointment.fecha_cita.ToDateTime(appointment.hora_inicio);
            if (appointmentDateTime <= now)
            {
                continue;
            }

            var difference = appointmentDateTime - now;
            var needs24HourReminder = appointment.recordatorio_24hrs != true &&
                                      difference.TotalHours <= 24 &&
                                      difference.TotalHours > 12;
            var needs12HourReminder = appointment.recordatorio_12hrs != true &&
                                      difference.TotalHours <= 12 &&
                                      difference.TotalHours > 1;
            var needs1HourReminder = appointment.recordatorio_1hr != true &&
                                     difference.TotalMinutes <= 60 &&
                                     difference.TotalMinutes > 0;

            if (!needs24HourReminder && !needs12HourReminder && !needs1HourReminder)
            {
                continue;
            }

            var patientEmail = appointment.id_pacienteNavigation.email?.Trim();
            if (string.IsNullOrWhiteSpace(patientEmail))
            {
                continue;
            }

            var patientName = $"{appointment.id_pacienteNavigation.nombres} {appointment.id_pacienteNavigation.apellidos}".Trim();
            var doctorName = $"{appointment.id_medicoNavigation.id_usuarioNavigation.nombres} {appointment.id_medicoNavigation.id_usuarioNavigation.apellidos}".Trim();
            var reminderWindow = needs1HourReminder
                ? "1 hora antes"
                : needs12HourReminder
                    ? "12 horas antes"
                    : "24 horas antes";
            var appointmentType = string.IsNullOrWhiteSpace(appointment.tipo_cita) ? "Presencial" : appointment.tipo_cita!;
            var statusLabel = appointment.id_estadoNavigation?.nombre_estado ?? "Programada";
            var reminderBody = OpticaCustomizationService.RenderTemplate(
                emailTemplate,
                new Dictionary<string, string>
                {
                    ["patient_name"] = string.IsNullOrWhiteSpace(patientName) ? "Paciente" : patientName,
                    ["doctor_name"] = string.IsNullOrWhiteSpace(doctorName) ? "Profesional asignado" : doctorName,
                    ["appointment_date"] = appointment.fecha_cita.ToString("yyyy-MM-dd"),
                    ["appointment_time"] = appointment.hora_inicio.ToString("HH:mm"),
                    ["appointment_type"] = appointmentType,
                    ["status_label"] = statusLabel,
                    ["reminder_window"] = reminderWindow
                });

            await sender.SendAppointmentReminderAsync(
                patientEmail,
                string.IsNullOrWhiteSpace(patientName) ? "Paciente" : patientName,
                string.IsNullOrWhiteSpace(doctorName) ? "Profesional asignado" : doctorName,
                appointment.fecha_cita,
                appointment.hora_inicio,
                appointmentType,
                statusLabel,
                reminderWindow,
                reminderBody,
                $"Recordatorio de cita optométrica ({reminderWindow}) · Óptica Lux",
                cancellationToken);

            appointment.notificacion_enviada = true;
            appointment.fecha_notificacion_enviada = now;
            appointment.tipo_notificacion = $"Email {reminderWindow}";
            appointment.fecha_actualizacion = now;
            appointment.usuario_actualizacion = "SYSTEM_REMINDER";

            if (needs24HourReminder)
            {
                appointment.recordatorio_24hrs = true;
            }

            if (needs12HourReminder)
            {
                appointment.recordatorio_12hrs = true;
                appointment.recordatorio_24hrs = true;
            }

            if (needs1HourReminder)
            {
                appointment.recordatorio_1hr = true;
                appointment.recordatorio_12hrs = true;
                appointment.recordatorio_24hrs = true;
            }

            dbContext.tbl_comunicacions.Add(new tbl_comunicacion
            {
                id_paciente = appointment.id_paciente,
                id_usuario = appointment.id_medicoNavigation.id_usuario,
                canal = "Email",
                destinatario = patientEmail,
                contenido_resumen = $"Recordatorio cita {appointment.id_cita} ({reminderWindow})",
                fecha_envio = now
            });

            dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
            {
                id_usuario = appointment.id_medicoNavigation.id_usuario,
                accion = "Recordatorio de cita",
                modulo = "Citas",
                fecha = now,
                detalle = $"CitaId={appointment.id_cita}; PacienteId={appointment.id_paciente}; DoctorId={appointment.id_medico}; Ventana={reminderWindow}; Canal=Email"
            });

            hasChanges = true;
        }

        await ProcessUnconfirmedSlotReleaseAsync(dbContext, customizationService, now, cancellationToken);

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ProcessUnconfirmedSlotReleaseAsync(
        OpticaDbContext dbContext,
        OpticaCustomizationService customizationService,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var releaseHours = await customizationService.GetAppointmentReleaseHoursAsync(cancellationToken);
        var thresholdTime = now.AddHours(releaseHours);

        var cancelledState = await dbContext.tbl_estado_cita
            .FirstOrDefaultAsync(x => x.nombre_estado == "Cancelada", cancellationToken);

        if (cancelledState is null)
        {
            return;
        }

        var unconfirmedAppointments = await dbContext.tbl_citas
            .Include(c => c.id_estadoNavigation)
            .Include(c => c.id_pacienteNavigation)
            .Include(c => c.id_medicoNavigation)
                .ThenInclude(m => m.id_usuarioNavigation)
            .Where(c =>
                c.id_estadoNavigation != null &&
                c.id_estadoNavigation.nombre_estado == "Programada")
            .ToListAsync(cancellationToken);

        var releasedCount = 0;
        foreach (var appointment in unconfirmedAppointments)
        {
            var appointmentDateTime = appointment.fecha_cita.ToDateTime(appointment.hora_inicio);
            if (appointmentDateTime > now && appointmentDateTime <= thresholdTime)
            {
                appointment.id_estado = cancelledState.id_estado;
                appointment.razon_cancelacion = $"Liberación automática de cupo por falta de confirmación (umbral {releaseHours}h).";
                appointment.fecha_actualizacion = now;
                appointment.usuario_actualizacion = "SYSTEM_AUTO_RELEASE";

                var doctorUserId = appointment.id_medicoNavigation?.id_usuario ?? 1;
                dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
                {
                    id_usuario = doctorUserId,
                    accion = "Liberación automática de cupo",
                    modulo = "Citas",
                    fecha = now,
                    detalle = $"CitaId={appointment.id_cita}; Paciente={appointment.id_pacienteNavigation?.nombres} {appointment.id_pacienteNavigation?.apellidos}; Motivo=Liberacion automatica por falta de confirmacion ({releaseHours}h)"
                });

                releasedCount++;
            }
        }

        if (releasedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Se liberaron {Count} citas no confirmadas de forma automática.", releasedCount);
        }
    }
}
