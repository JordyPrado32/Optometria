using System;

namespace OptometriaApp.Models;

public partial class tbl_detalle_nota_credito
{
    public int id_detalle_nota_credito { get; set; }

    public int id_nota_credito { get; set; }

    public int id_detalle_venta { get; set; }

    public int? cantidad_acreditada { get; set; }

    public decimal monto_subtotal { get; set; }

    public decimal monto_impuesto { get; set; }

    public decimal monto_total { get; set; }

    public decimal? porcentaje_impuesto { get; set; }

    public string? descripcion_item { get; set; }

    public DateTime fecha_creacion { get; set; }

    public virtual tbl_detalle_venta id_detalle_ventaNavigation { get; set; } = null!;

    public virtual tbl_nota_credito id_nota_creditoNavigation { get; set; } = null!;
}
