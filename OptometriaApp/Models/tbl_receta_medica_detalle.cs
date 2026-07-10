using System;

namespace OptometriaApp.Models;

public partial class tbl_receta_medica_detalle
{
    public int id_receta_detalle { get; set; }

    public int id_receta { get; set; }

    public string tipo_item_prescrito { get; set; } = null!;

    public int? id_producto { get; set; }

    public string nombre_item { get; set; } = null!;

    public string? indicaciones { get; set; }

    public int cantidad { get; set; }

    public string? unidad { get; set; }

    public bool? enviar_a_facturacion { get; set; }

    public bool? disponible_facturacion { get; set; }

    public int? stock_disponible { get; set; }

    public string? observaciones { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public virtual tbl_receta_medica id_recetaNavigation { get; set; } = null!;

    public virtual tbl_producto? id_productoNavigation { get; set; }
}
