using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_comprobante
{
    public int id_comprobante { get; set; }

    public int? id_venta { get; set; }

    public string? tipo_comprobante { get; set; }

    public string? numero_comprobante { get; set; }

    public long? secuencial { get; set; }

    public string? numero_autorizacion { get; set; }

    public DateTime? fecha_autorizacion { get; set; }

    public string? estado_comprobante { get; set; }

    public int? id_emisor { get; set; }

    public string? ruta_pdf { get; set; }

    public DateTime? fecha_emision { get; set; }

    public virtual tbl_venta? id_ventaNavigation { get; set; }
}
