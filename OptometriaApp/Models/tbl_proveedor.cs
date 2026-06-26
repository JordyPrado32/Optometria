using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_proveedor
{
    public int id_proveedor { get; set; }

    public string nombre { get; set; } = null!;

    public string? telefono { get; set; }

    public string? email { get; set; }

    public string? direccion { get; set; }

    public string? observaciones { get; set; }

    public string? ruc { get; set; }

    public string? razon_social { get; set; }

    public string? nombre_comercial { get; set; }

    public string? tipo_identificacion { get; set; }

    public string? ciudad { get; set; }

    public string? provincia { get; set; }

    public string? codigo_postal { get; set; }

    public string? contacto_nombre { get; set; }

    public string? contacto_telefono { get; set; }

    public string? contacto_correo { get; set; }

    public int? dias_credito_promedio { get; set; }

    public decimal? saldo_pendiente { get; set; }

    public decimal? limite_credito { get; set; }

    public string? condicion_pago { get; set; }

    public string? banco_nombre { get; set; }

    public string? cuenta_bancaria { get; set; }

    public string? tipo_cuenta { get; set; }

    public string? calificacion { get; set; }

    public int? tiempo_entrega_promedio { get; set; }

    public bool? es_activo { get; set; }

    public DateTime? fecha_registro { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public int? id_usuario_registro { get; set; }

    public int? id_usuario_actualizacion { get; set; }

    public virtual tbl_usuario? id_usuario_registroNavigation { get; set; }

    public virtual tbl_usuario? id_usuario_actualizacionNavigation { get; set; }

    public virtual ICollection<tbl_producto> tbl_productos { get; set; } = new List<tbl_producto>();

    public virtual ICollection<tbl_orden_compra> tbl_orden_compra { get; set; } = new List<tbl_orden_compra>();
}
