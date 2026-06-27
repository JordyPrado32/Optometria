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
                                      difference.TotalHours > 23;
            var needs1HourReminder = appointment.recordatorio_1hr != true &&
                                     difference.TotalMinutes <= 60 &&
                                     difference.TotalMinutes > 0;

            if (!needs24HourReminder && !needs1HourReminder)
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
            var reminderWindow = needs24HourReminder ? "24 horas antes" : "1 hora antes";
            var appointmentType = string.IsNullOrWhiteSpace(appointment.tipo_cita) ? "Presencial" : appointment.tipo_cita!;
            var statusLabel = appointment.id_estadoNavigation?.nombre_estado ?? "Programada";

            await sender.SendAppointmentReminderAsync(
                patientEmail,
                string.IsNullOrWhiteSpace(patientName) ? "Paciente" : patientName,
                string.IsNullOrWhiteSpace(doctorName) ? "Profesional asignado" : doctorName,
                appointment.fecha_cita,
                appointment.hora_inicio,
                appointmentType,
                statusLabel,
                reminderWindow,
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

            if (needs1HourReminder)
            {
                appointment.recordatorio_1hr = true;
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

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
