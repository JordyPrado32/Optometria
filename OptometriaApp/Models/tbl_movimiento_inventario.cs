using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_movimiento_inventario
{
    public int id_movimiento_inventario { get; set; }

    public int id_producto { get; set; }

    public int id_usuario { get; set; }

    public string? tipo_movimiento { get; set; }

    public int cantidad { get; set; }

    public int? stock_anterior { get; set; }

    public int? stock_resultante { get; set; }

    public DateTime? fecha_movimiento { get; set; }

    public string? observaciones { get; set; }

    public virtual tbl_producto id_productoNavigation { get; set; } = null!;

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;
}
