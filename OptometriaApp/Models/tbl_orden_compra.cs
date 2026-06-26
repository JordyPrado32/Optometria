using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_orden_compra
{
    public int id_orden_compra { get; set; }
    public string numero_orden { get; set; } = null!;
    public int id_proveedor { get; set; }
    public int id_usuario_solicita { get; set; }
    public int? id_usuario_autoriza { get; set; }
    public DateTime? fecha_orden { get; set; }
    public DateOnly? fecha_requerida { get; set; }
    public DateOnly? fecha_recepcion_esperada { get; set; }
    public DateTime? fecha_recepcion_real { get; set; }
    public decimal? subtotal { get; set; }
    public decimal? descuento_general { get; set; }
    public decimal? impuesto_total { get; set; }
    public decimal? total { get; set; }
    public string? condicion_pago { get; set; }
    public int? dias_credito { get; set; }
    public DateOnly? fecha_vencimiento_pago { get; set; }
    public string? moneda { get; set; }
    public decimal? tasa_cambio { get; set; }
    public string? estado_orden { get; set; }
    public string? tipo_orden { get; set; }
    public string? referencia_externa { get; set; }
    public string? observaciones { get; set; }
    public bool? activo { get; set; }
    public DateTime? fecha_creacion { get; set; }
    public DateTime? fecha_actualizacion { get; set; }

    public virtual tbl_proveedor id_proveedorNavigation { get; set; } = null!;
    public virtual tbl_usuario id_usuario_solicitaNavigation { get; set; } = null!;
    public virtual tbl_usuario? id_usuario_autorizaNavigation { get; set; }
    public virtual ICollection<tbl_detalle_orden_compra> tbl_detalle_orden_compra { get; set; } = new List<tbl_detalle_orden_compra>();
    public virtual ICollection<tbl_recepcion_compra> tbl_recepcion_compra { get; set; } = new List<tbl_recepcion_compra>();
    public virtual ICollection<tbl_lote_producto> tbl_lote_producto { get; set; } = new List<tbl_lote_producto>();
    public virtual ICollection<tbl_liquidacion_compra> tbl_liquidacion_compra { get; set; } = new List<tbl_liquidacion_compra>();
}
