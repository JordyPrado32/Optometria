using System;

namespace OptometriaApp.Models;

public partial class tbl_cobro_transferencia_detalle
{
    public int id_cobro_transferencia_detalle { get; set; }

    public int id_cobro_transferencia { get; set; }

    public int id_producto { get; set; }

    public int cantidad { get; set; }

    public decimal precio_unitario { get; set; }

    public decimal total_item { get; set; }

    public string? nombre_producto_snapshot { get; set; }

    public DateTime fecha_creacion { get; set; }

    public virtual tbl_cobro_transferencia id_cobro_transferenciaNavigation { get; set; } = null!;

    public virtual tbl_producto id_productoNavigation { get; set; } = null!;
}
