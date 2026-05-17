using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_laboratorio
{
    public int id_laboratorio { get; set; }

    public string nombre { get; set; } = null!;

    public string? correo { get; set; }

    public string? whatsapp { get; set; }

    public string? persona_contacto { get; set; }

    public string? direccion { get; set; }

    public bool? activo { get; set; }

    public virtual ICollection<tbl_orden_rx> tbl_orden_rxes { get; set; } = new List<tbl_orden_rx>();
}
