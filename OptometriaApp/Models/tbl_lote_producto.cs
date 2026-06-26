using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_lote_producto
{
    public int id_lote { get; set; }
    public int id_producto { get; set; }
    public string numero_lote { get; set; } = null!;
    public string? numero_serie { get; set; }
    public int? id_orden_compra { get; set; }
    public int cantidad_inicial { get; set; }
    public int cantidad_disponible { get; set; }
    public int? cantidad_vendida { get; set; }
    public int? cantidad_devuelta { get; set; }
    public int? cantidad_merma { get; set; }
    public DateOnly? fecha_fabricacion { get; set; }
    public DateOnly? fecha_vencimiento { get; set; }
    public decimal? costo_unitario { get; set; }
    public decimal? precio_venta_unitario { get; set; }
    public decimal? valor_total_costo { get; set; }
    public string? estado_lote { get; set; }
    public string? almacen { get; set; }
    public string? pasillo { get; set; }
    public string? estante { get; set; }
    public string? nivel { get; set; }
    public DateTime? fecha_ingreso { get; set; }
    public DateTime? fecha_ultima_salida { get; set; }
    public int? id_usuario_ingreso { get; set; }
    public string? observaciones { get; set; }

    public virtual tbl_producto id_productoNavigation { get; set; } = null!;
    public virtual tbl_orden_compra? id_orden_compraNavigation { get; set; }
    public virtual tbl_usuario? id_usuario_ingresoNavigation { get; set; }
    public virtual ICollection<tbl_detalle_orden_compra> tbl_detalle_orden_compra { get; set; } = new List<tbl_detalle_orden_compra>();
    public virtual ICollection<tbl_kardex> tbl_kardex { get; set; } = new List<tbl_kardex>();
}
