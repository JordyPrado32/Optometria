using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_cobro_transferencia
{
    public int id_cobro_transferencia { get; set; }

    public int? id_cta_cobrar { get; set; }

    public int? id_comprobante { get; set; }

    public int id_usuario_solicita { get; set; }

    public int? id_usuario_aprueba { get; set; }

    public decimal monto { get; set; }

    public string referencia { get; set; } = null!;

    public string? banco_origen { get; set; }

    public string? cedula_titular { get; set; }

    public string? nombre_titular { get; set; }

    public string? ruta_comprobante { get; set; }

    public string? observaciones { get; set; }

    public string estado { get; set; } = null!;

    public DateTime fecha_solicitud { get; set; }

    public DateTime? fecha_resolucion { get; set; }

    public DateTime? fecha_retiro_estimada { get; set; }

    public string? mensaje_retiro { get; set; }

    public string? observacion_resolucion { get; set; }

    public int? id_abono_generado { get; set; }

    public virtual tbl_cta_cobrar? id_cta_cobrarNavigation { get; set; }

    public virtual tbl_comprobante? id_comprobanteNavigation { get; set; }

    public virtual tbl_usuario id_usuario_solicitaNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_apruebaNavigation { get; set; }

    public virtual ICollection<tbl_cobro_transferencia_detalle> tbl_cobro_transferencia_detalles { get; set; } = new List<tbl_cobro_transferencia_detalle>();
}
