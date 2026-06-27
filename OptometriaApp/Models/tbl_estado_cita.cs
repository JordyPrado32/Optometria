using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_estado_cita
{
    public int id_estado { get; set; }

    public string nombre_estado { get; set; } = null!;

    public string? descripcion { get; set; }

    public bool? activo { get; set; }

    public virtual ICollection<tbl_citas> tbl_citas { get; set; } = new List<tbl_citas>();
}
