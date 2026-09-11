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

Te recordamos que tienes una cita de optometría programada {{reminder_window}} con el profesional {{doctor_name}}.

Fecha: {{appointment_date}}
Hora: {{appointment_time}}
Modalidad: {{appointment_type}}
Estado actual: {{status_label}}

Por favor ingresa a la aplicación para confirmar tu asistencia o, en caso de algún inconveniente, reagendar o cancelar la cita con anticipación para liberar tu turno.
""";

    public static async Task<tbl_configuracion_optica> GetOrCreateSettingsAsync(OpticaDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.tbl_configuracion_opticas.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            if (settings.porcentaje_impuesto is null or <= 0m)
            {
                settings.porcentaje_impuesto = 15.00m;
            }
            if (string.IsNullOrWhiteSpace(settings.opciones_impuesto_csv))
            {
                settings.opciones_impuesto_csv = "0, 5, 15";
            }
            if (settings.horas_liberacion_cita_no_confirmada is null or <= 0)
            {
                settings.horas_liberacion_cita_no_confirmada = 2;
            }
            return settings;
        }

        settings = new tbl_configuracion_optica
        {
            nombre_comercial = "Optica Lux",
            prefijo_pais = "593",
            porcentaje_impuesto = 15.00m,
            opciones_impuesto_csv = "0, 5, 15",
            horas_liberacion_cita_no_confirmada = 2
        };

        dbContext.tbl_configuracion_opticas.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<decimal> GetActiveVatPercentageAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return (settings.porcentaje_impuesto ?? 0m) > 0m ? settings.porcentaje_impuesto!.Value : 15.00m;
    }

    public async Task<decimal[]> GetAvailableVatRatesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return ParseVatRates(settings.opciones_impuesto_csv, settings.porcentaje_impuesto);
    }

    public static decimal[] ParseVatRates(string? rawCsv, decimal? defaultVatRate)
    {
        var activeRate = (defaultVatRate ?? 0m) > 0m ? defaultVatRate!.Value : 15.00m;
        if (string.IsNullOrWhiteSpace(rawCsv))
        {
            return [0m, 5m, activeRate];
        }

        var rates = rawCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : (decimal?)null)
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .Distinct()
            .OrderBy(r => r)
            .ToArray();

        if (rates.Length == 0)
        {
            return [0m, 5m, activeRate];
        }

        if (!rates.Contains(activeRate))
        {
            rates = rates.Append(activeRate).OrderBy(r => r).ToArray();
        }

        return rates;
    }

    public async Task<int> GetAppointmentReleaseHoursAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return (settings.horas_liberacion_cita_no_confirmada ?? 0) > 0 ? settings.horas_liberacion_cita_no_confirmada!.Value : 2;
    }

    public async Task<(string ReceptionPhone, string LabPhone)> GetAssistedContactNumbersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var reception = !string.IsNullOrWhiteSpace(settings.telefono_recepcion_asistida)
            ? settings.telefono_recepcion_asistida
            : settings.telefono ?? string.Empty;
        var lab = !string.IsNullOrWhiteSpace(settings.telefono_laboratorio_asistido)
            ? settings.telefono_laboratorio_asistido
            : reception;
        return (reception, lab);
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
