using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_log_auditoria
{
    public int id_log_auditoria { get; set; }

    public int? id_usuario { get; set; }

    public string? accion { get; set; }

    public string? modulo { get; set; }

    public DateTime? fecha { get; set; }

    public string? detalle { get; set; }

    public virtual tbl_usuario? id_usuarioNavigation { get; set; }
}
