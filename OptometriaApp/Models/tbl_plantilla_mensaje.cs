using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_plantilla_mensaje
{
    public int id_plantilla_mensaje { get; set; }

    public string? nombre { get; set; }

    public string? canal { get; set; }

    public string? tipo { get; set; }

    public string? contenido { get; set; }

    public virtual ICollection<tbl_comunicacion> tbl_comunicacions { get; set; } = new List<tbl_comunicacion>();
}
