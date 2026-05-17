using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_categoria_producto
{
    public int id_categoria { get; set; }

    public string nombre { get; set; } = null!;

    public string? descripcion { get; set; }

    public virtual ICollection<tbl_producto> tbl_productos { get; set; } = new List<tbl_producto>();
}
