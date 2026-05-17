using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_metodo_pago
{
    public int id_metodo_pago { get; set; }

    public string nombre { get; set; } = null!;

    public virtual ICollection<tbl_abono> tbl_abonos { get; set; } = new List<tbl_abono>();
}
