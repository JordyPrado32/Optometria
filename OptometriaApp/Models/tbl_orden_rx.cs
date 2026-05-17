using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_orden_rx
{
    public int id_orden_rx { get; set; }

    public int id_consulta { get; set; }

    public int id_laboratorio { get; set; }

    public int? id_rx_contactologia { get; set; }

    public int? id_rx_lente { get; set; }

    public string? numero_orden { get; set; }

    public string? tipo_rx { get; set; }

    public string? estado { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public string? observaciones { get; set; }

    public virtual tbl_consulta id_consultaNavigation { get; set; } = null!;

    public virtual tbl_laboratorio id_laboratorioNavigation { get; set; } = null!;

    public virtual tbl_rx_contactologia? id_rx_contactologiaNavigation { get; set; }

    public virtual tbl_rx_lente? id_rx_lenteNavigation { get; set; }

    public virtual ICollection<tbl_comunicacion> tbl_comunicacions { get; set; } = new List<tbl_comunicacion>();

    public virtual ICollection<tbl_envio_laboratorio> tbl_envio_laboratorios { get; set; } = new List<tbl_envio_laboratorio>();
}
