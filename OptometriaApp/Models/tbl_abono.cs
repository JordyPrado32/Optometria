using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_abono
{
    public int id_abono { get; set; }

    public int id_cta_cobrar { get; set; }

    public DateTime? fecha_abono { get; set; }

    public decimal monto_abono { get; set; }

    public int? metodo_pago_id { get; set; }

    public string? referencia_pago { get; set; }

    public string? usuario_registro { get; set; }

    public DateTime? fecha_registro { get; set; }

    public string? tipo_movimiento { get; set; }

    public int? id_abono_referencia { get; set; }

    public string? motivo_movimiento { get; set; }

    public virtual tbl_cta_cobrar id_cta_cobrarNavigation { get; set; } = null!;

    public virtual tbl_metodo_pago? metodo_pagoNavigation { get; set; }
}
