using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Options;
using OptometriaApp.Configuration;

namespace OptometriaApp.Services;

public sealed class EmailSender
{
    private readonly SmtpSettings settings;

    public EmailSender(IOptions<SmtpSettings> options)
    {
        settings = options.Value;
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(settings.Host)
            && settings.Port > 0
            && !string.IsNullOrWhiteSpace(settings.FromAddress)
            && !string.IsNullOrWhiteSpace(settings.UserName)
            && !string.IsNullOrWhiteSpace(settings.Password);
    }

    public async Task SendTemporaryPasswordAsync(string destinationEmail, string destinationName, string temporaryPassword, int minutesValid, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la seccion Smtp en appsettings.json.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = "Recuperacion de acceso - clave temporal",
            Body = $"""
Hola {destinationName},

Tu clave temporal para ingresar al sistema es:

{temporaryPassword}

Esta clave vencera en {minutesValid} minutos. Despues de usarla, el sistema te pedira cambiar tu contrasena.

Si no solicitaste este acceso, ignora este correo y avisa al administrador.
""",
            IsBodyHtml = false
        };

        message.To.Add(destinationEmail);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = new NetworkCredential(settings.UserName, settings.Password)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    public async Task SendAppointmentReminderAsync(
        string destinationEmail,
        string destinationName,
        string doctorName,
        DateOnly appointmentDate,
        TimeOnly startTime,
        string appointmentType,
        string statusLabel,
        string reminderWindow,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la seccion Smtp en appsettings.json.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = $"Recordatorio de cita optometrica ({reminderWindow})",
            Body = $"""
Hola {destinationName},

Te recordamos que tienes una cita {reminderWindow} con el profesional {doctorName}.

Fecha: {appointmentDate:yyyy-MM-dd}
Hora: {startTime:HH\:mm}
Tipo: {appointmentType}
Estado actual: {statusLabel}

Si necesitas reprogramarla o cancelarla, ingresa al sistema cuanto antes.
""",
            IsBodyHtml = false
        };

        message.To.Add(destinationEmail);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = new NetworkCredential(settings.UserName, settings.Password)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    public async Task SendAccountStatementAsync(
        string destinationEmail,
        string destinationName,
        string subject,
        string body,
        string attachmentFileName,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la seccion Smtp en appsettings.json.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        message.To.Add(destinationEmail);
        message.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), attachmentFileName, MediaTypeNames.Application.Pdf));

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = new NetworkCredential(settings.UserName, settings.Password)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }
}
