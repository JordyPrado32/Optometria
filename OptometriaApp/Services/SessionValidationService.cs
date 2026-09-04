using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Services;

public sealed class SessionValidationService(IDbContextFactory<OpticaDbContext> factory)
{
    public static string CredentialVersion(string passwordHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.tbl_usuarios.AsNoTracking().Include(x => x.tbl_usuario_seguridad)
            .FirstOrDefaultAsync(x => x.id_usuario == id, cancellationToken);
        if (user == null || user.activo != true || user.bloqueado == true || user.id_rol.ToString() != principal.FindFirstValue("RoleId")) return false;
        if (CredentialVersion(user.password_hash) != principal.FindFirstValue("CredentialVersion")) return false;
        var security = user.tbl_usuario_seguridad;
        if (security?.must_change_password == true && !string.Equals(principal.FindFirstValue("ForcePasswordChange"), "True", StringComparison.OrdinalIgnoreCase)) return false;
        if (security?.two_factor_enabled == true && principal.FindFirstValue("AuthStage") == "FullAccess" && principal.FindFirstValue("TwoFactorVerified") != "True") return false;
        return true;
    }
}

public sealed class ClinicAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SessionValidationService>().IsValidAsync(state.User, token);
    }
}
