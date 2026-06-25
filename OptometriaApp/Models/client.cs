using System;

namespace OptometriaApp.Models;

public partial class ClientEntity
{
    public int cliente_id { get; set; }

    public string tipo_cliente { get; set; } = null!;

    public string tipo_identificacion { get; set; } = null!;

    public string numero_identificacion { get; set; } = null!;

    public string razon_social { get; set; } = null!;

    public string? nombres { get; set; }

    public string? apellidos { get; set; }

    public string? nombre_comercial { get; set; }

    public string? direccion { get; set; }

    public string? ciudad { get; set; }

    public string? provincia { get; set; }

    public string? codigo_postal { get; set; }

    public string? telefono { get; set; }

    public string? correo_electronico { get; set; }

    public bool es_contribuyente_especial { get; set; }

    public string? numero_contribuyente_especial { get; set; }

    public string pais_codigo { get; set; } = "EC";

    public bool es_residente_exterior { get; set; }

    public bool es_consumidor_final { get; set; }

    public bool es_obligado_contabilidad { get; set; }

    public string? contacto_nombre { get; set; }

    public string? contacto_telefono { get; set; }

    public string? contacto_correo { get; set; }

    public string? condicion_pago { get; set; }

    public int dias_plazo { get; set; }

    public decimal limite_credito { get; set; }

    public decimal saldo_deudor { get; set; }

    public bool estado { get; set; }

    public string? observaciones { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public int id_usuario_creacion { get; set; }

    public int? id_usuario_actualizacion { get; set; }

    public virtual tbl_usuario id_usuario_creacionNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_actualizacionNavigation { get; set; }
}
