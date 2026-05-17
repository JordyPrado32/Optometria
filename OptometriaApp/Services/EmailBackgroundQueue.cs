using System.Threading.Channels;
using Microsoft.Extensions.Options;
using OptometriaApp.Configuration;

namespace OptometriaApp.Services;

public interface IEmailBackgroundQueue
{
    ValueTask QueueTemporaryPasswordEmailAsync(string destinationEmail, string destinationName, string temporaryPassword, int minutesValid, CancellationToken cancellationToken = default);
}

public sealed class EmailBackgroundQueue : BackgroundService, IEmailBackgroundQueue
{
    private readonly Channel<TemporaryPasswordEmailWorkItem> channel = Channel.CreateUnbounded<TemporaryPasswordEmailWorkItem>();
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<EmailBackgroundQueue> logger;
    private readonly SmtpSettings smtpSettings;

    public EmailBackgroundQueue(IServiceScopeFactory scopeFactory, IOptions<SmtpSettings> smtpOptions, ILogger<EmailBackgroundQueue> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        smtpSettings = smtpOptions.Value;
    }

    public ValueTask QueueTemporaryPasswordEmailAsync(string destinationEmail, string destinationName, string temporaryPassword, int minutesValid, CancellationToken cancellationToken = default)
    {
        return channel.Writer.WriteAsync(
            new TemporaryPasswordEmailWorkItem(destinationEmail, destinationName, temporaryPassword, minutesValid),
            cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<EmailSender>();

                if (!sender.IsConfigured())
                {
                    logger.LogWarning("SMTP no configurado. Se omitio el envio del correo temporal a {Email}.", workItem.DestinationEmail);
                    continue;
                }

                await sender.SendTemporaryPasswordAsync(
                    workItem.DestinationEmail,
                    workItem.DestinationName,
                    workItem.TemporaryPassword,
                    workItem.MinutesValid,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error enviando correo temporal a {Email}.", workItem.DestinationEmail);
            }
        }
    }

    private sealed record TemporaryPasswordEmailWorkItem(
        string DestinationEmail,
        string DestinationName,
        string TemporaryPassword,
        int MinutesValid);
}
