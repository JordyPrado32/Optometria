using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;
using OptometriaApp.Services;

namespace OptometriaApp.Api;

public static class MobileAuthEndpoints
{
    public static IEndpointRouteBuilder MapMobileAuthApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/auth")
            .WithTags("Autenticacion movil");

        group.MapPost("/login", LoginAsync)
            .WithName("MobileApiLogin")
            .WithSummary("Valida un usuario existente y emite un token para la aplicacion movil")
            .AllowAnonymous();

        group.MapPost("/login/mfa", VerifyMfaAsync)
            .WithName("MobileApiLoginMfa")
            .WithSummary("Completa el inicio de sesion mediante un codigo TOTP")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        MobileLoginRequest request,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        IPasswordHasher<tbl_usuario> passwordHasher,
        MobileTokenService tokenService,
        CancellationToken cancellationToken)
    {
        var identifier = request.Identifier?.Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
        {
            return Message(StatusCodes.Status400BadRequest, "Ingresa tu usuario o correo y la contrasena.");
        }

        var normalizedIdentifier = identifier.ToLowerInvariant();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await dbContext.tbl_usuarios
            .AsNoTracking()
            .Include(current => current.id_rolNavigation)
            .Include(current => current.tbl_usuario_seguridad)
            .FirstOrDefaultAsync(current =>
                current.usuario.ToLower() == normalizedIdentifier ||
                (current.email != null && current.email.ToLower() == normalizedIdentifier),
                cancellationToken);

        if (user is null ||
            VerifyPassword(passwordHasher, user, user.password_hash, password) == PasswordVerificationResult.Failed)
        {
            return Message(StatusCodes.Status401Unauthorized, "Usuario o contrasena incorrectos.");
        }

        if (user.activo != true)
        {
            return Message(StatusCodes.Status403Forbidden, "La cuenta se encuentra inactiva.");
        }

        if (user.bloqueado == true)
        {
            return Message(StatusCodes.Status403Forbidden, "La cuenta se encuentra bloqueada.");
        }

        var security = user.tbl_usuario_seguridad;
        if (security?.two_factor_enabled == true)
        {
            if (string.IsNullOrWhiteSpace(security.authenticator_secret))
            {
                return Message(StatusCodes.Status409Conflict, "La cuenta tiene MFA incompleto. Configuralo desde la aplicacion web.");
            }

            return Results.Ok(new MobileLoginResponse(
                RequiresMfa: true,
                MfaToken: tokenService.CreateMfaToken(user.id_usuario),
                Token: null,
                User: null));
        }

        return Results.Ok(BuildAuthenticatedResponse(user, tokenService));
    }

    private static async Task<IResult> VerifyMfaAsync(
        MobileMfaRequest request,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        AuthenticatorService authenticatorService,
        MobileTokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (!tokenService.TryValidateMfaToken(request.MfaToken ?? string.Empty, out var userId))
        {
            return Message(StatusCodes.Status401Unauthorized, "El ticket MFA vencio o no es valido. Inicia sesion nuevamente.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.tbl_usuarios
            .AsNoTracking()
            .Include(current => current.id_rolNavigation)
            .Include(current => current.tbl_usuario_seguridad)
            .FirstOrDefaultAsync(current => current.id_usuario == userId, cancellationToken);

        if (user is null || user.activo != true || user.bloqueado == true)
        {
            return Message(StatusCodes.Status401Unauthorized, "La cuenta ya no esta disponible.");
        }

        var security = user.tbl_usuario_seguridad;
        if (security?.two_factor_enabled != true ||
            string.IsNullOrWhiteSpace(security.authenticator_secret) ||
            !authenticatorService.ValidateCode(security.authenticator_secret, request.Code ?? string.Empty))
        {
            return Message(StatusCodes.Status401Unauthorized, "El codigo de verificacion no es valido.");
        }

        var response = BuildAuthenticatedResponse(user, tokenService);
        return Results.Ok(new
        {
            response.Token,
            response.User
        });
    }

    private static MobileLoginResponse BuildAuthenticatedResponse(
        tbl_usuario user,
        MobileTokenService tokenService)
    {
        return new MobileLoginResponse(
            RequiresMfa: false,
            MfaToken: null,
            Token: tokenService.CreateAccessToken(user),
            User: MobileUserResponse.FromEntity(user));
    }

    private static IResult Message(int statusCode, string message)
    {
        return Results.Json(new { message }, statusCode: statusCode);
    }

    private static PasswordVerificationResult VerifyPassword(
        IPasswordHasher<tbl_usuario> passwordHasher,
        tbl_usuario user,
        string? storedPassword,
        string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            return passwordHasher.VerifyHashedPassword(user, storedPassword, providedPassword);
        }
        catch (FormatException)
        {
            if (storedPassword.StartsWith("$2a$", StringComparison.Ordinal) ||
                storedPassword.StartsWith("$2b$", StringComparison.Ordinal) ||
                storedPassword.StartsWith("$2x$", StringComparison.Ordinal) ||
                storedPassword.StartsWith("$2y$", StringComparison.Ordinal))
            {
                return BCrypt.Net.BCrypt.Verify(providedPassword, storedPassword)
                    ? PasswordVerificationResult.Success
                    : PasswordVerificationResult.Failed;
            }

            return string.Equals(storedPassword, providedPassword, StringComparison.Ordinal)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
    }
}

public sealed record MobileLoginRequest(string? Identifier, string? Password);

public sealed record MobileMfaRequest(string? MfaToken, string? Code);

public sealed record MobileLoginResponse(
    bool RequiresMfa,
    string? MfaToken,
    string? Token,
    MobileUserResponse? User);

public sealed record MobileUserResponse(
    [property: JsonPropertyName("id_usuario")] int IdUsuario,
    [property: JsonPropertyName("id_rol")] int IdRol,
    [property: JsonPropertyName("nombres")] string Nombres,
    [property: JsonPropertyName("apellidos")] string Apellidos,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("usuario")] string Usuario,
    [property: JsonPropertyName("telefono")] string? Telefono,
    [property: JsonPropertyName("activo")] bool? Activo,
    [property: JsonPropertyName("intentos_fallidos")] int? IntentosFallidos,
    [property: JsonPropertyName("bloqueado")] bool? Bloqueado,
    [property: JsonPropertyName("ultimo_cambio_password")] string? UltimoCambioPassword,
    [property: JsonPropertyName("fecha_creacion")] string? FechaCreacion,
    [property: JsonPropertyName("fecha_nacimiento")] string? FechaNacimiento,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("security")] MobileSecurityResponse Security)
{
    public static MobileUserResponse FromEntity(tbl_usuario user)
    {
        var security = user.tbl_usuario_seguridad;
        var passwordChangedAt = user.ultimo_cambio_password?.ToString("yyyy-MM-dd");

        return new MobileUserResponse(
            user.id_usuario,
            user.id_rol,
            user.nombres,
            user.apellidos,
            user.email,
            user.usuario,
            user.telefono,
            user.activo,
            user.intentos_fallidos,
            user.bloqueado,
            passwordChangedAt,
            user.fecha_creacion?.ToString("O"),
            user.fecha_nacimiento?.ToString("yyyy-MM-dd"),
            user.avatar_url,
            new MobileSecurityResponse(
                security?.two_factor_enabled == true,
                security?.must_change_password == true,
                null,
                passwordChangedAt,
                user.ultimo_cambio_password?.AddDays(90).ToString("yyyy-MM-dd")));
    }
}

public sealed record MobileSecurityResponse(
    [property: JsonPropertyName("two_factor_enabled")] bool TwoFactorEnabled,
    [property: JsonPropertyName("must_change_password")] bool MustChangePassword,
    [property: JsonPropertyName("last_login_at")] string? LastLoginAt,
    [property: JsonPropertyName("password_changed_at")] string? PasswordChangedAt,
    [property: JsonPropertyName("password_expires_at")] string? PasswordExpiresAt);
