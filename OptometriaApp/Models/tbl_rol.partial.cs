namespace OptometriaApp.Models;

public partial class tbl_rol
{
    public virtual ICollection<tbl_rol_menu_permiso> tbl_rol_menu_permisos { get; set; } = new List<tbl_rol_menu_permiso>();
}
