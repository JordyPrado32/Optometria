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

    public string? clave_acceso { get; set; }

    public string? codigo_numerico { get; set; }

    public string? ambiente_sri { get; set; }

    public string? tipo_emision_sri { get; set; }

    public string? version_xml { get; set; }

    public string? ruta_xml { get; set; }

    public string? xml_no_firmado { get; set; }

    public string? xml_firmado { get; set; }

    public string? hash_xml { get; set; }

    public DateTime? fecha_firma { get; set; }

    public string? estado_sri { get; set; }

    public string? mensajes_sri { get; set; }

    public DateTime? fecha_emision { get; set; }

    public virtual tbl_venta? id_ventaNavigation { get; set; }
}
