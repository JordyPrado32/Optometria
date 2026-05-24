using System.Security.Cryptography;
using OtpNet;
using QRCoder;

namespace OptometriaApp.Services;

public sealed class AuthenticatorService
{
    public string GenerateSecret()
    {
        return Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
    }

    public string GenerateTemporaryPassword(int length = 14)
    {
        const string uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lowercase = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@$%*+-_.?";
        var allChars = uppercase + lowercase + digits + symbols;

        var passwordChars = new List<char>
        {
            uppercase[RandomNumberGenerator.GetInt32(uppercase.Length)],
            lowercase[RandomNumberGenerator.GetInt32(lowercase.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };

        while (passwordChars.Count < length)
        {
            passwordChars.Add(allChars[RandomNumberGenerator.GetInt32(allChars.Length)]);
        }

        for (var i = passwordChars.Count - 1; i > 0; i--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (passwordChars[i], passwordChars[swapIndex]) = (passwordChars[swapIndex], passwordChars[i]);
        }

        return new string(passwordChars.ToArray());
    }

    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedSecret = NormalizeSecret(secret);
        var normalizedCode = new string(code.Where(char.IsDigit).ToArray());
        if (normalizedSecret.Length == 0 || normalizedCode.Length != 6)
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(normalizedSecret));
        return totp.VerifyTotp(normalizedCode, out _, new VerificationWindow(previous: 2, future: 2));
    }

    public string BuildManualEntryKey(string secret)
    {
        var normalizedSecret = NormalizeSecret(secret);
        return string.Join(" ", Enumerable.Range(0, (normalizedSecret.Length + 3) / 4)
            .Select(index => normalizedSecret.Substring(index * 4, Math.Min(4, normalizedSecret.Length - index * 4))));
    }

    public string BuildQrCodeDataUri(string issuer, string accountName, string secret)
    {
        var normalizedSecret = NormalizeSecret(secret);
        var otpAuthUrl = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={normalizedSecret}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);

        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string NormalizeSecret(string secret)
    {
        return new string(secret
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
