using System;

namespace OptometriaApp.Models;

public partial class EmisorEntity
{
    public int emisor_id { get; set; }

    public string ruc { get; set; } = null!;

    public string razon_social { get; set; } = null!;

    public string? nombre_comercial { get; set; }

    public string tipo_persona { get; set; } = null!;

    public string tipo_identificacion { get; set; } = null!;

    public string? direccion { get; set; }

    public string? telefono { get; set; }

    public string? correo { get; set; }

    public string? provincia { get; set; }

    public string? ciudad { get; set; }

    public string? codigo_postal { get; set; }

    public string establecimiento_codigo { get; set; } = null!;

    public string punto_emision_codigo { get; set; } = null!;

    public string? nombre_representante_legal { get; set; }

    public string? cedula_representante { get; set; }

    public string? direccion_establecimiento { get; set; }

    public bool obligado_contabilidad { get; set; }

    public bool es_contribuyente_especial { get; set; }

    public string? numero_contribuyente_especial { get; set; }

    public string ambiente_codigo { get; set; } = "1";

    public string tipo_emision_codigo { get; set; } = "1";

    public string? certificado_digital_ruta { get; set; }

    public string? certificado_digital_clave { get; set; }

    public string? regimen_rimpe { get; set; }

    public bool estado { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public DateTime? fecha_actualizacion { get; set; }

    public int id_usuario_creacion { get; set; }

    public int? id_usuario_actualizacion { get; set; }

    public virtual tbl_usuario id_usuario_creacionNavigation { get; set; } = null!;

    public virtual tbl_usuario? id_usuario_actualizacionNavigation { get; set; }
}
