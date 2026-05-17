using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_consulta
{
    public int id_consulta { get; set; }

    public int id_paciente { get; set; }

    public int id_optometra { get; set; }

    public DateTime? fecha_consulta { get; set; }

    public string? motivo_consulta { get; set; }

    public string? antecedentes_personales { get; set; }

    public string? antecedentes_familiares { get; set; }

    public string? antecedentes_oculares { get; set; }

    public string? enfermedades_previas { get; set; }

    public string? alergias { get; set; }

    public string? medicamentos { get; set; }

    public bool? usa_lentes { get; set; }

    public string? detalle_usa_lentes { get; set; }

    public string? historia_clinica { get; set; }

    public string? examenes_preliminares { get; set; }

    public string? evaluaciones { get; set; }

    public string? examenes_varios { get; set; }

    public string? notas { get; set; }

    public virtual tbl_usuario id_optometraNavigation { get; set; } = null!;

    public virtual tbl_paciente id_pacienteNavigation { get; set; } = null!;

    public virtual ICollection<tbl_archivo_consulta> tbl_archivo_consulta { get; set; } = new List<tbl_archivo_consulta>();

    public virtual ICollection<tbl_orden_rx> tbl_orden_rxes { get; set; } = new List<tbl_orden_rx>();

    public virtual ICollection<tbl_rx_contactologia> tbl_rx_contactologia { get; set; } = new List<tbl_rx_contactologia>();

    public virtual ICollection<tbl_rx_lente> tbl_rx_lentes { get; set; } = new List<tbl_rx_lente>();
}
