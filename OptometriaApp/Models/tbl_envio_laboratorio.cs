using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_envio_laboratorio
{
    public int id_envio_laboratorio { get; set; }

    public int id_orden_rx { get; set; }

    public int id_usuario { get; set; }

    public string? canal { get; set; }

    public string? estado { get; set; }

    public DateTime? fecha_envio { get; set; }

    public DateTime? fecha_cambio_estado { get; set; }

    public int? id_usuario_entrega { get; set; }

    public string? metodo_entrega { get; set; }

    public decimal? tarifa_entrega { get; set; }

    public string? direccion_entrega { get; set; }

    public string? referencia_entrega { get; set; }

    public string? telefono_entrega { get; set; }

    public string? nombre_receptor { get; set; }

    public DateTime? fecha_listo_entrega { get; set; }

    public DateTime? fecha_entregado { get; set; }

    public int? id_comprobante_entrega { get; set; }

    public string? numero_guia_remision { get; set; }

    public string? repartidor_nombre { get; set; }

    public string? repartidor_telefono { get; set; }

    public string? estado_tracking { get; set; }

    public decimal? latitud_actual { get; set; }

    public decimal? longitud_actual { get; set; }

    public string? url_mapa_seguimiento { get; set; }

    public string? observaciones_logistica { get; set; }

    public virtual tbl_orden_rx id_orden_rxNavigation { get; set; } = null!;

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_entregaNavigation { get; set; }
}
