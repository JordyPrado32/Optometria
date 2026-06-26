using System;

namespace OptometriaApp.Models;

public partial class tbl_recepcion_compra
{
    public int id_recepcion { get; set; }
    public int id_orden_compra { get; set; }
    public string numero_recepcion { get; set; } = null!;
    public string? numero_guia_remision { get; set; }
    public int id_usuario_recibe { get; set; }
    public DateTime? fecha_recepcion { get; set; }
    public int? cantidad_total_recibida { get; set; }
    public int? cantidad_total_rechazada { get; set; }
    public string? observaciones_recepcion { get; set; }
    public string? estado_recepcion { get; set; }
    public bool? activo { get; set; }

    public virtual tbl_orden_compra id_orden_compraNavigation { get; set; } = null!;
    public virtual tbl_usuario id_usuario_recibeNavigation { get; set; } = null!;
}
