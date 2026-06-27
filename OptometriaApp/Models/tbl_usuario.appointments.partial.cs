namespace OptometriaApp.Models;

public partial class tbl_usuario
{
    public virtual tbl_medico? tbl_medico { get; set; }

    public virtual tbl_paciente? tbl_paciente_vinculado { get; set; }
}
