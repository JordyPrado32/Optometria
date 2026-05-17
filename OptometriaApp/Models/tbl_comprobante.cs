using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_comprobante
{
    public int id_comprobante { get; set; }

    public int id_venta { get; set; }

    public string? numero_comprobante { get; set; }

    public string? ruta_pdf { get; set; }

    public DateTime? fecha_emision { get; set; }

    public virtual tbl_venta id_ventaNavigation { get; set; } = null!;
}
