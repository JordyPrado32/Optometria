namespace OptometriaApp.Models;

public sealed class AppMenuItem
{
    public int IdMenu { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Ruta { get; set; } = string.Empty;

    public string? Icono { get; set; }

    public int Orden { get; set; }

    public int? IdMenuPadre { get; set; }
}
