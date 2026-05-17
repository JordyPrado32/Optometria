using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_detalle_venta
{
    public int id_detalle_venta { get; set; }

    public int id_venta { get; set; }

    public int id_producto { get; set; }

    public int cantidad { get; set; }

    public decimal? precio_unitario { get; set; }

    public decimal? descuento { get; set; }

    public string? motivo_descuento { get; set; }

    public string? concepto_item { get; set; }

    public decimal? total_item { get; set; }

    public virtual tbl_producto id_productoNavigation { get; set; } = null!;

    public virtual tbl_venta id_ventaNavigation { get; set; } = null!;
}
