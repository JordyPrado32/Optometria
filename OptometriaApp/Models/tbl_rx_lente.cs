using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_rx_lente
{
    public int id_rx_lente { get; set; }

    public int id_consulta { get; set; }

    public decimal? od_esfera { get; set; }

    public decimal? od_cilindro { get; set; }

    public decimal? od_eje { get; set; }

    public decimal? od_addicion { get; set; }

    public decimal? od_prisma { get; set; }

    public decimal? od_dnp { get; set; }

    public decimal? od_dp { get; set; }

    public decimal? od_altura { get; set; }

    public decimal? oi_esfera { get; set; }

    public decimal? oi_cilindro { get; set; }

    public decimal? oi_eje { get; set; }

    public decimal? oi_addicion { get; set; }

    public decimal? oi_prisma { get; set; }

    public decimal? oi_dnp { get; set; }

    public decimal? oi_dp { get; set; }

    public decimal? oi_altura { get; set; }

    public string? diseno_lente { get; set; }

    public string? material { get; set; }

    public string? tratamiento { get; set; }

    public string? observaciones { get; set; }

    public virtual tbl_consulta id_consultaNavigation { get; set; } = null!;

    public virtual ICollection<tbl_orden_rx> tbl_orden_rxes { get; set; } = new List<tbl_orden_rx>();
}
