using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_rol
{
    public int id_rol { get; set; }

    public string nombre { get; set; } = null!;

    public string? descripcion { get; set; }

    public virtual ICollection<tbl_usuario> tbl_usuarios { get; set; } = new List<tbl_usuario>();
}
