using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_producto
{
    public int id_producto { get; set; }

    public int? id_proveedor { get; set; }

    public int? id_categoria { get; set; }

    public string codigo_producto { get; set; } = null!;

    public string nombre_producto { get; set; } = null!;

    public string? descripcion { get; set; }

    public decimal? precio_costo { get; set; }

    public decimal precio_venta { get; set; }

    public int? stock_actual { get; set; }

    public int? stock_minimo { get; set; }

    public bool? activo { get; set; }

    public virtual tbl_categoria_producto? id_categoriaNavigation { get; set; }

    public virtual tbl_proveedor? id_proveedorNavigation { get; set; }

    public virtual ICollection<tbl_detalle_venta> tbl_detalle_venta { get; set; } = new List<tbl_detalle_venta>();

    public virtual ICollection<tbl_movimiento_inventario> tbl_movimiento_inventarios { get; set; } = new List<tbl_movimiento_inventario>();
}
