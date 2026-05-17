using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_envio_laboratorio
{
    public int id_envio_laboratorio { get; set; }

    public int id_orden_rx { get; set; }

    public int id_usuario { get; set; }

    public string? canal { get; set; }

    public string? estado { get; set; }

    public DateTime? fecha_envio { get; set; }

    public DateTime? fecha_cambio_estado { get; set; }

    public int? id_usuario_entrega { get; set; }

    public virtual tbl_orden_rx id_orden_rxNavigation { get; set; } = null!;

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_entregaNavigation { get; set; }
}
