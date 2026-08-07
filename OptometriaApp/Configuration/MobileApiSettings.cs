namespace OptometriaApp.Configuration;

public sealed class MobileApiSettings
{
    public const string SectionName = "MobileApi";

    public string Issuer { get; set; } = "OptometriaApp";

    public string Audience { get; set; } = "InfraMovil";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 120;

    public int MfaTokenMinutes { get; set; } = 5;
}
