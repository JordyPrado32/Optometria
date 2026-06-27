using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_medico
{
    public int id_medico { get; set; }

    public int id_usuario { get; set; }

    public string numero_licencia { get; set; } = null!;

    public string? especialidad { get; set; }

    public string? cedula_profesional { get; set; }

    public string? institucion_egreso { get; set; }

    public int? anio_egreso { get; set; }

    public string? telefono_consultorio { get; set; }

    public string? biografia { get; set; }

    public string? certificaciones { get; set; }

    public string? idiomas { get; set; }

    public decimal? precio_consulta_base { get; set; }

    public decimal? descuento_porcentaje { get; set; }

    public bool? aceptar_citas_telefonicas { get; set; }

    public bool? aceptar_citas_presenciales { get; set; }

    public int? duracion_consulta_minutos { get; set; }

    public string? observaciones { get; set; }

    public bool? activo { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public string? usuario_creacion { get; set; }

    public string? usuario_actualizacion { get; set; }

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;

    public virtual ICollection<tbl_bloqueo_horarios> tbl_bloqueo_horarios { get; set; } = new List<tbl_bloqueo_horarios>();

    public virtual ICollection<tbl_citas> tbl_citas { get; set; } = new List<tbl_citas>();

    public virtual ICollection<tbl_disponibilidad_medico> tbl_disponibilidad_medico { get; set; } = new List<tbl_disponibilidad_medico>();
}
