using System;

namespace OptometriaApp.Models;

public partial class tbl_kardex
{
    public int id_kardex { get; set; }
    public int id_producto { get; set; }
    public int? id_lote { get; set; }
    public string? numero_lote { get; set; }
    public DateTime? fecha_movimiento { get; set; }
    public string tipo_movimiento { get; set; } = null!;
    public int? id_referencia { get; set; }
    public string? tipo_referencia { get; set; }
    public string? comprobante_numero { get; set; }
    public int cantidad_movimiento { get; set; }
    public decimal? costo_unitario { get; set; }
    public decimal? costo_total { get; set; }
    public int? stock_anterior { get; set; }
    public int? stock_nuevo { get; set; }
    public decimal? saldo_anterior_dinero { get; set; }
    public decimal? saldo_nuevo_dinero { get; set; }
    public decimal? precio_promedio_ponderado { get; set; }
    public string? metodo_valuacion { get; set; }
    public int? id_usuario_movimiento { get; set; }
    public string? descripcion_movimiento { get; set; }
    public string? glosa_contable { get; set; }
    public string? cuenta_contable_debito { get; set; }
    public string? cuenta_contable_credito { get; set; }
    public string? centro_costo { get; set; }
    public string? estado_kardex { get; set; }
    public string? observaciones { get; set; }
    public DateTime? fecha_creacion { get; set; }

    public virtual tbl_producto id_productoNavigation { get; set; } = null!;
    public virtual tbl_lote_producto? id_loteNavigation { get; set; }
    public virtual tbl_usuario? id_usuario_movimientoNavigation { get; set; }
}
