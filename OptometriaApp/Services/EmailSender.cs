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
            throw new InvalidOperationException("SMTP no configurado. Completa la sección Smtp en appsettings.json.");
        }

        var detailsHtml = $"""
            <div style="background: #FAF7F2; border: 1px solid #EAE2D5; border-radius: 12px; padding: 20px; text-align: center; margin: 20px 0;">
                <span style="display: block; font-size: 13px; font-weight: 700; color: #7F6951; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 8px;">Clave Temporal de Acceso</span>
                <span style="display: inline-block; font-family: 'Courier New', monospace; font-size: 26px; font-weight: 800; letter-spacing: 4px; color: #276144; background: #ffffff; border: 2px dashed #5DA181; padding: 8px 24px; border-radius: 8px;">{temporaryPassword}</span>
                <span style="display: block; font-size: 12px; color: #8C7864; margin-top: 10px;">Esta clave vencerá en <strong>{minutesValid} minutos</strong>.</span>
            </div>
            """;

        var actionBoxHtml = """
            <div style="background: #FEF8ED; border-left: 4px solid #FEBC64; border-radius: 6px; padding: 14px 18px; margin: 18px 0; font-size: 13px; color: #8A5700;">
                <strong>Seguridad:</strong> Una vez que inicies sesión con esta clave temporal, el sistema te solicitará definir una nueva contraseña personalizada.
            </div>
            """;

        var htmlBody = BuildStyledHtmlLayout(
            badgeText: "Seguridad & Acceso",
            headline: "Recuperación de Contraseña",
            recipientName: destinationName,
            leadMessage: "Has solicitado una clave temporal para ingresar al sistema de Óptica Lux.",
            detailsHtml: detailsHtml,
            actionBoxHtml: actionBoxHtml,
            ctaText: "Ingresar al Sistema");

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
            Subject = "Recuperación de acceso - clave temporal · Óptica Lux",
            Body = htmlBody,
            IsBodyHtml = true
        };

        message.To.Add(BuildRecipientAddress(destinationEmail));
        await SendAsync(message, cancellationToken);
    }

    public async Task SendAppointmentCreatedAsync(
        string destinationEmail,
        string destinationName,
        string doctorName,
        DateOnly appointmentDate,
        TimeOnly startTime,
        TimeOnly? endTime = null,
        string appointmentType = "Presencial",
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("SMTP no configurado. Completa la sección Smtp en appsettings.json.");
        }

        var effectiveEndTime = endTime ?? startTime.AddMinutes(30);
        var formattedDate = appointmentDate.ToString("dddd, dd 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES"));
        formattedDate = char.ToUpper(formattedDate[0]) + formattedDate[1..];

        var detailsHtml = $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse: collapse; background: #FAF7F2; border: 1px solid #EAE2D5; border-radius: 12px; overflow: hidden; margin: 20px 0;">
                <tr>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600; width: 35%;">📅 Fecha</td>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{formattedDate}</td>
                </tr>
                <tr>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">⏰ Horario</td>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{startTime:HH\:mm} - {effectiveEndTime:HH\:mm}</td>
                </tr>
                <tr>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">👨‍⚕️ Profesional</td>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{doctorName}</td>
                </tr>
                <tr>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">🏢 Modalidad</td>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 600;">{appointmentType}</td>
                </tr>
                {(string.IsNullOrWhiteSpace(reason) ? "" : $"""
                <tr>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">📋 Motivo</td>
                    <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18;">{reason}</td>
                </tr>
                """)}
                <tr>
                    <td style="padding: 12px 18px; font-size: 13px; color: #7F6951; font-weight: 600;">📌 Estado</td>
                    <td style="padding: 12px 18px;">
                        <span style="background: #FFF5E6; color: #A86900; border: 1px solid rgba(254, 188, 100, 0.6); padding: 4px 12px; border-radius: 999px; font-weight: 700; font-size: 12px; text-transform: uppercase;">Programada</span>
                    </td>
                </tr>
            </table>
            """;

        var actionBoxHtml = """
            <div style="background: #F4FAF7; border-left: 4px solid #5DA181; border-radius: 6px; padding: 16px 18px; margin: 20px 0; font-size: 13px; line-height: 1.5; color: #20503B;">
                <strong>💡 Confirmación fácil:</strong> Apenas abras la aplicación podrás <strong>confirmar tu cita en 1 clic</strong>. Si no puedes asistir, también puedes <strong>reagendar o cancelar</strong> para liberar el cupo.
            </div>
            """;

        var htmlBody = BuildStyledHtmlLayout(
            badgeText: "Reserva de Cita",
            headline: "¡Tu Cita ha sido Agendada!",
            recipientName: destinationName,
            leadMessage: "Tu cita optométrica ha sido registrada exitosamente en Óptica Lux. A continuación te compartimos los detalles de tu consulta:",
            detailsHtml: detailsHtml,
            actionBoxHtml: actionBoxHtml,
            ctaText: "Ver y Confirmar mi Cita");

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
            Subject = $"Confirmación de reserva de cita · Óptica Lux ({appointmentDate:dd/MM/yyyy})",
            Body = htmlBody,
            IsBodyHtml = true
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
            throw new InvalidOperationException("SMTP no configurado. Completa la sección Smtp en appsettings.json.");
        }

        var effectiveSubject = string.IsNullOrWhiteSpace(customSubject)
            ? $"Recordatorio de cita optométrica ({reminderWindow}) · Óptica Lux"
            : customSubject;

        string htmlBody;

        if (!string.IsNullOrWhiteSpace(customBody) && customBody.Trim().StartsWith("<"))
        {
            // Custom HTML body provided
            htmlBody = customBody;
        }
        else
        {
            var formattedDate = appointmentDate.ToString("dddd, dd 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES"));
            formattedDate = char.ToUpper(formattedDate[0]) + formattedDate[1..];
            var isConfirmed = string.Equals(statusLabel, "Confirmada", StringComparison.OrdinalIgnoreCase);

            var statusBadgeStyle = isConfirmed
                ? "background: #EEF6F2; color: #276144; border: 1px solid rgba(93, 161, 129, 0.5);"
                : "background: #FFF5E6; color: #A86900; border: 1px solid rgba(254, 188, 100, 0.6);";

            var detailsHtml = $"""
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse: collapse; background: #FAF7F2; border: 1px solid #EAE2D5; border-radius: 12px; overflow: hidden; margin: 20px 0;">
                    <tr>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600; width: 35%;">📅 Fecha</td>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{formattedDate}</td>
                    </tr>
                    <tr>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">⏰ Hora</td>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{startTime:HH\:mm}</td>
                    </tr>
                    <tr>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">👨‍⚕️ Profesional</td>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 700;">{doctorName}</td>
                    </tr>
                    <tr>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 13px; color: #7F6951; font-weight: 600;">🏢 Modalidad</td>
                        <td style="padding: 12px 18px; border-bottom: 1px solid #EAE2D5; font-size: 14px; color: #241D18; font-weight: 600;">{appointmentType}</td>
                    </tr>
                    <tr>
                        <td style="padding: 12px 18px; font-size: 13px; color: #7F6951; font-weight: 600;">📌 Estado</td>
                        <td style="padding: 12px 18px;">
                            <span style="{statusBadgeStyle} padding: 4px 12px; border-radius: 999px; font-weight: 700; font-size: 12px; text-transform: uppercase;">{statusLabel}</span>
                        </td>
                    </tr>
                </table>
                """;

            var actionBoxHtml = $"""
                <div style="background: #FEF8ED; border-left: 4px solid #FEBC64; border-radius: 6px; padding: 16px 18px; margin: 20px 0; font-size: 13px; line-height: 1.5; color: #8A5700;">
                    <strong>⏰ Recordatorio ({reminderWindow}):</strong> Por favor ingresa a la aplicación para <strong>confirmar tu asistencia</strong>, o en caso de necesitarlo, <strong>reagendar o cancelar</strong> con anticipación para liberar tu turno.
                </div>
                """;

            var leadMsg = string.IsNullOrWhiteSpace(customBody)
                ? $"Te recordamos que tienes una cita de optometría programada <strong>{reminderWindow}</strong> con el profesional <strong>{doctorName}</strong>."
                : customBody.Replace("\n", "<br/>");

            htmlBody = BuildStyledHtmlLayout(
                badgeText: $"Recordatorio · {reminderWindow}",
                headline: "Recordatorio de Cita Optométrica",
                recipientName: destinationName,
                leadMessage: leadMsg,
                detailsHtml: detailsHtml,
                actionBoxHtml: actionBoxHtml,
                ctaText: "Confirmar o Gestionar Cita");
        }

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
            Subject = effectiveSubject,
            Body = htmlBody,
            IsBodyHtml = true
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
            throw new InvalidOperationException("SMTP no configurado. Completa la sección Smtp en appsettings.json.");
        }

        var htmlBody = BuildStyledHtmlLayout(
            badgeText: "Estado de Cuenta",
            headline: "Comprobante / Estado de Cuenta",
            recipientName: destinationName,
            leadMessage: body.Replace("\n", "<br/>"),
            detailsHtml: "",
            actionBoxHtml: null,
            ctaText: null);

        using var message = new MailMessage
        {
            From = BuildFromAddress(),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        message.To.Add(BuildRecipientAddress(destinationEmail));
        message.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), attachmentFileName, MediaTypeNames.Application.Pdf));
        await SendAsync(message, cancellationToken);
    }

    private static string BuildStyledHtmlLayout(
        string badgeText,
        string headline,
        string recipientName,
        string leadMessage,
        string detailsHtml,
        string? actionBoxHtml = null,
        string? ctaText = null)
    {
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Estimado/a paciente" : $"Hola {recipientName}";

        return $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{headline}</title>
            </head>
            <body style="margin: 0; padding: 0; background-color: #F8F6F0; font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased; color: #2D3748;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color: #F8F6F0; padding: 30px 15px;">
                    <tr>
                        <td align="center">
                            <!-- Main Container Card -->
                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width: 580px; background-color: #FFFFFF; border-radius: 18px; overflow: hidden; box-shadow: 0 10px 30px rgba(127, 105, 81, 0.08); border: 1px solid #E8E0D2;">
                                
                                <!-- Brand Header -->
                                <tr>
                                    <td style="background: linear-gradient(135deg, #276144 0%, #1A4430 100%); padding: 32px 24px; text-align: center;">
                                        <div style="display: inline-block;">
                                            <span style="font-size: 26px; font-weight: 800; letter-spacing: 0.5px; color: #FFFFFF; font-family: 'Segoe UI', sans-serif;">Óptica <span style="color: #FEBC64;">Lux</span></span>
                                            <span style="display: block; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 2px; color: #D1E5DB; margin-top: 4px;">Salud Visual & Especialistas</span>
                                        </div>
                                    </td>
                                </tr>

                                <!-- Badge Ribbon -->
                                <tr>
                                    <td style="background-color: #EEF6F2; border-bottom: 1px solid #D9EBE1; padding: 12px 24px; text-align: center;">
                                        <span style="display: inline-block; background-color: #276144; color: #FFFFFF; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 1px; padding: 4px 14px; border-radius: 999px;">
                                            {badgeText}
                                        </span>
                                    </td>
                                </tr>

                                <!-- Content Body -->
                                <tr>
                                    <td style="padding: 32px 28px 24px;">
                                        <h1 style="font-size: 20px; font-weight: 800; color: #241D18; margin: 0 0 16px 0; line-height: 1.3;">
                                            {headline}
                                        </h1>

                                        <p style="font-size: 15px; line-height: 1.6; color: #4A5568; margin: 0 0 16px 0;">
                                            <strong style="color: #241D18;">{greeting},</strong>
                                        </p>

                                        <p style="font-size: 14px; line-height: 1.6; color: #4A5568; margin: 0 0 16px 0;">
                                            {leadMessage}
                                        </p>

                                        {detailsHtml}

                                        {(string.IsNullOrWhiteSpace(actionBoxHtml) ? "" : actionBoxHtml)}

                                        {(string.IsNullOrWhiteSpace(ctaText) ? "" : $"""
                                        <div style="text-align: center; margin: 28px 0 12px;">
                                            <span style="display: inline-block; background: #276144; color: #FFFFFF; font-size: 14px; font-weight: 700; padding: 12px 28px; border-radius: 10px; text-decoration: none; box-shadow: 0 4px 14px rgba(39, 97, 68, 0.25);">
                                                {ctaText}
                                            </span>
                                        </div>
                                        """)}
                                    </td>
                                </tr>

                                <!-- Footer -->
                                <tr>
                                    <td style="background-color: #FAF7F2; border-top: 1px solid #EAE2D5; padding: 22px 24px; text-align: center; font-size: 12px; color: #7F6951; line-height: 1.5;">
                                        <strong style="color: #241D18;">Óptica Lux</strong> · Cuidamos tu salud visual<br/>
                                        Este es un correo automático de notificación generado por el sistema.
                                    </td>
                                </tr>

                            </table>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;
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

