using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_venta
{
    public int id_venta { get; set; }

    public int id_paciente { get; set; }

    public int id_usuario { get; set; }

    public int? id_cliente_facturacion { get; set; }

    public DateTime? fecha_venta { get; set; }

    public decimal? subtotal { get; set; }

    public decimal? porcentaje_impuesto { get; set; }

    public decimal? impuesto_total { get; set; }

    public decimal? descuento_total { get; set; }

    public decimal? total { get; set; }

    public decimal? valor_cobrado { get; set; }

    public decimal? saldo_pendiente { get; set; }

    public string? estado { get; set; }

    public string? concepto { get; set; }

    public string? forma_pago { get; set; }

    public int? dias_credito { get; set; }

    public string? observaciones_factura { get; set; }

    public virtual tbl_paciente id_pacienteNavigation { get; set; } = null!;

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;

    public virtual ICollection<tbl_abono> tbl_abonos { get; set; } = new List<tbl_abono>();

    public virtual ICollection<tbl_comprobante> tbl_comprobantes { get; set; } = new List<tbl_comprobante>();

    public virtual ICollection<tbl_detalle_venta> tbl_detalle_venta { get; set; } = new List<tbl_detalle_venta>();
}
