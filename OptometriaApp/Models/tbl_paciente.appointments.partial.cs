using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_paciente
{
    public int? id_usuario { get; set; }

    public virtual tbl_usuario? id_usuarioNavigation { get; set; }

    public virtual ICollection<tbl_citas> tbl_citas { get; set; } = new List<tbl_citas>();

    public virtual ICollection<tbl_cancelaciones_paciente> tbl_cancelaciones_paciente { get; set; } = new List<tbl_cancelaciones_paciente>();
}
