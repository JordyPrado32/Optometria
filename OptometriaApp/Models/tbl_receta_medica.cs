using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_receta_medica
{
    public int id_receta { get; set; }

    public int id_consulta { get; set; }

    public int id_paciente { get; set; }

    public int id_medico { get; set; }

    public string numero_receta { get; set; } = null!;

    public string? estado { get; set; }

    public string? diagnostico_resumen { get; set; }

    public string? observaciones { get; set; }

    public DateTime? fecha_emision { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public string? usuario_creacion { get; set; }

    public virtual tbl_consulta id_consultaNavigation { get; set; } = null!;

    public virtual tbl_paciente id_pacienteNavigation { get; set; } = null!;

    public virtual tbl_medico id_medicoNavigation { get; set; } = null!;

    public virtual ICollection<tbl_receta_medica_detalle> tbl_receta_medica_detalle { get; set; } = new List<tbl_receta_medica_detalle>();
}
