using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_usuario
{
    public int id_usuario { get; set; }

    public int id_rol { get; set; }

    public string nombres { get; set; } = null!;

    public string apellidos { get; set; } = null!;

    public string? email { get; set; }

    public string usuario { get; set; } = null!;

    public string password_hash { get; set; } = null!;

    public string? telefono { get; set; }

    public bool? activo { get; set; }

    public int? intentos_fallidos { get; set; }

    public bool? bloqueado { get; set; }

    public DateOnly? ultimo_cambio_password { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public virtual tbl_rol id_rolNavigation { get; set; } = null!;

    public virtual ICollection<tbl_abono> tbl_abonos { get; set; } = new List<tbl_abono>();

    public virtual ICollection<tbl_comunicacion> tbl_comunicacions { get; set; } = new List<tbl_comunicacion>();

    public virtual ICollection<tbl_consulta> tbl_consulta { get; set; } = new List<tbl_consulta>();

    public virtual ICollection<tbl_envio_laboratorio> tbl_envio_laboratorioid_usuarioNavigations { get; set; } = new List<tbl_envio_laboratorio>();

    public virtual ICollection<tbl_envio_laboratorio> tbl_envio_laboratorioid_usuario_entregaNavigations { get; set; } = new List<tbl_envio_laboratorio>();

    public virtual ICollection<tbl_log_auditoria> tbl_log_auditoria { get; set; } = new List<tbl_log_auditoria>();

    public virtual ICollection<tbl_movimiento_inventario> tbl_movimiento_inventarios { get; set; } = new List<tbl_movimiento_inventario>();

    public virtual ICollection<tbl_paciente> tbl_pacientes { get; set; } = new List<tbl_paciente>();

    public virtual ICollection<tbl_sesion> tbl_sesions { get; set; } = new List<tbl_sesion>();

    public virtual ICollection<tbl_venta> tbl_venta { get; set; } = new List<tbl_venta>();
}
