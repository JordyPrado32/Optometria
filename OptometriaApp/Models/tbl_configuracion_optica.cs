using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_configuracion_optica
{
    public int id_configuracion { get; set; }

    public string? nombre_comercial { get; set; }

    public string? ruc { get; set; }

    public string? direccion { get; set; }

    public string? telefono { get; set; }

    public string? prefijo_pais { get; set; }

    public decimal? porcentaje_impuesto { get; set; }

    public string? carpeta_rx { get; set; }

    public string? ruta_logo { get; set; }

    public string? ruta_fondo { get; set; }

    public string? tienda_hero_titulo { get; set; }

    public string? tienda_hero_subtitulo { get; set; }

    public string? tienda_hero_boton { get; set; }

    public string? tienda_hero_imagen { get; set; }

    public string? tienda_banner_texto { get; set; }

    public string? tienda_basicos_titulo { get; set; }

    public string? tienda_basicos_subtitulo { get; set; }
}
