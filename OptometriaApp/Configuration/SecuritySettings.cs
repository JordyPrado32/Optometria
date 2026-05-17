namespace OptometriaApp.Configuration;

public sealed class SecuritySettings
{
    public string ApplicationName { get; set; } = "OptometriaApp";

    public string TotpIssuer { get; set; } = "OptometriaApp";

    public int TemporaryPasswordMinutesValid { get; set; } = 15;
}
