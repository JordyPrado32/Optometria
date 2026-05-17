using System;

namespace OptometriaApp.Models;

public partial class tbl_usuario_seguridad
{
    public int id_usuario { get; set; }

    public bool two_factor_enabled { get; set; }

    public string? authenticator_secret { get; set; }

    public string? recovery_password_hash { get; set; }

    public DateTime? recovery_password_expires_at { get; set; }

    public bool must_change_password { get; set; }

    public DateTime? created_at { get; set; }

    public DateTime? updated_at { get; set; }

    public virtual tbl_usuario id_usuarioNavigation { get; set; } = null!;
}
