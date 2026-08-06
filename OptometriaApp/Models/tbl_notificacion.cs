using System;

namespace OptometriaApp.Models;

public partial class tbl_notificacion
{
    public int id_notificacion { get; set; }

    public int id_usuario_destino { get; set; }

    public int? id_usuario_origen { get; set; }

    public string titulo { get; set; } = null!;

    public string mensaje { get; set; } = null!;

    public string tipo { get; set; } = null!;

    public string? ruta_destino { get; set; }

    public string? modulo_origen { get; set; }

    public string? entidad_tipo { get; set; }

    public int? entidad_id { get; set; }

    public bool leida { get; set; }

    public DateTime fecha_creacion { get; set; }

    public DateTime? fecha_lectura { get; set; }

    public virtual tbl_usuario id_usuario_destinoNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_origenNavigation { get; set; }
}
