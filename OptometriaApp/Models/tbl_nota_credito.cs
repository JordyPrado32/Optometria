using System;

namespace OptometriaApp.Models;

public partial class tbl_nota_credito
{
    public int id_nota_credito { get; set; }

    public int? id_comprobante_relacionado { get; set; }

    public int? id_cta_cobrar { get; set; }

    public string numero_nota { get; set; } = null!;

    public DateTime fecha_emision { get; set; }

    public decimal monto_total { get; set; }

    public string? motivo { get; set; }

    public string estado { get; set; } = null!;

    public string? usuario_creacion { get; set; }

    public DateTime fecha_creacion { get; set; }

    public virtual tbl_comprobante? id_comprobante_relacionadoNavigation { get; set; }

    public virtual tbl_cta_cobrar? id_cta_cobrarNavigation { get; set; }
}
