using System.Globalization;
using System.Text.RegularExpressions;

namespace OptometriaApp.Services;

public static partial class AppValidation
{
    public static readonly string[] EcuadorProvinces =
    [
        "Azuay", "Bolivar", "Canar", "Carchi", "Chimborazo", "Cotopaxi", "El Oro", "Esmeraldas",
        "Galapagos", "Guayas", "Imbabura", "Loja", "Los Rios", "Manabi", "Morona Santiago",
        "Napo", "Orellana", "Pastaza", "Pichincha", "Santa Elena", "Santo Domingo de los Tsachilas",
        "Sucumbios", "Tungurahua", "Zamora Chinchipe"
    ];

    public static IReadOnlyList<string> GetCitiesForProvince(string? province)
    {
        if (string.IsNullOrWhiteSpace(province))
        {
            return [];
        }

        return EcuadorCitiesByProvince.TryGetValue(province.Trim(), out var cities) ? cities : [];
    }

    private static readonly Dictionary<string, string[]> EcuadorCitiesByProvince = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Azuay"] = ["Cuenca", "Gualaceo", "Paute", "Sigsig"],
        ["Bolivar"] = ["Guaranda", "Chillanes", "San Miguel", "Caluma"],
        ["Canar"] = ["Azogues", "La Troncal", "Canar", "Biblian"],
        ["Carchi"] = ["Tulcan", "Montufar", "Espejo", "Mira"],
        ["Chimborazo"] = ["Riobamba", "Guano", "Alausi", "Chambo"],
        ["Cotopaxi"] = ["Latacunga", "La Mana", "Salcedo", "Pujili"],
        ["El Oro"] = ["Machala", "Pasaje", "Santa Rosa", "Huaquillas"],
        ["Esmeraldas"] = ["Esmeraldas", "Atacames", "Quininde", "Muisne"],
        ["Galapagos"] = ["Puerto Baquerizo Moreno", "Puerto Ayora", "Puerto Villamil", "Santa Rosa"],
        ["Guayas"] = ["Guayaquil", "Duran", "Daule", "Samborondon"],
        ["Imbabura"] = ["Ibarra", "Otavalo", "Cotacachi", "Atuntaqui"],
        ["Loja"] = ["Loja", "Catamayo", "Macara", "Cariamanga"],
        ["Los Rios"] = ["Babahoyo", "Quevedo", "Ventanas", "Vinces"],
        ["Manabi"] = ["Portoviejo", "Manta", "Chone", "Jipijapa"],
        ["Morona Santiago"] = ["Macas", "Gualaquiza", "Sucua", "Limon Indanza"],
        ["Napo"] = ["Tena", "Archidona", "El Chaco", "Baeza"],
        ["Orellana"] = ["Francisco de Orellana", "Loreto", "La Joya de los Sachas", "Nuevo Rocafuerte"],
        ["Pastaza"] = ["Puyo", "Mera", "Santa Clara", "Arajuno"],
        ["Pichincha"] = ["Quito", "Cayambe", "Machachi", "Sangolqui"],
        ["Santa Elena"] = ["Santa Elena", "La Libertad", "Salinas", "Ballenita"],
        ["Santo Domingo de los Tsachilas"] = ["Santo Domingo", "La Concordia", "Alluriquin", "Valle Hermoso"],
        ["Sucumbios"] = ["Nueva Loja", "Shushufindi", "Cascales", "Cuyabeno"],
        ["Tungurahua"] = ["Ambato", "Banos", "Pelileo", "Pillaro"],
        ["Zamora Chinchipe"] = ["Zamora", "Yantzaza", "Zumbi", "Palanda"]
    };

    public static bool IsValidEcuadorianCedula(string? rawCedula)
    {
        var cedula = OnlyDigits(rawCedula);
        if (cedula.Length != 10 || cedula.Distinct().Count() == 1)
        {
            return false;
        }

        if (!int.TryParse(cedula[..2], out var provinceCode) || provinceCode is < 1 or > 24)
        {
            return false;
        }

        if (cedula[2] < '0' || cedula[2] > '5')
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < 9; index++)
        {
            var digit = cedula[index] - '0';
            if (index % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        var verifier = (10 - (sum % 10)) % 10;
        return verifier == cedula[9] - '0';
    }

    public static bool IsPersonName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !PersonNameRegex().IsMatch(normalized))
        {
            return false;
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(part => part.Length >= 3);
    }

    public static bool IsDigitsOnly(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && DigitsRegex().IsMatch(value.Trim());
    }

    public static bool IsValidEmail(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && EmailRegex().IsMatch(value.Trim());
    }

    public static string OnlyDigits(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    public static bool IsNotFuture(DateOnly? value)
    {
        return !value.HasValue || value.Value <= DateOnly.FromDateTime(DateTime.Today);
    }

    public static bool IsNotPast(DateOnly? value)
    {
        return !value.HasValue || value.Value >= DateOnly.FromDateTime(DateTime.Today);
    }

    public static bool TryParseDecimal(string? rawValue, out decimal value)
    {
        return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
               decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"^[\p{L}\s]+$")]
    private static partial Regex PersonNameRegex();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
