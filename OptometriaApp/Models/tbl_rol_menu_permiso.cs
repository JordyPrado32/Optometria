using System;

namespace OptometriaApp.Models;

public partial class tbl_rol_menu_permiso
{
    public int id_rol_menu_permiso { get; set; }

    public int id_rol { get; set; }

    public int id_menu { get; set; }

    public bool puede_ver { get; set; }

    public bool puede_crear { get; set; }

    public bool puede_editar { get; set; }

    public bool puede_eliminar { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public virtual tbl_menu_app id_menuNavigation { get; set; } = null!;

    public virtual tbl_rol id_rolNavigation { get; set; } = null!;
}
