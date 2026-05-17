using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_rx_contactologia
{
    public int id_rx_contactologia { get; set; }

    public int id_consulta { get; set; }

    public decimal? od_esfera { get; set; }

    public decimal? od_cilindro { get; set; }

    public decimal? od_eje { get; set; }

    public decimal? od_diametro { get; set; }

    public decimal? od_curva_base { get; set; }

    public string? od_av { get; set; }

    public string? od_avcc_lejos { get; set; }

    public string? od_avcc_cerca { get; set; }

    public decimal? oi_esfera { get; set; }

    public decimal? oi_cilindro { get; set; }

    public decimal? oi_eje { get; set; }

    public decimal? oi_diametro { get; set; }

    public decimal? oi_curva_base { get; set; }

    public string? oi_av { get; set; }

    public string? oi_avcc_lejos { get; set; }

    public string? oi_avcc_cerca { get; set; }

    public string? tipo_lente { get; set; }

    public string? observaciones { get; set; }

    public virtual tbl_consulta id_consultaNavigation { get; set; } = null!;

    public virtual ICollection<tbl_orden_rx> tbl_orden_rxes { get; set; } = new List<tbl_orden_rx>();
}
