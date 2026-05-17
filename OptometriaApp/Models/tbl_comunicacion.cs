using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_comunicacion
{
    public int id_comunicacion { get; set; }

    public int? id_paciente { get; set; }

    public int? id_orden_rx { get; set; }

    public int? id_plantilla_mensaje { get; set; }

    public int? id_usuario { get; set; }

    public string? canal { get; set; }

    public string? destinatario { get; set; }

    public DateTime? fecha_envio { get; set; }

    public string? contenido_resumen { get; set; }

    public virtual tbl_orden_rx? id_orden_rxNavigation { get; set; }

    public virtual tbl_paciente? id_pacienteNavigation { get; set; }

    public virtual tbl_plantilla_mensaje? id_plantilla_mensajeNavigation { get; set; }

    public virtual tbl_usuario? id_usuarioNavigation { get; set; }
}
