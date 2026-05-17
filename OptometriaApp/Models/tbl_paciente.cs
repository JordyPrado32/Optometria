using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_paciente
{
    public int id_paciente { get; set; }

    public string? codigo_paciente { get; set; }

    public string cedula { get; set; } = null!;

    public string nombres { get; set; } = null!;

    public string apellidos { get; set; } = null!;

    public DateOnly? fecha_nacimiento { get; set; }

    public int? edad { get; set; }

    public string? genero { get; set; }

    public string? estado_civil { get; set; }

    public string? ocupacion { get; set; }

    public string? direccion { get; set; }

    public string? telefono { get; set; }

    public string? email { get; set; }

    public string? observaciones { get; set; }

    public bool? activo { get; set; }

    public DateTime? fecha_registro { get; set; }

    public int? id_usuario_registro { get; set; }

    public virtual tbl_usuario? id_usuario_registroNavigation { get; set; }

    public virtual ICollection<tbl_comunicacion> tbl_comunicacions { get; set; } = new List<tbl_comunicacion>();

    public virtual ICollection<tbl_consulta> tbl_consulta { get; set; } = new List<tbl_consulta>();

    public virtual ICollection<tbl_venta> tbl_venta { get; set; } = new List<tbl_venta>();
}
