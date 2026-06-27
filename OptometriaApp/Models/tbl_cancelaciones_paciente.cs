using System;

namespace OptometriaApp.Models;

public partial class tbl_cancelaciones_paciente
{
    public int id_cancelacion { get; set; }

    public int id_cita { get; set; }

    public int id_paciente { get; set; }

    public DateTime? fecha_cancelacion { get; set; }

    public string? razon_cancelacion { get; set; }

    public string? quien_cancelo { get; set; }

    public bool? penalizacion_aplicada { get; set; }

    public int? dias_espera_proxima_cita { get; set; }

    public string? usuario_cancelacion { get; set; }

    public virtual tbl_citas id_citaNavigation { get; set; } = null!;

    public virtual tbl_paciente id_pacienteNavigation { get; set; } = null!;
}
