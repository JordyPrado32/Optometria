using System;

namespace OptometriaApp.Models;

public partial class tbl_historia_clinica_optometria_evento
{
    public int id_historia_evento { get; set; }

    public int id_historia_clinica { get; set; }

    public int id_paciente { get; set; }

    public int id_consulta { get; set; }

    public int id_optometra { get; set; }

    public DateTime fecha_evento { get; set; }

    public DateTime fecha_ultima_actualizacion { get; set; }

    public string estado { get; set; } = "Borrador";

    public int resumen_progreso { get; set; }

    public string? motivo_consulta { get; set; }

    public string? anamnesis { get; set; }

    public string? diagnostico_resumen { get; set; }

    public string? cie10 { get; set; }

    public string? payload_json { get; set; }

    public bool consentimiento_firmado { get; set; }

    public bool es_legado_migrado { get; set; }

    public bool activo { get; set; }
}
