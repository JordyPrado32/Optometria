using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_proveedor
{
    public int id_proveedor { get; set; }

    public string nombre { get; set; } = null!;

    public string? telefono { get; set; }

    public string? email { get; set; }

    public string? direccion { get; set; }

    public string? observaciones { get; set; }

    public virtual ICollection<tbl_producto> tbl_productos { get; set; } = new List<tbl_producto>();
}
