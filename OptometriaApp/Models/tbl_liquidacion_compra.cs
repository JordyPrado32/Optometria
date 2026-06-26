using System;

namespace OptometriaApp.Models;

public partial class tbl_liquidacion_compra
{
    public int id_liquidacion_compra { get; set; }
    public int id_orden_compra { get; set; }
    public string numero_liquidacion { get; set; } = null!;
    public int id_usuario_registro { get; set; }
    public DateTime? fecha_liquidacion { get; set; }
    public string? numero_factura { get; set; }
    public string? numero_autorizacion { get; set; }
    public decimal? subtotal { get; set; }
    public decimal? descuento_total { get; set; }
    public decimal? impuesto_total { get; set; }
    public decimal? total { get; set; }
    public decimal? saldo_pagado { get; set; }
    public decimal? saldo_pendiente { get; set; }
    public string? estado_liquidacion { get; set; }
    public string? observaciones { get; set; }
    public bool? activo { get; set; }
    public DateTime? fecha_creacion { get; set; }
    public DateTime? fecha_actualizacion { get; set; }

    public virtual tbl_orden_compra id_orden_compraNavigation { get; set; } = null!;
    public virtual tbl_usuario id_usuario_registroNavigation { get; set; } = null!;
}
