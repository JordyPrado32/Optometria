using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_menu_app
{
    public int id_menu { get; set; }

    public string nombre { get; set; } = null!;

    public string ruta { get; set; } = null!;

    public string? icono { get; set; }

    public int orden { get; set; }

    public bool activo { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public virtual ICollection<tbl_rol_menu_permiso> tbl_rol_menu_permisos { get; set; } = new List<tbl_rol_menu_permiso>();
}
