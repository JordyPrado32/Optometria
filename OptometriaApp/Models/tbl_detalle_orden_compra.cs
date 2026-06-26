using System;

namespace OptometriaApp.Models;

public partial class tbl_detalle_orden_compra
{
    public int id_detalle_orden_compra { get; set; }
    public int id_orden_compra { get; set; }
    public int id_producto { get; set; }
    public int? id_lote { get; set; }
    public int cantidad_solicitada { get; set; }
    public int? cantidad_recibida { get; set; }
    public int? cantidad_rechazada { get; set; }
    public int? cantidad_pendiente { get; set; }
    public decimal precio_unitario { get; set; }
    public decimal? precio_total_linea { get; set; }
    public decimal? descuento_linea { get; set; }
    public decimal? impuesto_linea { get; set; }
    public string? codigo_fiscal_fe { get; set; }
    public string? unidad_medida_fe { get; set; }
    public string? estado_linea { get; set; }
    public DateOnly? fecha_recepcion_esperada { get; set; }
    public string? observaciones { get; set; }

    public virtual tbl_orden_compra id_orden_compraNavigation { get; set; } = null!;
    public virtual tbl_producto id_productoNavigation { get; set; } = null!;
    public virtual tbl_lote_producto? id_loteNavigation { get; set; }
}
