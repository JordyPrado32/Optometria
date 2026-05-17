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

        var normalizedCode = new string(code.Where(char.IsDigit).ToArray());
        if (normalizedCode.Length != 6)
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(normalizedCode, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public string BuildManualEntryKey(string secret)
    {
        return string.Join(" ", Enumerable.Range(0, (secret.Length + 3) / 4)
            .Select(index => secret.Substring(index * 4, Math.Min(4, secret.Length - index * 4))));
    }

    public string BuildQrCodeDataUri(string issuer, string accountName, string secret)
    {
        var otpAuthUrl = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);

        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
