using System;

namespace OptometriaApp.Models;

public partial class tbl_cta_cobrar
{
    public int id_cta_cobrar { get; set; }

    public int id_cliente { get; set; }

    public int? id_venta { get; set; }

    public int? id_comprobante { get; set; }

    public decimal monto_total { get; set; }

    public decimal saldo { get; set; }

    public DateTime fecha_emision { get; set; }

    public DateOnly? fecha_vencimiento { get; set; }

    public string estado { get; set; } = null!;

    public DateTime fecha_creacion { get; set; }

    public string? usuario_creacion { get; set; }

    public virtual tbl_comprobante? id_comprobanteNavigation { get; set; }

    public virtual tbl_venta? id_ventaNavigation { get; set; }
}
