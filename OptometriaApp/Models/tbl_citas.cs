using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_citas
{
    public int id_cita { get; set; }

    public int id_medico { get; set; }

    public int id_paciente { get; set; }

    public int? id_disponibilidad { get; set; }

    public DateOnly fecha_cita { get; set; }

    public TimeOnly hora_inicio { get; set; }

    public TimeOnly hora_fin { get; set; }

    public string? tipo_cita { get; set; }

    public string? motivo_cita { get; set; }

    public string? descripcion_adicional { get; set; }

    public int? id_estado { get; set; }

    public DateTime? fecha_confirmacion { get; set; }

    public string? usuario_confirmacion { get; set; }

    public string? razon_cancelacion { get; set; }

    public DateTime? fecha_cancelacion { get; set; }

    public string? usuario_cancelacion { get; set; }

    public bool? notificacion_enviada { get; set; }

    public DateTime? fecha_notificacion_enviada { get; set; }

    public string? tipo_notificacion { get; set; }

    public bool? recordatorio_24hrs { get; set; }

    public bool? recordatorio_1hr { get; set; }

    public int? id_consulta { get; set; }

    public string? notas_medico { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public string? usuario_creacion { get; set; }

    public string? usuario_actualizacion { get; set; }

    public virtual tbl_consulta? id_consultaNavigation { get; set; }

    public virtual tbl_disponibilidad_medico? id_disponibilidadNavigation { get; set; }

    public virtual tbl_estado_cita? id_estadoNavigation { get; set; }

    public virtual tbl_medico id_medicoNavigation { get; set; } = null!;

    public virtual tbl_paciente id_pacienteNavigation { get; set; } = null!;

    public virtual ICollection<tbl_cancelaciones_paciente> tbl_cancelaciones_paciente { get; set; } = new List<tbl_cancelaciones_paciente>();
}
