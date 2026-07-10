using System;

namespace OptometriaApp.Models;

public partial class tbl_bloqueo_horarios
{
    public int id_bloqueo { get; set; }

    public int id_medico { get; set; }

    public DateOnly fecha_inicio { get; set; }

    public DateOnly fecha_fin { get; set; }

    public string? alcance_bloqueo { get; set; }

    public TimeOnly? hora_inicio { get; set; }

    public TimeOnly? hora_fin { get; set; }

    public string? tipo_bloqueo { get; set; }

    public string? razon_bloqueo { get; set; }

    public bool? activo { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public string? usuario_creacion { get; set; }

    public virtual tbl_medico id_medicoNavigation { get; set; } = null!;
}
