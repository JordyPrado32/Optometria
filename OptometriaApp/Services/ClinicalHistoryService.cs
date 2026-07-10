using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class ClinicalHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClinicalHistoryLoadResult> LoadPatientHistoryAsync(
        OpticaDbContext dbContext,
        tbl_paciente patient,
        int? selectedEncounterId)
    {
        var history = await dbContext.tbl_historia_clinica_optometrias
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id_paciente == patient.id_paciente && x.activo == true);

        var events = history is null
            ? []
            : await dbContext.tbl_historia_clinica_optometria_eventos
                .AsNoTracking()
                .Where(x => x.id_historia_clinica == history.id_historia_clinica && x.activo)
                .OrderByDescending(x => x.fecha_evento)
                .ThenByDescending(x => x.id_historia_evento)
                .ToListAsync();

        var openingSnapshot = history is null
            ? BuildOpeningSnapshot(patient)
            : Deserialize<OpeningSnapshot>(history.datos_apertura_json) ?? BuildOpeningSnapshot(patient);

        var selectedEvent = selectedEncounterId.HasValue
            ? events.FirstOrDefault(x => x.id_historia_evento == selectedEncounterId.Value)
            : events.FirstOrDefault();

        var legacyEditor = BuildLegacyEditor(history, patient);
        var editor = selectedEvent is not null
            ? BuildEditorFromEvent(selectedEvent)
            : legacyEditor ?? BuildNewEditor(patient);

        return new ClinicalHistoryLoadResult
        {
            History = history,
            OpeningSnapshot = openingSnapshot,
            Encounters = events.Select(MapEncounterSummary).ToList(),
            SelectedEncounter = selectedEvent is null ? null : MapEncounterSummary(selectedEvent),
            Editor = editor,
            HasLegacyDataPendingMigration = selectedEvent is null && legacyEditor is not null,
            LegacyEncounterLabel = legacyEditor is null || history is null
                ? null
                : $"Registro previo del {history.fecha_ultima_actualizacion?.ToString("yyyy-MM-dd HH:mm") ?? history.fecha_apertura?.ToString("yyyy-MM-dd HH:mm") ?? "sin fecha"}"
        };
    }

    public async Task<ClinicalHistorySaveResult> SaveAsync(
        OpticaDbContext dbContext,
        ClinicalHistorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ApplyStructuredAnamnesisDefaults(request.Editor);

        var validationErrors = ValidateForMode(request.Editor, request.Mode);
        if (validationErrors.Count > 0)
        {
            return new ClinicalHistorySaveResult
            {
                ValidationErrors = validationErrors
            };
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var history = await dbContext.tbl_historia_clinica_optometrias
            .FirstOrDefaultAsync(
                x => x.id_paciente == request.Patient.id_paciente && x.activo == true,
                cancellationToken);

        if (history is null)
        {
            history = new tbl_historia_clinica_optometria
            {
                id_paciente = request.Patient.id_paciente,
                id_optometra_apertura = request.ActorUserId,
                fecha_apertura = DateTime.Now,
                activo = true
            };

            dbContext.tbl_historia_clinica_optometrias.Add(history);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        history.id_optometra_ultima_actualizacion = request.ActorUserId;
        history.fecha_ultima_actualizacion = DateTime.Now;
        history.numero_historia = NormalizeOptional(request.Editor.NumeroHistoria);
        history.consultorio = NormalizeOptional(request.Editor.Consultorio);
        history.llave_clinica = NormalizeOptional(request.Editor.LlaveClinica);
        history.lugar_nacimiento = NormalizeOptional(request.Editor.LugarNacimiento);
        history.procedencia = NormalizeOptional(request.Editor.Procedencia);
        history.ultimo_control = NormalizeOptional(request.Editor.UltimoControl);
        history.datos_apertura_json = Serialize(request.OpeningSnapshot);
        history.motivo_consulta = NormalizeOptional(request.Editor.MotivoConsulta);
        history.anamnesis = NormalizeOptional(request.Editor.Anamnesis);
        history.antecedentes_json = Serialize(request.Editor.Antecedentes);
        history.usa_lentes = request.Editor.UsaLentes;
        history.lentes_json = Serialize(request.Editor.Lentes);
        history.agudeza_visual_json = Serialize(request.Editor.Visual);
        history.biomicroscopia_json = Serialize(request.Editor.Biomicroscopia);
        history.oftalmoscopia_json = Serialize(request.Editor.Oftalmoscopia);
        history.examen_motor_json = Serialize(request.Editor.Motor);
        history.queratometria_json = Serialize(request.Editor.Keratometria);
        history.refraccion_json = Serialize(request.Editor.Refraction);
        history.diagnostico_json = Serialize(request.Editor.Diagnostico);
        history.observaciones_generales = NormalizeOptional(request.Editor.ObservacionesGenerales);
        history.nombre_examinador = NormalizeOptional(request.Editor.NombreExaminador);
        history.nivel_paralelo_jornada = NormalizeOptional(request.Editor.NivelParaleloJornada);
        history.consentimiento_json = Serialize(request.Editor.Consentimiento);

        var consultation = request.SelectedEncounterId.HasValue
            ? await TryLoadConsultationForEncounterAsync(dbContext, request.SelectedEncounterId.Value, cancellationToken)
            : null;

        consultation ??= new tbl_consulta
        {
            id_paciente = request.Patient.id_paciente,
            id_optometra = request.ActorUserId
        };

        if (consultation.id_consulta == 0)
        {
            dbContext.tbl_consulta.Add(consultation);
        }

        consultation.fecha_consulta = DateTime.Now;
        consultation.motivo_consulta = NormalizeOptional(request.Editor.MotivoConsulta);
        consultation.historia_clinica = NormalizeOptional(request.Editor.NumeroHistoria);
        consultation.antecedentes_personales = NormalizeOptional(request.Editor.Antecedentes.PersonalesGenerales);
        consultation.antecedentes_familiares = NormalizeOptional(request.Editor.Antecedentes.FamiliaresGenerales);
        consultation.antecedentes_oculares = NormalizeOptional(string.Join(" | ", new[]
        {
            request.Editor.Antecedentes.PersonalesOculares,
            request.Editor.Antecedentes.FamiliaresOculares
        }.Where(value => !string.IsNullOrWhiteSpace(value))));
        consultation.enfermedades_previas = NormalizeOptional(request.Editor.Anamnesis);
        consultation.usa_lentes = request.Editor.UsaLentes;
        consultation.detalle_usa_lentes = NormalizeOptional(request.Editor.Lentes.Observaciones);
        consultation.examenes_preliminares = NormalizeOptional(BuildPreliminaryExamSummary(request.Editor));
        consultation.evaluaciones = NormalizeOptional(request.Editor.Diagnostico.TratamientoConducta);
        consultation.examenes_varios = NormalizeOptional(BuildPrescriptionSummary(request.Editor.Diagnostico, "Examen"));
        consultation.medicamentos = NormalizeOptional(BuildPrescriptionSummary(request.Editor.Diagnostico, "Medicamento"));
        consultation.notas = NormalizeOptional(request.Editor.ObservacionesGenerales);

        await dbContext.SaveChangesAsync(cancellationToken);

        await SyncPrescriptionAsync(dbContext, consultation, request, cancellationToken);

        var encounter = request.SelectedEncounterId.HasValue
            ? await dbContext.tbl_historia_clinica_optometria_eventos
                .FirstOrDefaultAsync(x => x.id_historia_evento == request.SelectedEncounterId.Value, cancellationToken)
            : null;

        if (encounter is null)
        {
            encounter = new tbl_historia_clinica_optometria_evento
            {
                id_historia_clinica = history.id_historia_clinica,
                id_paciente = request.Patient.id_paciente,
                id_consulta = consultation.id_consulta,
                id_optometra = request.ActorUserId,
                fecha_evento = DateTime.Now,
                activo = true
            };

            dbContext.tbl_historia_clinica_optometria_eventos.Add(encounter);
        }

        encounter.fecha_ultima_actualizacion = DateTime.Now;
        encounter.estado = request.Mode == ClinicalHistorySaveMode.Finalize ? "Cerrada" : "Borrador";
        encounter.resumen_progreso = GetCompletionPercent(request.Editor);
        encounter.motivo_consulta = NormalizeOptional(request.Editor.MotivoConsulta);
        encounter.anamnesis = NormalizeOptional(request.Editor.Anamnesis);
        encounter.diagnostico_resumen = NormalizeOptional(BuildDiagnosisSummary(request.Editor));
        encounter.cie10 = NormalizeOptional(request.Editor.Diagnostico.Cie10);
        encounter.payload_json = Serialize(request.Editor);
        encounter.consentimiento_firmado = request.Editor.Consentimiento.Autorizado &&
            (!string.IsNullOrWhiteSpace(request.Editor.Consentimiento.FirmaReferencia) || !string.IsNullOrWhiteSpace(request.Editor.Consentimiento.FirmaDibujada));
        encounter.es_legado_migrado = request.HasLegacyDataPendingMigration;

        await BillingDraftServiceEnsureAsync(dbContext, consultation.id_consulta, request.ActorUserId, request.BillingDraftService);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClinicalHistorySaveResult
        {
            EncounterId = encounter.id_historia_evento,
            ConsultationId = consultation.id_consulta,
            StatusMessage = request.Mode == ClinicalHistorySaveMode.Finalize
                ? "La evolucion clinica se cerro correctamente."
                : "El borrador de la evolucion clinica se guardo correctamente."
        };
    }

    public static List<string> ValidateStep(ClinicalHistoryEditorModel editor, int step)
    {
        var errors = new List<string>();

        switch (step)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(editor.NumeroHistoria))
                {
                    errors.Add("Define el numero de historia clinica antes de continuar.");
                }
                 break;
            case 2:
                if (string.IsNullOrWhiteSpace(editor.MotivoConsulta))
                {
                    errors.Add("Ingresa el motivo de consulta.");
                }

                errors.AddRange(ValidateStructuredAnamnesis(editor));
                break;
            case 3:
                if (!HasAnyTechnicalData(editor))
                {
                    errors.Add("Registra al menos un hallazgo tecnico, visual o de refraccion.");
                }
                break;
            case 4:
                if (!HasAnyDiagnosis(editor))
                {
                    errors.Add("Completa diagnostico o conducta clinica.");
                }
                break;
            case 5:
                errors.AddRange(ValidateForMode(editor, ClinicalHistorySaveMode.Finalize));
                break;
        }

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ValidateForMode(ClinicalHistoryEditorModel editor, ClinicalHistorySaveMode mode)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(editor.NumeroHistoria))
        {
            errors.Add("Define el numero de historia clinica.");
        }

        if (mode == ClinicalHistorySaveMode.Draft)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(editor.MotivoConsulta))
        {
            errors.Add("Ingresa el motivo de consulta.");
        }

        if (!HasAnyAnamnesis(editor))
        {
            errors.Add("Completa la anamnesis.");
        }

        errors.AddRange(ValidateStructuredAnamnesis(editor));

        if (!HasAnyAntecedent(editor))
        {
            errors.Add("Registra al menos un antecedente relevante.");
        }

        if (!HasAnyTechnicalData(editor))
        {
            errors.Add("Completa hallazgos tecnicos o visuales.");
        }

        if (!HasAnyDiagnosis(editor))
        {
            errors.Add("Completa diagnostico y conducta.");
        }

        foreach (var item in editor.Diagnostico.PrescripcionItems)
        {
            if (item.Cantidad <= 0 || string.IsNullOrWhiteSpace(item.NombreItem))
            {
                errors.Add("Cada item de la receta debe tener nombre y cantidad valida.");
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(editor.NombreExaminador))
        {
            errors.Add("Ingresa el nombre del examinador.");
        }

        if (!editor.Consentimiento.Autorizado)
        {
            errors.Add("Debes registrar la autorizacion del consentimiento.");
        }

        if (string.IsNullOrWhiteSpace(editor.Consentimiento.FirmaReferencia) &&
            string.IsNullOrWhiteSpace(editor.Consentimiento.FirmaDibujada))
        {
            errors.Add("Debes registrar la firma o una referencia valida.");
        }

        return errors;
    }

    public static int GetCompletionPercent(ClinicalHistoryEditorModel editor)
    {
        var checks = new[]
        {
            !string.IsNullOrWhiteSpace(editor.NumeroHistoria),
            !string.IsNullOrWhiteSpace(editor.MotivoConsulta),
            HasAnyAnamnesis(editor),
            HasAnyAntecedent(editor),
            HasAnyTechnicalData(editor),
            HasAnyDiagnosis(editor),
            !string.IsNullOrWhiteSpace(editor.NombreExaminador),
            editor.Consentimiento.Autorizado,
            !string.IsNullOrWhiteSpace(editor.Consentimiento.FirmaReferencia) || !string.IsNullOrWhiteSpace(editor.Consentimiento.FirmaDibujada)
        };

        var completed = checks.Count(x => x);
        return (int)Math.Round((completed * 100m) / checks.Length, MidpointRounding.AwayFromZero);
    }

    public static bool HasBillingTriggers(ClinicalHistoryEditorModel editor)
        => editor.Diagnostico.PrescripcionItems.Any(x => x.EnviarAFacturacion && (x.ProductoId > 0 || !string.IsNullOrWhiteSpace(x.NombreItem))) ||
           !string.IsNullOrWhiteSpace(editor.Diagnostico.ExamenesIndicados) ||
           !string.IsNullOrWhiteSpace(editor.Diagnostico.MedicamentosRecetados);

    public static bool HasAnyAnamnesis(ClinicalHistoryEditorModel editor)
        => !string.IsNullOrWhiteSpace(editor.Anamnesis) || HasStructuredAnamnesis(editor.AnamnesisGuiada);

    public static bool HasStructuredAnamnesis(AnamnesisGuidedSection section)
        => !string.IsNullOrWhiteSpace(section.MotivoPrincipal) ||
           !string.IsNullOrWhiteSpace(section.Inicio) ||
           !string.IsNullOrWhiteSpace(section.DuracionValor) ||
           !string.IsNullOrWhiteSpace(section.Lateralidad) ||
           !string.IsNullOrWhiteSpace(section.Intensidad) ||
           !string.IsNullOrWhiteSpace(section.Desencadenantes) ||
           !string.IsNullOrWhiteSpace(section.Aliviantes) ||
           !string.IsNullOrWhiteSpace(section.NotasAdicionales) ||
           section.Sintomas.Count > 0 ||
           section.BanderasAlerta.Count > 0;

    public static string BuildStructuredAnamnesisSummary(AnamnesisGuidedSection section)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(section.MotivoPrincipal))
        {
            parts.Add($"Motivo principal: {section.MotivoPrincipal.Trim()}");
        }

        var temporal = string.Join(", ", new[]
        {
            string.IsNullOrWhiteSpace(section.Inicio) ? null : $"inicio {section.Inicio.Trim().ToLowerInvariant()}",
            string.IsNullOrWhiteSpace(section.DuracionValor) ? null : $"duracion {section.DuracionValor.Trim()} {section.DuracionUnidad.Trim().ToLowerInvariant()}",
            string.IsNullOrWhiteSpace(section.Lateralidad) ? null : $"lateralidad {section.Lateralidad.Trim().ToLowerInvariant()}"
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (!string.IsNullOrWhiteSpace(temporal))
        {
            parts.Add(temporal);
        }

        if (section.Sintomas.Count > 0)
        {
            parts.Add($"Sintomas: {string.Join(", ", section.Sintomas)}");
        }

        if (!string.IsNullOrWhiteSpace(section.Intensidad))
        {
            parts.Add($"Intensidad: {section.Intensidad.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(section.Desencadenantes))
        {
            parts.Add($"Desencadenantes: {section.Desencadenantes.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(section.Aliviantes))
        {
            parts.Add($"Aliviantes: {section.Aliviantes.Trim()}");
        }

        if (section.BanderasAlerta.Count > 0)
        {
            parts.Add($"Alertas: {string.Join(", ", section.BanderasAlerta)}");
        }

        if (!string.IsNullOrWhiteSpace(section.NotasAdicionales))
        {
            parts.Add($"Notas: {section.NotasAdicionales.Trim()}");
        }

        return string.Join(". ", parts);
    }

    public static List<string> ValidateStructuredAnamnesis(ClinicalHistoryEditorModel editor)
    {
        var errors = new List<string>();
        var section = editor.AnamnesisGuiada;

        if (string.IsNullOrWhiteSpace(editor.MotivoConsulta) || editor.MotivoConsulta.Trim().Length < 8)
        {
            errors.Add("El motivo de consulta debe ser especifico y tener al menos 8 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(section.MotivoPrincipal))
        {
            errors.Add("Selecciona un motivo principal en la anamnesis guiada.");
        }

        if (string.IsNullOrWhiteSpace(section.Inicio))
        {
            errors.Add("Selecciona como inicio el cuadro actual.");
        }

        if (string.IsNullOrWhiteSpace(section.Lateralidad))
        {
            errors.Add("Selecciona la lateralidad del motivo principal.");
        }

        if (string.IsNullOrWhiteSpace(section.Intensidad))
        {
            errors.Add("Selecciona la intensidad percibida del cuadro.");
        }

        if (section.Sintomas.Count == 0)
        {
            errors.Add("Selecciona al menos un sintoma frecuente.");
        }

        if (!int.TryParse(section.DuracionValor, out var duration) || duration <= 0 || duration > 3650)
        {
            errors.Add("La duracion debe ser un numero real entre 1 y 3650.");
        }

        if (section.MotivoPrincipal.Equals("Otro (especificar)", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(section.NotasAdicionales))
        {
            errors.Add("Si eliges otro motivo principal, debes especificarlo en notas adicionales.");
        }

        if (ContainsAmbiguousText(editor.MotivoConsulta))
        {
            errors.Add("Evita motivos ambiguos como 'molestia' o 'revision'. Describe el problema principal.");
        }

        if (!string.IsNullOrWhiteSpace(editor.Anamnesis) && editor.Anamnesis.Trim().Length < 12 && !HasStructuredAnamnesis(section))
        {
            errors.Add("La anamnesis libre es demasiado corta. Completa el formato guiado o amplia la descripcion.");
        }

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string GetEncounterStatusClass(string status)
        => status.Equals("Cerrada", StringComparison.OrdinalIgnoreCase)
            ? "clinical-badge clinical-badge--success"
            : "clinical-badge clinical-badge--warning";

    public static ClinicalHistoryEditorModel BuildNewEditor(tbl_paciente patient)
    {
        return new ClinicalHistoryEditorModel
        {
            NumeroHistoria = string.IsNullOrWhiteSpace(patient.codigo_paciente) ? $"HC-{patient.id_paciente:D5}" : patient.codigo_paciente!,
            AnamnesisGuiada = new AnamnesisGuidedSection(),
            Lentes = new LensSection(),
            Visual = new VisualSection(),
            Antecedentes = new AntecedentsSection(),
            Biomicroscopia = new BiomicroscopiaSection(),
            Oftalmoscopia = new OftalmoscopiaSection(),
            Motor = new MotorExamSection(),
            Keratometria = new KeratometrySection(),
            Refraction = new RefractionSection(),
            Diagnostico = new DiagnosisSection(),
            Consentimiento = new ConsentSection
            {
                Nombre = $"{patient.nombres} {patient.apellidos}".Trim(),
                Cedula = patient.cedula,
                Texto = "Permito que se me realicen pruebas no invasivas para la evaluacion visual y el uso clinico anonimizado de los resultados."
            }
        };
    }

    public static OpeningSnapshot BuildOpeningSnapshot(tbl_paciente patient)
    {
        return new OpeningSnapshot
        {
            Apellidos = patient.apellidos,
            Nombres = patient.nombres,
            Cedula = patient.cedula,
            FechaNacimiento = patient.fecha_nacimiento?.ToString("yyyy-MM-dd") ?? string.Empty,
            Edad = patient.edad?.ToString() ?? string.Empty,
            Genero = patient.genero ?? string.Empty,
            Ocupacion = patient.ocupacion ?? string.Empty,
            Email = patient.email ?? string.Empty,
            Direccion = patient.direccion ?? string.Empty,
            Telefono = patient.telefono ?? string.Empty
        };
    }

    private static ClinicalHistoryEditorModel? BuildLegacyEditor(tbl_historia_clinica_optometria? history, tbl_paciente patient)
    {
        if (history is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(history.motivo_consulta) &&
            string.IsNullOrWhiteSpace(history.anamnesis) &&
            string.IsNullOrWhiteSpace(history.antecedentes_json) &&
            string.IsNullOrWhiteSpace(history.biomicroscopia_json) &&
            string.IsNullOrWhiteSpace(history.diagnostico_json))
        {
            return null;
        }

        return new ClinicalHistoryEditorModel
        {
            NumeroHistoria = history.numero_historia ?? string.Empty,
            Consultorio = history.consultorio ?? string.Empty,
            LlaveClinica = history.llave_clinica ?? string.Empty,
            LugarNacimiento = history.lugar_nacimiento ?? string.Empty,
            Procedencia = history.procedencia ?? string.Empty,
            UltimoControl = history.ultimo_control ?? string.Empty,
            MotivoConsulta = history.motivo_consulta ?? string.Empty,
            Anamnesis = history.anamnesis ?? string.Empty,
            UsaLentes = history.usa_lentes ?? false,
            ObservacionesGenerales = history.observaciones_generales ?? string.Empty,
            NombreExaminador = history.nombre_examinador ?? string.Empty,
            NivelParaleloJornada = history.nivel_paralelo_jornada ?? string.Empty,
            AnamnesisGuiada = new AnamnesisGuidedSection(),
            Antecedentes = Deserialize<AntecedentsSection>(history.antecedentes_json) ?? new AntecedentsSection(),
            Lentes = Deserialize<LensSection>(history.lentes_json) ?? new LensSection(),
            Visual = Deserialize<VisualSection>(history.agudeza_visual_json) ?? new VisualSection(),
            Biomicroscopia = Deserialize<BiomicroscopiaSection>(history.biomicroscopia_json) ?? new BiomicroscopiaSection(),
            Oftalmoscopia = Deserialize<OftalmoscopiaSection>(history.oftalmoscopia_json) ?? new OftalmoscopiaSection(),
            Motor = Deserialize<MotorExamSection>(history.examen_motor_json) ?? new MotorExamSection(),
            Keratometria = Deserialize<KeratometrySection>(history.queratometria_json) ?? new KeratometrySection(),
            Refraction = Deserialize<RefractionSection>(history.refraccion_json) ?? new RefractionSection(),
            Diagnostico = Deserialize<DiagnosisSection>(history.diagnostico_json) ?? new DiagnosisSection(),
            Consentimiento = Deserialize<ConsentSection>(history.consentimiento_json) ?? new ConsentSection
            {
                Nombre = $"{patient.nombres} {patient.apellidos}".Trim(),
                Cedula = patient.cedula
            }
        };
    }

    private static ClinicalHistoryEditorModel BuildEditorFromEvent(tbl_historia_clinica_optometria_evento encounter)
        => Deserialize<ClinicalHistoryEditorModel>(encounter.payload_json) ?? new ClinicalHistoryEditorModel();

    private static ClinicalEncounterSummary MapEncounterSummary(tbl_historia_clinica_optometria_evento encounter)
    {
        return new ClinicalEncounterSummary
        {
            EncounterId = encounter.id_historia_evento,
            ConsultationId = encounter.id_consulta,
            Status = encounter.estado,
            Progress = encounter.resumen_progreso,
            Motive = encounter.motivo_consulta ?? "Sin motivo registrado",
            Diagnosis = encounter.diagnostico_resumen ?? "Sin diagnostico registrado",
            EventDate = encounter.fecha_evento,
            UpdatedAt = encounter.fecha_ultima_actualizacion
        };
    }

    private static async Task<tbl_consulta?> TryLoadConsultationForEncounterAsync(
        OpticaDbContext dbContext,
        int encounterId,
        CancellationToken cancellationToken)
    {
        var encounter = await dbContext.tbl_historia_clinica_optometria_eventos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id_historia_evento == encounterId, cancellationToken);

        if (encounter is null)
        {
            return null;
        }

        return await dbContext.tbl_consulta
            .FirstOrDefaultAsync(x => x.id_consulta == encounter.id_consulta, cancellationToken);
    }

    private static async Task BillingDraftServiceEnsureAsync(
        OpticaDbContext dbContext,
        int consultationId,
        int actorUserId,
        BillingDraftService billingDraftService)
    {
        await billingDraftService.EnsureConsultationBillingEntriesAsync(dbContext, consultationId, actorUserId);
    }

    private static string BuildPreliminaryExamSummary(ClinicalHistoryEditorModel editor)
        => string.Join(" | ", new[]
        {
            editor.Visual.OdVlSc,
            editor.Visual.OiVlSc,
            editor.Visual.AoVlSc,
            editor.Refraction.RxEstaticaDinamica
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildDiagnosisSummary(ClinicalHistoryEditorModel editor)
        => string.Join(" / ", new[]
        {
            editor.Diagnostico.DiagnosticoOd,
            editor.Diagnostico.DiagnosticoOi,
            editor.Diagnostico.DiagnosticoMotor
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildPrescriptionSummary(DiagnosisSection diagnosis, string itemType)
    {
        var structured = diagnosis.PrescripcionItems
            .Where(x => string.Equals(x.TipoItem, itemType, StringComparison.OrdinalIgnoreCase))
            .Select(BuildPrescriptionDisplay)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (structured.Count > 0)
        {
            return string.Join(" | ", structured);
        }

        return string.Equals(itemType, "Examen", StringComparison.OrdinalIgnoreCase)
            ? diagnosis.ExamenesIndicados
            : diagnosis.MedicamentosRecetados;
    }

    private static string BuildPrescriptionDisplay(PrescriptionLineItem item)
    {
        var name = string.IsNullOrWhiteSpace(item.NombreItem) ? "Item sin nombre" : item.NombreItem.Trim();
        var quantity = item.Cantidad <= 0 ? 1 : item.Cantidad;
        var unit = string.IsNullOrWhiteSpace(item.Unidad) ? string.Empty : $" {item.Unidad.Trim()}";
        var indications = string.IsNullOrWhiteSpace(item.Indicaciones) ? string.Empty : $" - {item.Indicaciones.Trim()}";
        return $"{name} x{quantity}{unit}{indications}".Trim();
    }

    private static bool HasAnyAntecedent(ClinicalHistoryEditorModel editor)
        => !string.IsNullOrWhiteSpace(editor.Antecedentes.PersonalesOculares) ||
           !string.IsNullOrWhiteSpace(editor.Antecedentes.PersonalesGenerales) ||
           !string.IsNullOrWhiteSpace(editor.Antecedentes.FamiliaresOculares) ||
           !string.IsNullOrWhiteSpace(editor.Antecedentes.FamiliaresGenerales);

    private static bool HasAnyTechnicalData(ClinicalHistoryEditorModel editor)
        => !string.IsNullOrWhiteSpace(editor.Visual.OdVlSc) ||
           !string.IsNullOrWhiteSpace(editor.Visual.OiVlSc) ||
           !string.IsNullOrWhiteSpace(editor.Refraction.RxEstaticaDinamica) ||
           !string.IsNullOrWhiteSpace(editor.Motor.Resumen) ||
           !string.IsNullOrWhiteSpace(editor.Biomicroscopia.OrbitaOd) ||
           !string.IsNullOrWhiteSpace(editor.Oftalmoscopia.PapilaOd);

    private static bool HasAnyDiagnosis(ClinicalHistoryEditorModel editor)
        => !string.IsNullOrWhiteSpace(editor.Diagnostico.DiagnosticoOd) ||
           !string.IsNullOrWhiteSpace(editor.Diagnostico.DiagnosticoOi) ||
           !string.IsNullOrWhiteSpace(editor.Diagnostico.TratamientoConducta);

    private static bool ContainsAmbiguousText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var ambiguousTerms = new[]
        {
            "molestia",
            "revision",
            "chequeo",
            "control",
            "malestar",
            "incomodo"
        };

        return ambiguousTerms.Any(term => normalized.Equals(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Serialize<T>(T model) where T : class
        => JsonSerializer.Serialize(model, JsonOptions);

    private static async Task SyncPrescriptionAsync(
        OpticaDbContext dbContext,
        tbl_consulta consultation,
        ClinicalHistorySaveRequest request,
        CancellationToken cancellationToken)
    {
        var items = request.Editor.Diagnostico.PrescripcionItems
            .Where(x => x.Cantidad > 0 && !string.IsNullOrWhiteSpace(x.NombreItem))
            .ToList();

        if (items.Count == 0 &&
            string.IsNullOrWhiteSpace(request.Editor.Diagnostico.MedicamentosRecetados) &&
            string.IsNullOrWhiteSpace(request.Editor.Diagnostico.ExamenesIndicados))
        {
            return;
        }

        var doctorProfileId = await dbContext.tbl_medico
            .Where(x => x.id_usuario == request.ActorUserId)
            .Select(x => (int?)x.id_medico)
            .FirstOrDefaultAsync(cancellationToken);

        if (!doctorProfileId.HasValue)
        {
            return;
        }

        var recipe = await dbContext.tbl_receta_medica
            .Include(x => x.tbl_receta_medica_detalle)
            .FirstOrDefaultAsync(x => x.id_consulta == consultation.id_consulta, cancellationToken);

        if (recipe is null)
        {
            recipe = new tbl_receta_medica
            {
                id_consulta = consultation.id_consulta,
                id_paciente = consultation.id_paciente,
                id_medico = doctorProfileId.Value,
                numero_receta = $"RXM-{consultation.id_consulta:D8}",
                fecha_emision = DateTime.Now,
                usuario_creacion = request.ActorUserId.ToString()
            };

            dbContext.tbl_receta_medica.Add(recipe);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        recipe.id_paciente = consultation.id_paciente;
        recipe.id_medico = doctorProfileId.Value;
        recipe.estado = request.Mode == ClinicalHistorySaveMode.Finalize ? "Activa" : "Borrador";
        recipe.diagnostico_resumen = NormalizeOptional(BuildDiagnosisSummary(request.Editor));
        recipe.observaciones = NormalizeOptional(request.Editor.Diagnostico.TratamientoConducta);
        recipe.fecha_actualizacion = DateTime.Now;

        if (!dbContext.Entry(recipe).Collection(x => x.tbl_receta_medica_detalle).IsLoaded)
        {
            await dbContext.Entry(recipe).Collection(x => x.tbl_receta_medica_detalle).LoadAsync(cancellationToken);
        }

        if (recipe.tbl_receta_medica_detalle.Count > 0)
        {
            dbContext.tbl_receta_medica_detalle.RemoveRange(recipe.tbl_receta_medica_detalle);
            recipe.tbl_receta_medica_detalle.Clear();
        }

        if (items.Count == 0)
        {
            items = BuildFallbackPrescriptionItems(request.Editor.Diagnostico);
        }

        foreach (var item in items)
        {
            recipe.tbl_receta_medica_detalle.Add(new tbl_receta_medica_detalle
            {
                tipo_item_prescrito = string.IsNullOrWhiteSpace(item.TipoItem) ? "Medicamento" : item.TipoItem.Trim(),
                id_producto = item.ProductoId > 0 ? item.ProductoId : null,
                nombre_item = item.NombreItem.Trim(),
                indicaciones = NormalizeOptional(item.Indicaciones),
                cantidad = item.Cantidad <= 0 ? 1 : item.Cantidad,
                unidad = NormalizeOptional(item.Unidad),
                enviar_a_facturacion = item.EnviarAFacturacion,
                disponible_facturacion = false,
                stock_disponible = null,
                observaciones = NormalizeOptional(item.Observaciones),
                fecha_creacion = DateTime.Now
            });
        }
    }

    private static List<PrescriptionLineItem> BuildFallbackPrescriptionItems(DiagnosisSection diagnosis)
    {
        var items = new List<PrescriptionLineItem>();

        if (!string.IsNullOrWhiteSpace(diagnosis.MedicamentosRecetados))
        {
            items.Add(new PrescriptionLineItem
            {
                TipoItem = "Medicamento",
                NombreItem = diagnosis.MedicamentosRecetados.Trim(),
                Cantidad = 1,
                Unidad = "indicacion",
                EnviarAFacturacion = false
            });
        }

        if (!string.IsNullOrWhiteSpace(diagnosis.ExamenesIndicados))
        {
            items.Add(new PrescriptionLineItem
            {
                TipoItem = "Examen",
                NombreItem = diagnosis.ExamenesIndicados.Trim(),
                Cantidad = 1,
                Unidad = "orden",
                EnviarAFacturacion = false
            });
        }

        return items;
    }

    private static void ApplyStructuredAnamnesisDefaults(ClinicalHistoryEditorModel editor)
    {
        var structuredSummary = BuildStructuredAnamnesisSummary(editor.AnamnesisGuiada);
        if (string.IsNullOrWhiteSpace(structuredSummary))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(editor.Anamnesis))
        {
            editor.Anamnesis = structuredSummary;
            return;
        }

        var marker = $"Resumen guiado: {structuredSummary}";
        if (!editor.Anamnesis.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            editor.Anamnesis = $"{editor.Anamnesis.Trim()}\n\n{marker}";
        }
    }

    private static T? Deserialize<T>(string? value) where T : class
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ClinicalHistoryLoadResult
{
    public tbl_historia_clinica_optometria? History { get; init; }
    public required OpeningSnapshot OpeningSnapshot { get; init; }
    public required ClinicalHistoryEditorModel Editor { get; init; }
    public required List<ClinicalEncounterSummary> Encounters { get; init; }
    public ClinicalEncounterSummary? SelectedEncounter { get; init; }
    public bool HasLegacyDataPendingMigration { get; init; }
    public string? LegacyEncounterLabel { get; init; }
}

public sealed class ClinicalHistorySaveRequest
{
    public required tbl_paciente Patient { get; init; }
    public required OpeningSnapshot OpeningSnapshot { get; init; }
    public required ClinicalHistoryEditorModel Editor { get; init; }
    public required int ActorUserId { get; init; }
    public required BillingDraftService BillingDraftService { get; init; }
    public int? SelectedEncounterId { get; init; }
    public bool HasLegacyDataPendingMigration { get; init; }
    public ClinicalHistorySaveMode Mode { get; init; }
}

public sealed class ClinicalHistorySaveResult
{
    public int? EncounterId { get; init; }
    public int? ConsultationId { get; init; }
    public string? StatusMessage { get; init; }
    public List<string> ValidationErrors { get; init; } = [];
}

public sealed class ClinicalEncounterSummary
{
    public int EncounterId { get; init; }
    public int ConsultationId { get; init; }
    public required string Status { get; init; }
    public int Progress { get; init; }
    public required string Motive { get; init; }
    public required string Diagnosis { get; init; }
    public DateTime EventDate { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public enum ClinicalHistorySaveMode
{
    Draft = 1,
    Finalize = 2
}

public sealed class ClinicalHistoryEditorModel
{
    public string NumeroHistoria { get; set; } = string.Empty;
    public string Consultorio { get; set; } = string.Empty;
    public string LlaveClinica { get; set; } = string.Empty;
    public string LugarNacimiento { get; set; } = string.Empty;
    public string Procedencia { get; set; } = string.Empty;
    public string UltimoControl { get; set; } = string.Empty;
    public string MotivoConsulta { get; set; } = string.Empty;
    public string Anamnesis { get; set; } = string.Empty;
    public bool UsaLentes { get; set; }
    public string ObservacionesGenerales { get; set; } = string.Empty;
    public string NombreExaminador { get; set; } = string.Empty;
    public string NivelParaleloJornada { get; set; } = string.Empty;
    public AnamnesisGuidedSection AnamnesisGuiada { get; set; } = new();
    public AntecedentsSection Antecedentes { get; set; } = new();
    public LensSection Lentes { get; set; } = new();
    public VisualSection Visual { get; set; } = new();
    public BiomicroscopiaSection Biomicroscopia { get; set; } = new();
    public OftalmoscopiaSection Oftalmoscopia { get; set; } = new();
    public MotorExamSection Motor { get; set; } = new();
    public KeratometrySection Keratometria { get; set; } = new();
    public RefractionSection Refraction { get; set; } = new();
    public DiagnosisSection Diagnostico { get; set; } = new();
    public ConsentSection Consentimiento { get; set; } = new();
}

public sealed class AnamnesisGuidedSection
{
    public string MotivoPrincipal { get; set; } = string.Empty;
    public string Inicio { get; set; } = string.Empty;
    public string DuracionValor { get; set; } = string.Empty;
    public string DuracionUnidad { get; set; } = "dias";
    public string Lateralidad { get; set; } = string.Empty;
    public string Intensidad { get; set; } = string.Empty;
    public string Desencadenantes { get; set; } = string.Empty;
    public string Aliviantes { get; set; } = string.Empty;
    public string NotasAdicionales { get; set; } = string.Empty;
    public List<string> Sintomas { get; set; } = [];
    public List<string> BanderasAlerta { get; set; } = [];
}

public sealed class OpeningSnapshot
{
    public string Apellidos { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string FechaNacimiento { get; set; } = string.Empty;
    public string Edad { get; set; } = string.Empty;
    public string Genero { get; set; } = string.Empty;
    public string Ocupacion { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
}

public sealed class AntecedentsSection
{
    public string PersonalesOculares { get; set; } = string.Empty;
    public string PersonalesGenerales { get; set; } = string.Empty;
    public string FamiliaresOculares { get; set; } = string.Empty;
    public string FamiliaresGenerales { get; set; } = string.Empty;
}

public sealed class LensSection
{
    public string Addicion { get; set; } = string.Empty;
    public string Prismas { get; set; } = string.Empty;
    public string Od { get; set; } = string.Empty;
    public string Oi { get; set; } = string.Empty;
    public string Ao { get; set; } = string.Empty;
    public string TipoLente { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string Filtro { get; set; } = string.Empty;
    public string TiempoUsoRx { get; set; } = string.Empty;
    public string DistPupilar { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class VisualSection
{
    public string DistanciaVl { get; set; } = "6 metros";
    public string DistanciaVp { get; set; } = "40 cm";
    public string Ph { get; set; } = string.Empty;
    public string Dominancia { get; set; } = string.Empty;
    public string Optotipo { get; set; } = string.Empty;
    public string OdVlSc { get; set; } = string.Empty;
    public string OiVlSc { get; set; } = string.Empty;
    public string AoVlSc { get; set; } = string.Empty;
    public string OdVpSc { get; set; } = string.Empty;
    public string OiVpSc { get; set; } = string.Empty;
    public string AoVpSc { get; set; } = string.Empty;
}

public sealed class BiomicroscopiaSection
{
    public string GraphicOd { get; set; } = string.Empty;
    public string GraphicOi { get; set; } = string.Empty;
    public string OrbitaOd { get; set; } = string.Empty;
    public string OrbitaOi { get; set; } = string.Empty;
    public string ParpadosOd { get; set; } = string.Empty;
    public string ParpadosOi { get; set; } = string.Empty;
    public string SistemaLagrimalOd { get; set; } = string.Empty;
    public string SistemaLagrimalOi { get; set; } = string.Empty;
    public string ConjuntivaOd { get; set; } = string.Empty;
    public string ConjuntivaOi { get; set; } = string.Empty;
    public string CorneaOd { get; set; } = string.Empty;
    public string CorneaOi { get; set; } = string.Empty;
    public string IrisOd { get; set; } = string.Empty;
    public string IrisOi { get; set; } = string.Empty;
    public string CristalinoOd { get; set; } = string.Empty;
    public string CristalinoOi { get; set; } = string.Empty;
    public string TestAdicionalesOd { get; set; } = string.Empty;
    public string TestAdicionalesOi { get; set; } = string.Empty;
}

public sealed class OftalmoscopiaSection
{
    public string GraphicOd { get; set; } = string.Empty;
    public string GraphicOi { get; set; } = string.Empty;
    public string PapilaOd { get; set; } = string.Empty;
    public string PapilaOi { get; set; } = string.Empty;
    public string VasosOd { get; set; } = string.Empty;
    public string VasosOi { get; set; } = string.Empty;
    public string MaculaOd { get; set; } = string.Empty;
    public string MaculaOi { get; set; } = string.Empty;
    public string TapeteOd { get; set; } = string.Empty;
    public string TapeteOi { get; set; } = string.Empty;
}

public sealed class MotorExamSection
{
    public string Resumen { get; set; } = string.Empty;
    public string HallazgosComplementarios { get; set; } = string.Empty;
}

public sealed class KeratometrySection
{
    public string Resumen { get; set; } = string.Empty;
    public string AstigmatismoCorneal { get; set; } = string.Empty;
}

public sealed class RefractionSection
{
    public string RxEstaticaDinamica { get; set; } = string.Empty;
    public string SubjetivoAfinacionBalance { get; set; } = string.Empty;
    public string PruebaAmbulatoriaRxFinal { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class DiagnosisSection
{
    public string DiagnosticoOd { get; set; } = string.Empty;
    public string DiagnosticoOi { get; set; } = string.Empty;
    public string DiagnosticoMotor { get; set; } = string.Empty;
    public string Cie10 { get; set; } = string.Empty;
    public string PatologicoPresuntivo { get; set; } = string.Empty;
    public string TratamientoConducta { get; set; } = string.Empty;
    public string ExamenesIndicados { get; set; } = string.Empty;
    public string MedicamentosRecetados { get; set; } = string.Empty;
    public List<PrescriptionLineItem> PrescripcionItems { get; set; } = [];
}

public sealed class PrescriptionLineItem
{
    public string TipoItem { get; set; } = "Medicamento";
    public int ProductoId { get; set; }
    public string NombreItem { get; set; } = string.Empty;
    public int Cantidad { get; set; } = 1;
    public string Unidad { get; set; } = "unidad";
    public string Indicaciones { get; set; } = string.Empty;
    public bool EnviarAFacturacion { get; set; }
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class ConsentSection
{
    public string Nombre { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string FirmaReferencia { get; set; } = string.Empty;
    public string FirmaDibujada { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public bool Autorizado { get; set; }
}
