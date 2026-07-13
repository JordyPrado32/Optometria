using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class OpticaCustomizationService
{
    public const string LabWhatsappTemplateType = "EnvioLaboratorio";
    public const string PatientReminderEmailTemplateType = "RecordatorioPaciente";
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public OpticaCustomizationService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<tbl_configuracion_optica> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetOrCreateSettingsAsync(dbContext, cancellationToken);
    }

    public async Task<string> GetTemplateContentAsync(string canal, string tipo, string fallbackContent, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var template = await GetOrCreateTemplateAsync(dbContext, canal, tipo, fallbackContent, cancellationToken);
        return string.IsNullOrWhiteSpace(template.contenido) ? fallbackContent : template.contenido!;
    }

    public static string RenderTemplate(string template, IReadOnlyDictionary<string, string> variables)
    {
        var rendered = template;
        foreach (var variable in variables)
        {
            rendered = rendered.Replace($"{{{{{variable.Key}}}}}", variable.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return rendered;
    }

    public static string NormalizePhone(string? rawValue, string? countryPrefix)
    {
        var digits = new string((rawValue ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return string.Empty;
        }

        var prefixDigits = new string((countryPrefix ?? string.Empty).Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(prefixDigits) && !digits.StartsWith(prefixDigits, StringComparison.Ordinal))
        {
            digits = $"{prefixDigits}{digits}";
        }

        return digits;
    }

    public static string DefaultLabWhatsappTemplate =>
        "Hola, compartimos la orden RX {{order_number}} del paciente {{patient_name}}. Tipo: {{rx_type}}. Laboratorio: {{laboratory_name}}. {{observations}} Se adjunta o comparte la receta desde el sistema.";

    public static string DefaultPatientReminderEmailTemplate =>
        """
Hola {{patient_name}},

Te recordamos que tienes una cita {{reminder_window}} con el profesional {{doctor_name}}.

Fecha: {{appointment_date}}
Hora: {{appointment_time}}
Tipo: {{appointment_type}}
Estado actual: {{status_label}}

Si necesitas reprogramarla o cancelarla, ingresa al sistema cuanto antes.
""";

    public static async Task<tbl_configuracion_optica> GetOrCreateSettingsAsync(OpticaDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.tbl_configuracion_opticas.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new tbl_configuracion_optica
        {
            nombre_comercial = "Optica Lux",
            prefijo_pais = "593"
        };

        dbContext.tbl_configuracion_opticas.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public static async Task<tbl_plantilla_mensaje> GetOrCreateTemplateAsync(
        OpticaDbContext dbContext,
        string canal,
        string tipo,
        string fallbackContent,
        CancellationToken cancellationToken = default)
    {
        var template = await dbContext.tbl_plantilla_mensajes
            .FirstOrDefaultAsync(
                x => x.canal == canal && x.tipo == tipo,
                cancellationToken);

        if (template is not null)
        {
            if (string.IsNullOrWhiteSpace(template.contenido))
            {
                template.contenido = fallbackContent;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return template;
        }

        template = new tbl_plantilla_mensaje
        {
            nombre = $"{canal} {tipo}",
            canal = canal,
            tipo = tipo,
            contenido = fallbackContent
        };

        dbContext.tbl_plantilla_mensajes.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        return template;
    }
}
