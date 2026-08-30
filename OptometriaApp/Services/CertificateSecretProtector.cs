using Microsoft.AspNetCore.DataProtection;

namespace OptometriaApp.Services;

public sealed class CertificateSecretProtector
{
    private const string ProtectedPrefix = "dp:v1:";
    private readonly IDataProtector protector;

    public CertificateSecretProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("OptometriaApp.SRI.CertificatePassword.v1");
    }

    public string Protect(string secret) => ProtectedPrefix + protector.Protect(secret);

    public string Unprotect(string? storedSecret)
    {
        if (string.IsNullOrEmpty(storedSecret))
        {
            return string.Empty;
        }

        return storedSecret.StartsWith(ProtectedPrefix, StringComparison.Ordinal)
            ? protector.Unprotect(storedSecret[ProtectedPrefix.Length..])
            : storedSecret;
    }
}
