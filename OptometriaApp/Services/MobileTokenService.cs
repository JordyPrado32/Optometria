using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OptometriaApp.Configuration;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class MobileTokenService(IOptions<MobileApiSettings> options)
{
    public const string TokenUseClaim = "token_use";
    public const string AccessTokenUse = "access";
    public const string MfaTokenUse = "mfa";
    private const string UserIdClaim = "uid";

    private readonly MobileApiSettings settings = options.Value;

    public string CreateAccessToken(tbl_usuario user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.id_usuario.ToString()),
            new(UserIdClaim, user.id_usuario.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.usuario),
            new(ClaimTypes.Name, user.usuario),
            new(ClaimTypes.Role, user.id_rolNavigation?.nombre ?? user.id_rol.ToString()),
            new("role_id", user.id_rol.ToString()),
            new(TokenUseClaim, AccessTokenUse)
        };

        if (!string.IsNullOrWhiteSpace(user.email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.email));
        }

        return CreateToken(claims, settings.AccessTokenMinutes);
    }

    public string CreateMfaToken(int userId)
    {
        return CreateToken(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(UserIdClaim, userId.ToString()),
                new Claim(TokenUseClaim, MfaTokenUse)
            ],
            settings.MfaTokenMinutes);
    }

    public bool TryValidateMfaToken(string token, out int userId)
    {
        userId = 0;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                token,
                CreateValidationParameters(settings),
                out _);

            return string.Equals(
                       principal.FindFirstValue(TokenUseClaim),
                       MfaTokenUse,
                       StringComparison.Ordinal) &&
                   int.TryParse(principal.FindFirstValue(UserIdClaim), out userId) &&
                   userId > 0;
        }
        catch (Exception exception) when (
            exception is SecurityTokenException or ArgumentException)
        {
            return false;
        }
    }

    public static TokenValidationParameters CreateValidationParameters(MobileApiSettings settings)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(settings.SigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    }

    private string CreateToken(IEnumerable<Claim> claims, int configuredMinutes)
    {
        var now = DateTime.UtcNow;
        var minutes = Math.Clamp(configuredMinutes, 1, 1440);
        var credentials = new SigningCredentials(
            CreateSigningKey(settings.SigningKey),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(minutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SymmetricSecurityKey CreateSigningKey(string signingKey)
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }
}
