using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_sesion
{
    public int id_sesion { get; set; }

    public int id_usuario { get; set; }

    public DateTime? fecha_inicio { get; set; }

    public DateTime? fecha_fin { get; set; }

    public string? ip { get; set; }

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;
}
