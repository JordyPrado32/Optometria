using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_nota_credito
{
    public int id_nota_credito { get; set; }

    public int? id_comprobante_relacionado { get; set; }

    public int? id_cta_cobrar { get; set; }

    public string numero_nota { get; set; } = null!;

    public DateTime fecha_emision { get; set; }

    public decimal monto_total { get; set; }

    public decimal? saldo_disponible { get; set; }

    public DateTime? fecha_vencimiento { get; set; }

    public long? secuencial { get; set; }

    public string tipo_nota { get; set; } = "Total";

    public string? motivo { get; set; }

    public string estado { get; set; } = null!;

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

    public string? usuario_creacion { get; set; }

    public DateTime fecha_creacion { get; set; }

    public virtual ICollection<tbl_detalle_nota_credito> tbl_detalle_nota_credito { get; set; } = new List<tbl_detalle_nota_credito>();

    public virtual tbl_comprobante? id_comprobante_relacionadoNavigation { get; set; }

    public virtual tbl_cta_cobrar? id_cta_cobrarNavigation { get; set; }
}
