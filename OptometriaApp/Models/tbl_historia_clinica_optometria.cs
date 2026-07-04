using System;

namespace OptometriaApp.Models;

public partial class tbl_historia_clinica_optometria
{
    public int id_historia_clinica { get; set; }

    public int id_paciente { get; set; }

    public int id_optometra_apertura { get; set; }

    public int? id_optometra_ultima_actualizacion { get; set; }

    public DateTime? fecha_apertura { get; set; }

    public DateTime? fecha_ultima_actualizacion { get; set; }

    public string? numero_historia { get; set; }

    public string? consultorio { get; set; }

    public string? llave_clinica { get; set; }

    public string? lugar_nacimiento { get; set; }

    public string? procedencia { get; set; }

    public string? ultimo_control { get; set; }

    public string? datos_apertura_json { get; set; }

    public string? motivo_consulta { get; set; }

    public string? anamnesis { get; set; }

    public string? antecedentes_json { get; set; }

    public bool? usa_lentes { get; set; }

    public string? lentes_json { get; set; }

    public string? agudeza_visual_json { get; set; }

    public string? biomicroscopia_json { get; set; }

    public string? oftalmoscopia_json { get; set; }

    public string? examen_motor_json { get; set; }

    public string? queratometria_json { get; set; }

    public string? refraccion_json { get; set; }

    public string? diagnostico_json { get; set; }

    public string? observaciones_generales { get; set; }

    public string? nombre_examinador { get; set; }

    public string? nivel_paralelo_jornada { get; set; }

    public string? consentimiento_json { get; set; }

    public bool? activo { get; set; }
}
