using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_archivo_consulta
{
    public int id_archivo_consulta { get; set; }

    public int id_consulta { get; set; }

    public string? ruta_archivo { get; set; }

    public string? nombre_original { get; set; }

    public string? tipo_archivo { get; set; }

    public DateTime? fecha_subida { get; set; }

    public virtual tbl_consulta id_consultaNavigation { get; set; } = null!;
}
