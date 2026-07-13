using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using OptometriaApp.Configuration;

namespace OptometriaApp.Services;

public sealed class EmailSender
{
    private readonly SmtpSettings settings;
    private readonly ILogger<EmailSender> logger;

    public EmailSender(IOptions<SmtpSettings> options, ILogger<EmailSender> logger)
    {
        settings = options.Value;
        this.logger = logger;
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(GetHost())
            && settings.Port > 0
            && !string.IsNullOrWhiteSpace(GetFromAddress())
            && !string.IsNullOrWhiteSpace(GetUserName())
            && !string.IsNullOrWhiteSpace(GetPassword());
    }

    public async Task SendTemporaryPasswordAsync(string destinationEmail, string destinationName, string temporaryPassword, int minutesValid, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la seccion Smtp en appsettings.json.");
        }

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
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

        message.To.Add(BuildRecipientAddress(destinationEmail));
        await SendAsync(message, cancellationToken);
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
        string? customBody = null,
        string? customSubject = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la seccion Smtp en appsettings.json.");
        }

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
            Subject = string.IsNullOrWhiteSpace(customSubject) ? $"Recordatorio de cita optometrica ({reminderWindow})" : customSubject,
            Body = string.IsNullOrWhiteSpace(customBody) ? $"""
Hola {destinationName},

Te recordamos que tienes una cita {reminderWindow} con el profesional {doctorName}.

Fecha: {appointmentDate:yyyy-MM-dd}
Hora: {startTime:HH\:mm}
Tipo: {appointmentType}
Estado actual: {statusLabel}

Si necesitas reprogramarla o cancelarla, ingresa al sistema cuanto antes.
""" : customBody,
            IsBodyHtml = false
        };

        message.To.Add(BuildRecipientAddress(destinationEmail));
        await SendAsync(message, cancellationToken);
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
            From = BuildFromAddress(),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        message.To.Add(BuildRecipientAddress(destinationEmail));
        message.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), attachmentFileName, MediaTypeNames.Application.Pdf));
        await SendAsync(message, cancellationToken);
    }

    private async Task SendAsync(MailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(GetHost(), settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(GetUserName(), GetPassword())
        };

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await client.SendMailAsync(message);
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "Error SMTP enviando correo a {Recipients} usando {Host}:{Port}.", string.Join(", ", message.To.Select(m => m.Address)), GetHost(), settings.Port);
            throw;
        }
    }

    private MailAddress BuildFromAddress()
    {
        return new MailAddress(GetFromAddress(), GetFromName(), Encoding.UTF8);
    }

    private static MailAddress BuildRecipientAddress(string destinationEmail)
    {
        return new MailAddress(destinationEmail.Trim());
    }

    private string GetHost() => settings.Host.Trim();

    private string GetUserName() => settings.UserName.Trim();

    private string GetFromAddress() => settings.FromAddress.Trim();

    private string GetFromName() => string.IsNullOrWhiteSpace(settings.FromName) ? "OptometriaApp" : settings.FromName.Trim();

    private string GetPassword()
    {
        var password = settings.Password.Trim();

        // Gmail suele mostrar app passwords agrupadas con espacios visuales.
        if (GetHost().Contains("gmail", StringComparison.OrdinalIgnoreCase) &&
            password.Contains(' ') &&
            password.Replace(" ", string.Empty, StringComparison.Ordinal).Length == 16)
        {
            return password.Replace(" ", string.Empty, StringComparison.Ordinal);
        }

        return password;
    }
}
