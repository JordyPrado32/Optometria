using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_disponibilidad_medico
{
    public int id_disponibilidad { get; set; }

    public int id_medico { get; set; }

    public byte dia_semana { get; set; }

    public string? nombre_dia { get; set; }

    public TimeOnly hora_inicio { get; set; }

    public TimeOnly hora_fin { get; set; }

    public bool? permitir_descanso_medio_dia { get; set; }

    public TimeOnly? hora_descanso_inicio { get; set; }

    public TimeOnly? hora_descanso_fin { get; set; }

    public bool? disponible { get; set; }

    public string? observaciones { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public string? usuario_actualizacion { get; set; }

    public virtual tbl_medico id_medicoNavigation { get; set; } = null!;

    public virtual ICollection<tbl_citas> tbl_citas { get; set; } = new List<tbl_citas>();
}
