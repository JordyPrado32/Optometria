using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_abono
{
    public int id_abono { get; set; }

    public int id_venta { get; set; }

    public int id_usuario { get; set; }

    public int id_metodo_pago { get; set; }

    public decimal monto { get; set; }

    public DateTime? fecha_abono { get; set; }

    public string? concepto { get; set; }

    public virtual tbl_metodo_pago id_metodo_pagoNavigation { get; set; } = null!;

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;

    public virtual tbl_venta id_ventaNavigation { get; set; } = null!;
}
