using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptometriaApp.Components;
using OptometriaApp.Configuration;
using OptometriaApp.Data;
using OptometriaApp.Models;
using OptometriaApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<SecuritySettings>(builder.Configuration.GetSection("Security"));

builder.Services.AddDbContext<OpticaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OpticaConnection")));
builder.Services.AddDbContextFactory<OpticaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OpticaConnection")),
    ServiceLifetime.Scoped);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("FullAccess", policy =>
        policy.RequireClaim(AuthClaimTypes.AuthStage, AuthStages.FullAccess));

    options.AddPolicy("OperationalAccess", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(AuthClaimTypes.AuthStage, AuthStages.FullAccess) &&
            !string.Equals(context.User.FindFirstValue(AuthClaimTypes.ForcePasswordChange), bool.TrueString, StringComparison.OrdinalIgnoreCase)));

    options.AddPolicy("TwoFactorSetup", policy =>
        policy.RequireAssertion(context =>
        {
            var stage = context.User.FindFirstValue(AuthClaimTypes.AuthStage);
            return stage is AuthStages.TwoFactorSetupRequired or AuthStages.FullAccess;
        }));

    options.AddPolicy("TwoFactorVerification", policy =>
        policy.RequireClaim(AuthClaimTypes.AuthStage, AuthStages.TwoFactorPending));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddScoped<IPasswordHasher<tbl_usuario>, PasswordHasher<tbl_usuario>>();
builder.Services.AddScoped<AuthenticatorService>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<EmailBackgroundQueue>();
builder.Services.AddSingleton<IEmailBackgroundQueue>(sp => sp.GetRequiredService<EmailBackgroundQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailBackgroundQueue>());
builder.Services.AddScoped<MenuAccessService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await EnsureSecuritySchemaAsync(app);
await EnsureNavigationSchemaAsync(app);
await EnsureUserProfileSchemaAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/StatusCode/{0}"); app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    IPasswordHasher<tbl_usuario> passwordHasher,
    AuthenticatorService authenticatorService,
    EmailSender emailSender,
    IEmailBackgroundQueue emailQueue,
    IOptions<SecuritySettings> securityOptions) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var usuario = form["usuario"].ToString().Trim();
    var password = form["password"].ToString();
    var rememberMe = IsChecked(form["rememberMe"]);
    var usuarioNormalizado = NormalizeValue(usuario);

    if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
    {
        return Results.LocalRedirect("/?error=Completa+tu+usuario+y+contrasena");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u => u.usuario.ToLower() == usuarioNormalizado);

    if (usuarioDb is null)
    {
        return Results.LocalRedirect("/?error=Usuario+o+contrasena+incorrectos");
    }

    var seguridad = await GetOrCreateUserSecurityAsync(dbContext, usuarioDb);

    if (usuarioDb.activo == false)
    {
        return Results.LocalRedirect("/?error=Tu+cuenta+esta+inactiva");
    }

    var passwordResult = VerifyPassword(passwordHasher, usuarioDb, usuarioDb.password_hash, password);
    var temporaryPasswordResult = PasswordVerificationResult.Failed;

    if (!string.IsNullOrWhiteSpace(seguridad.recovery_password_hash) &&
        seguridad.recovery_password_expires_at.HasValue &&
        seguridad.recovery_password_expires_at.Value >= DateTime.Now)
    {
        temporaryPasswordResult = VerifyPassword(passwordHasher, usuarioDb, seguridad.recovery_password_hash, password);
    }

    if (usuarioDb.bloqueado == true)
    {
        if (temporaryPasswordResult == PasswordVerificationResult.Failed)
        {
            return Results.LocalRedirect("/?error=Tu+cuenta+esta+bloqueada.+Usa+la+clave+temporal+enviada+al+correo+o+recupera+tu+acceso");
        }

        usuarioDb.bloqueado = false;
    }

    if (passwordResult == PasswordVerificationResult.Failed && temporaryPasswordResult == PasswordVerificationResult.Failed)
    {
        usuarioDb.intentos_fallidos = (usuarioDb.intentos_fallidos ?? 0) + 1;

        if (usuarioDb.intentos_fallidos >= 3)
        {
            usuarioDb.bloqueado = true;
            seguridad.must_change_password = true;
            seguridad.updated_at = DateTime.Now;

            var blockedMessage = "Tu cuenta fue bloqueada despues de 3 intentos fallidos";

            if (!string.IsNullOrWhiteSpace(usuarioDb.email) && emailSender.IsConfigured())
            {
                var temporaryPassword = authenticatorService.GenerateTemporaryPassword();
                var minutesValid = Math.Max(5, securityOptions.Value.TemporaryPasswordMinutesValid);

                seguridad.recovery_password_hash = passwordHasher.HashPassword(usuarioDb, temporaryPassword);
                seguridad.recovery_password_expires_at = DateTime.Now.AddMinutes(minutesValid);

                await dbContext.SaveChangesAsync();

                await emailQueue.QueueTemporaryPasswordEmailAsync(
                    usuarioDb.email,
                    $"{usuarioDb.nombres} {usuarioDb.apellidos}".Trim(),
                    temporaryPassword,
                    minutesValid);

                blockedMessage = "Tu cuenta fue bloqueada y enviamos una clave temporal a tu correo para volver a acceder";
            }
            else
            {
                await dbContext.SaveChangesAsync();
                blockedMessage = "Tu cuenta fue bloqueada y no se pudo enviar la clave temporal porque falta correo o configuracion SMTP";
            }

            return Results.LocalRedirect($"/?error={Uri.EscapeDataString(blockedMessage)}");
        }

        seguridad.updated_at = DateTime.Now;
        await dbContext.SaveChangesAsync();
        return Results.LocalRedirect("/?error=Usuario+o+contrasena+incorrectos");
    }

    var usedTemporaryPassword = temporaryPasswordResult != PasswordVerificationResult.Failed;
    usuarioDb.intentos_fallidos = 0;

    if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
    {
        usuarioDb.password_hash = passwordHasher.HashPassword(usuarioDb, password);
    }

    if (usedTemporaryPassword)
    {
        seguridad.must_change_password = true;
        seguridad.recovery_password_hash = null;
        seguridad.recovery_password_expires_at = null;
    }

    if (HasPasswordExpired(usuarioDb.ultimo_cambio_password, DateTime.Now))
    {
        seguridad.must_change_password = true;
    }

    seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    var forcePasswordChange = seguridad.must_change_password;

    if (seguridad.two_factor_enabled && !string.IsNullOrWhiteSpace(seguridad.authenticator_secret))
    {
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(usuarioDb, AuthStages.TwoFactorPending, forcePasswordChange, rememberMe),
            BuildAuthenticationProperties(rememberMe));

        return Results.LocalRedirect("/verify-2fa");
    }

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.TwoFactorSetupRequired, forcePasswordChange, rememberMe),
        BuildAuthenticationProperties(rememberMe));

    return Results.LocalRedirect("/setup-2fa");
}).DisableAntiforgery();

app.MapPost("/auth/register", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    IPasswordHasher<tbl_usuario> passwordHasher) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var nombres = form["nombres"].ToString().Trim();
    var apellidos = form["apellidos"].ToString().Trim();
    var email = form["email"].ToString().Trim();
    var telefono = form["telefono"].ToString().Trim();
    var usuario = form["usuario"].ToString().Trim();
    var password = form["password"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();
    var acceptedTerms = IsChecked(form["acceptedTerms"]);
    var usuarioNormalizado = NormalizeValue(usuario);
    var emailNormalizado = NormalizeValue(email);
    var avatar_url = form["avatar_url"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(nombres) ||
        string.IsNullOrWhiteSpace(apellidos) ||
        string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(usuario) ||
        string.IsNullOrWhiteSpace(password))
    {
        return Results.LocalRedirect("/register?error=Completa+todos+los+campos+obligatorios");
    }

    if (!IsValidEmail(email))
    {
        return Results.LocalRedirect("/register?error=Ingresa+un+correo+electronico+valido");
    }

    if (password != confirmPassword)
    {
        return Results.LocalRedirect("/register?error=Las+contrasenas+no+coinciden");
    }

    if (!acceptedTerms)
    {
        return Results.LocalRedirect("/register?error=Debes+aceptar+los+terminos+y+condiciones");
    }

    var passwordValidationError = ValidatePassword(password, usuario, nombres, apellidos, email);
    if (passwordValidationError is not null)
    {
        return Results.LocalRedirect($"/register?error={Uri.EscapeDataString(passwordValidationError)}");
    }

    var existeUsuario = await dbContext.tbl_usuarios.AnyAsync(u => u.usuario.ToLower() == usuarioNormalizado);
    if (existeUsuario)
    {
        return Results.LocalRedirect("/register?error=El+nombre+de+usuario+ya+esta+registrado");
    }

    var existeEmail = await dbContext.tbl_usuarios.AnyAsync(u => u.email != null && u.email.ToLower() == emailNormalizado);
    if (existeEmail)
    {
        return Results.LocalRedirect("/register?error=El+correo+electronico+ya+esta+registrado");
    }

    var rol = await dbContext.tbl_rols.FirstOrDefaultAsync(r => r.id_rol == 2);
    if (rol is null)
    {
        return Results.LocalRedirect("/register?error=No+existe+el+rol+2+en+la+base+de+datos");
    }

    var nuevoUsuario = new tbl_usuario
    {
        id_rol = rol.id_rol,
        nombres = nombres,
        apellidos = apellidos,
        email = email,
        telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono,
        usuario = usuario,
        activo = true,
        bloqueado = false,
        intentos_fallidos = 0,
        fecha_creacion = DateTime.Now,
        ultimo_cambio_password = DateOnly.FromDateTime(DateTime.Now),
        avatar_url = string.IsNullOrWhiteSpace(avatar_url) ? null : avatar_url,
    };

    nuevoUsuario.password_hash = passwordHasher.HashPassword(nuevoUsuario, password);

    dbContext.tbl_usuarios.Add(nuevoUsuario);
    await dbContext.SaveChangesAsync();

    var seguridad = new tbl_usuario_seguridad
    {
        id_usuario = nuevoUsuario.id_usuario,
        two_factor_enabled = false,
        must_change_password = false,
        created_at = DateTime.Now,
        updated_at = DateTime.Now
    };

    dbContext.tbl_usuario_seguridad.Add(seguridad);
    await dbContext.SaveChangesAsync();

    nuevoUsuario.id_rolNavigation = rol;
    nuevoUsuario.tbl_usuario_seguridad = seguridad;

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(nuevoUsuario, AuthStages.TwoFactorSetupRequired, false, false),
        BuildAuthenticationProperties(false));

    return Results.LocalRedirect("/setup-2fa");
}).DisableAntiforgery();

app.MapPost("/auth/forgot-password", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    IPasswordHasher<tbl_usuario> passwordHasher,
    AuthenticatorService authenticatorService,
    EmailSender emailSender,
    IEmailBackgroundQueue emailQueue,
    IOptions<SecuritySettings> securityOptions) =>
{
    if (!emailSender.IsConfigured())
    {
        return Results.LocalRedirect("/forgot-password?error=SMTP+no+configurado.+Completa+la+seccion+Smtp+en+appsettings.json");
    }

    var form = await httpContext.Request.ReadFormAsync();
    var credential = form["credential"].ToString().Trim();
    var normalizedCredential = NormalizeValue(credential);

    if (string.IsNullOrWhiteSpace(credential))
    {
        return Results.LocalRedirect("/forgot-password?error=Ingresa+tu+usuario+o+correo+electronico");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u =>
            u.usuario.ToLower() == normalizedCredential ||
            (u.email != null && u.email.ToLower() == normalizedCredential));

    if (usuarioDb is null || string.IsNullOrWhiteSpace(usuarioDb.email))
    {
        return Results.LocalRedirect("/forgot-password?message=Si+la+cuenta+existe,+se+ha+programado+el+envio+de+una+clave+temporal+al+correo+registrado");
    }

    var seguridad = await GetOrCreateUserSecurityAsync(dbContext, usuarioDb);
    var temporaryPassword = authenticatorService.GenerateTemporaryPassword();
    var minutesValid = Math.Max(5, securityOptions.Value.TemporaryPasswordMinutesValid);

    seguridad.recovery_password_hash = passwordHasher.HashPassword(usuarioDb, temporaryPassword);
    seguridad.recovery_password_expires_at = DateTime.Now.AddMinutes(minutesValid);
    seguridad.updated_at = DateTime.Now;

    await dbContext.SaveChangesAsync();

    await emailQueue.QueueTemporaryPasswordEmailAsync(
        usuarioDb.email!,
        $"{usuarioDb.nombres} {usuarioDb.apellidos}".Trim(),
        temporaryPassword,
        minutesValid);

    return Results.LocalRedirect("/forgot-password?message=Si+la+cuenta+existe,+se+ha+programado+el+envio+de+una+clave+temporal+al+correo+registrado");
}).DisableAntiforgery();

app.MapPost("/auth/setup-2fa/confirm", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    AuthenticatorService authenticatorService) =>
{
    var userId = GetUserId(httpContext.User);
    if (userId is null)
    {
        return Results.LocalRedirect("/?error=Tu+sesion+expiro");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u => u.id_usuario == userId.Value);

    if (usuarioDb is null)
    {
        return Results.LocalRedirect("/?error=No+se+encontro+el+usuario");
    }

    var seguridad = await GetOrCreateUserSecurityAsync(dbContext, usuarioDb);
    var code = (await httpContext.Request.ReadFormAsync())["code"].ToString();

    if (string.IsNullOrWhiteSpace(seguridad.authenticator_secret))
    {
        return Results.LocalRedirect("/setup-2fa?error=No+hay+una+clave+de+autenticador+activa+para+configurar");
    }

    if (!authenticatorService.ValidateCode(seguridad.authenticator_secret, code))
    {
        return Results.LocalRedirect("/setup-2fa?error=El+codigo+de+Google+Authenticator+no+es+valido");
    }

    seguridad.two_factor_enabled = true;
    seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    var forcePasswordChange = seguridad.must_change_password;
    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, forcePasswordChange, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect(forcePasswordChange ? "/change-password" : "/dashboard");
}).DisableAntiforgery();

app.MapPost("/auth/verify-2fa", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    AuthenticatorService authenticatorService) =>
{
    var userId = GetUserId(httpContext.User);
    if (userId is null)
    {
        return Results.LocalRedirect("/?error=Tu+sesion+expiro");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u => u.id_usuario == userId.Value);

    if (usuarioDb is null || usuarioDb.tbl_usuario_seguridad is null || string.IsNullOrWhiteSpace(usuarioDb.tbl_usuario_seguridad.authenticator_secret))
    {
        return Results.LocalRedirect("/?error=No+se+encontro+la+configuracion+de+2+factores");
    }

    var code = (await httpContext.Request.ReadFormAsync())["code"].ToString();
    if (!authenticatorService.ValidateCode(usuarioDb.tbl_usuario_seguridad.authenticator_secret, code))
    {
        return Results.LocalRedirect("/verify-2fa?error=El+codigo+de+Google+Authenticator+no+es+valido");
    }

    usuarioDb.tbl_usuario_seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    var forcePasswordChange = usuarioDb.tbl_usuario_seguridad.must_change_password;
    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, forcePasswordChange, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect(forcePasswordChange ? "/change-password" : "/dashboard");
}).DisableAntiforgery();

app.MapPost("/auth/change-password", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    IPasswordHasher<tbl_usuario> passwordHasher) =>
{
    var userId = GetUserId(httpContext.User);
    if (userId is null)
    {
        return Results.LocalRedirect("/?error=Tu+sesion+expiro");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u => u.id_usuario == userId.Value);

    if (usuarioDb is null)
    {
        return Results.LocalRedirect("/?error=No+se+encontro+el+usuario");
    }

    var seguridad = await GetOrCreateUserSecurityAsync(dbContext, usuarioDb);
    var form = await httpContext.Request.ReadFormAsync();
    var password = form["password"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();

    if (string.IsNullOrWhiteSpace(password))
    {
        return Results.LocalRedirect("/change-password?error=Ingresa+la+nueva+contrasena");
    }

    if (password != confirmPassword)
    {
        return Results.LocalRedirect("/change-password?error=Las+contrasenas+no+coinciden");
    }

    var passwordValidationError = ValidatePassword(password, usuarioDb.usuario, usuarioDb.nombres, usuarioDb.apellidos, usuarioDb.email ?? string.Empty);
    if (passwordValidationError is not null)
    {
        return Results.LocalRedirect($"/change-password?error={Uri.EscapeDataString(passwordValidationError)}");
    }

    usuarioDb.password_hash = passwordHasher.HashPassword(usuarioDb, password);
    usuarioDb.ultimo_cambio_password = DateOnly.FromDateTime(DateTime.Now);
    seguridad.must_change_password = false;
    seguridad.recovery_password_hash = null;
    seguridad.recovery_password_expires_at = null;
    seguridad.updated_at = DateTime.Now;

    await dbContext.SaveChangesAsync();

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, false, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect("/dashboard");
}).DisableAntiforgery();

app.MapPost("/auth/profile", async (
    HttpContext httpContext,
    OpticaDbContext dbContext,
    IPasswordHasher<tbl_usuario> passwordHasher) =>
{
    var userId = GetUserId(httpContext.User);
    if (userId is null)
    {
        return Results.LocalRedirect("/?error=Tu+sesion+expiro");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u => u.id_usuario == userId.Value);

    if (usuarioDb is null)
    {
        return Results.LocalRedirect("/?error=No+se+encontro+el+usuario");
    }

    var seguridad = await GetOrCreateUserSecurityAsync(dbContext, usuarioDb);
    var form = await httpContext.Request.ReadFormAsync();

    var nombres = form["nombres"].ToString().Trim();
    var apellidos = form["apellidos"].ToString().Trim();
    var usuario = form["usuario"].ToString().Trim();
    var telefono = form["telefono"].ToString().Trim();
    var avatarUrl = form["avatar_url"].ToString().Trim();
    var fechaNacimientoRaw = form["fechaNacimiento"].ToString().Trim();

    var currentPassword = form["currentPassword"].ToString();
    var newPassword = form["newPassword"].ToString();
    var confirmNewPassword = form["confirmNewPassword"].ToString();

    if (string.IsNullOrWhiteSpace(nombres) ||
        string.IsNullOrWhiteSpace(apellidos) ||
        string.IsNullOrWhiteSpace(usuario))
    {
        return Results.LocalRedirect("/profile?error=Completa+los+campos+obligatorios+del+perfil");
    }

    DateOnly? fechaNacimiento = null;
    if (!string.IsNullOrWhiteSpace(fechaNacimientoRaw))
    {
        if (!DateOnly.TryParseExact(fechaNacimientoRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFechaNacimiento))
        {
            return Results.LocalRedirect("/profile?error=La+fecha+de+nacimiento+no+es+valida");
        }

        fechaNacimiento = parsedFechaNacimiento;
    }

    var usuarioNormalizado = NormalizeValue(usuario);
    var usuarioYaExiste = await dbContext.tbl_usuarios.AnyAsync(u =>
        u.id_usuario != usuarioDb.id_usuario &&
        u.usuario.ToLower() == usuarioNormalizado);

    if (usuarioYaExiste)
    {
        return Results.LocalRedirect("/profile?error=El+nombre+de+usuario+ya+esta+registrado");
    }

    if (!string.IsNullOrWhiteSpace(avatarUrl) && !IsValidAvatarFileName(avatarUrl))
    {
        return Results.LocalRedirect("/profile?error=El+avatar+seleccionado+no+es+valido");
    }

    usuarioDb.nombres = nombres;
    usuarioDb.apellidos = apellidos;
    usuarioDb.usuario = usuario;
    usuarioDb.telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono;
    usuarioDb.avatar_url = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
    usuarioDb.fecha_nacimiento = fechaNacimiento;

    var wantsPasswordChange =
        !string.IsNullOrWhiteSpace(currentPassword) ||
        !string.IsNullOrWhiteSpace(newPassword) ||
        !string.IsNullOrWhiteSpace(confirmNewPassword);

    if (wantsPasswordChange)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            string.IsNullOrWhiteSpace(confirmNewPassword))
        {
            return Results.LocalRedirect("/profile?error=Completa+los+campos+actual%2C+nueva+y+confirmacion+de+contrasena");
        }

        var currentPasswordResult = VerifyPassword(passwordHasher, usuarioDb, usuarioDb.password_hash, currentPassword);
        if (currentPasswordResult == PasswordVerificationResult.Failed)
        {
            return Results.LocalRedirect("/profile?error=La+contrasena+actual+no+coincide");
        }

        if (newPassword != confirmNewPassword)
        {
            return Results.LocalRedirect("/profile?error=La+nueva+contrasena+y+su+confirmacion+no+coinciden");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return Results.LocalRedirect("/profile?error=La+nueva+contrasena+debe+ser+diferente+a+la+actual");
        }

        var passwordValidationError = ValidatePassword(newPassword, usuario, nombres, apellidos, usuarioDb.email ?? string.Empty);
        if (passwordValidationError is not null)
        {
            return Results.LocalRedirect($"/profile?error={Uri.EscapeDataString(passwordValidationError)}");
        }

        usuarioDb.password_hash = passwordHasher.HashPassword(usuarioDb, newPassword);
        usuarioDb.ultimo_cambio_password = DateOnly.FromDateTime(DateTime.Now);
        seguridad.must_change_password = false;
        seguridad.recovery_password_hash = null;
        seguridad.recovery_password_expires_at = null;
    }

    seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, false, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect("/profile?message=Perfil+actualizado");
}).DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/?message=Sesion+cerrada");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task EnsureSecuritySchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.tbl_usuario_seguridad', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_usuario_seguridad
            (
                id_usuario INT NOT NULL PRIMARY KEY,
                two_factor_enabled BIT NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_two_factor_enabled DEFAULT (0),
                authenticator_secret VARCHAR(128) NULL,
                recovery_password_hash VARCHAR(255) NULL,
                recovery_password_expires_at DATETIME NULL,
                must_change_password BIT NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_must_change_password DEFAULT (0),
                created_at DATETIME NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_created_at DEFAULT (GETDATE()),
                updated_at DATETIME NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_updated_at DEFAULT (GETDATE()),
                CONSTRAINT FK_tbl_usuario_seguridad_tbl_usuario
                    FOREIGN KEY (id_usuario) REFERENCES dbo.tbl_usuario(id_usuario)
                    ON DELETE CASCADE
            );
        END
        """);
}

static async Task EnsureNavigationSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.tbl_menu_app', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_menu_app
            (
                id_menu INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                nombre VARCHAR(150) NOT NULL,
                ruta VARCHAR(200) NOT NULL,
                icono VARCHAR(100) NULL,
                orden INT NOT NULL CONSTRAINT DF_tbl_menu_app_orden DEFAULT (0),
                activo BIT NOT NULL CONSTRAINT DF_tbl_menu_app_activo DEFAULT (1),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_menu_app_fecha_creacion DEFAULT (GETDATE()),
                CONSTRAINT UQ_tbl_menu_app_ruta UNIQUE (ruta)
            );
        END;

        IF OBJECT_ID('dbo.tbl_rol_menu_permiso', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_rol_menu_permiso
            (
                id_rol_menu_permiso INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_rol INT NOT NULL,
                id_menu INT NOT NULL,
                puede_ver BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_ver DEFAULT (0),
                puede_crear BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_crear DEFAULT (0),
                puede_editar BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_editar DEFAULT (0),
                puede_eliminar BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_eliminar DEFAULT (0),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_fecha_creacion DEFAULT (GETDATE()),
                CONSTRAINT UQ_tbl_rol_menu_permiso_rol_menu UNIQUE (id_rol, id_menu),
                CONSTRAINT FK_tbl_rol_menu_permiso_tbl_rol FOREIGN KEY (id_rol) REFERENCES dbo.tbl_rol(id_rol) ON DELETE CASCADE,
                CONSTRAINT FK_tbl_rol_menu_permiso_tbl_menu_app FOREIGN KEY (id_menu) REFERENCES dbo.tbl_menu_app(id_menu) ON DELETE CASCADE
            );
        END;

        MERGE dbo.tbl_menu_app AS target
        USING
        (
            VALUES
                ('Dashboard', '/dashboard', 'dashboard', 1, 1),
                ('Mi perfil', '/profile', 'user', 2, 1),
                ('Registrar usuario', '/register', 'user-plus', 3, 1),
                ('Seguridad', '/setup-2fa', 'shield', 4, 1),
                ('Roles', '/roles', 'roles', 5, 1),
                ('Menus', '/menus', 'menu', 6, 1)
        ) AS source(nombre, ruta, icono, orden, activo)
        ON target.ruta = source.ruta
        WHEN MATCHED THEN
            UPDATE SET
                target.nombre = source.nombre,
                target.icono = source.icono,
                target.orden = source.orden,
                target.activo = source.activo
        WHEN NOT MATCHED THEN
            INSERT (nombre, ruta, icono, orden, activo)
            VALUES (source.nombre, source.ruta, source.icono, source.orden, source.activo);

        IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 1)
        BEGIN
            MERGE dbo.tbl_rol_menu_permiso AS target
            USING
            (
                SELECT
                    1 AS id_rol,
                    id_menu,
                    CAST(1 AS BIT) AS puede_ver,
                    CAST(1 AS BIT) AS puede_crear,
                    CAST(1 AS BIT) AS puede_editar,
                    CAST(1 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app
            ) AS source
            ON target.id_rol = source.id_rol AND target.id_menu = source.id_menu
            WHEN MATCHED THEN
                UPDATE SET
                    target.puede_ver = source.puede_ver,
                    target.puede_crear = source.puede_crear,
                    target.puede_editar = source.puede_editar,
                    target.puede_eliminar = source.puede_eliminar
            WHEN NOT MATCHED THEN
                INSERT (id_rol, id_menu, puede_ver, puede_crear, puede_editar, puede_eliminar)
                VALUES (source.id_rol, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);
        END;

        IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 2)
        BEGIN
            MERGE dbo.tbl_rol_menu_permiso AS target
            USING
            (
                SELECT
                    2 AS id_rol,
                    m.id_menu,
                    CAST(CASE WHEN m.ruta IN ('/dashboard', '/profile', '/setup-2fa') THEN 1 ELSE 0 END AS BIT) AS puede_ver,
                    CAST(0 AS BIT) AS puede_crear,
                    CAST(0 AS BIT) AS puede_editar,
                    CAST(0 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app m
            ) AS source
            ON target.id_rol = source.id_rol AND target.id_menu = source.id_menu
            WHEN MATCHED THEN
                UPDATE SET
                    target.puede_ver = source.puede_ver,
                    target.puede_crear = source.puede_crear,
                    target.puede_editar = source.puede_editar,
                    target.puede_eliminar = source.puede_eliminar
            WHEN NOT MATCHED THEN
                INSERT (id_rol, id_menu, puede_ver, puede_crear, puede_editar, puede_eliminar)
                VALUES (source.id_rol, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);
        END;
        """);
}

static async Task EnsureUserProfileSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF COL_LENGTH('dbo.tbl_usuario', 'fecha_nacimiento') IS NULL
        BEGIN
            ALTER TABLE dbo.tbl_usuario
            ADD fecha_nacimiento DATE NULL;
        END
        """);
}

static async Task<tbl_usuario_seguridad> GetOrCreateUserSecurityAsync(OpticaDbContext dbContext, tbl_usuario usuario)
{
    if (usuario.tbl_usuario_seguridad is not null)
    {
        return usuario.tbl_usuario_seguridad;
    }

    var seguridad = new tbl_usuario_seguridad
    {
        id_usuario = usuario.id_usuario,
        two_factor_enabled = false,
        must_change_password = false,
        created_at = DateTime.Now,
        updated_at = DateTime.Now
    };

    dbContext.tbl_usuario_seguridad.Add(seguridad);
    usuario.tbl_usuario_seguridad = seguridad;

    await dbContext.SaveChangesAsync();
    return seguridad;
}

static ClaimsPrincipal BuildPrincipal(tbl_usuario usuario, string authStage, bool forcePasswordChange, bool rememberMe)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, usuario.id_usuario.ToString()),
        new(ClaimTypes.Name, usuario.usuario),
        new("NombreCompleto", $"{usuario.nombres} {usuario.apellidos}".Trim()),
        new(AuthClaimTypes.AuthStage, authStage),
        new(AuthClaimTypes.ForcePasswordChange, forcePasswordChange.ToString()),
        new(AuthClaimTypes.RememberMe, rememberMe.ToString()),
        new(AuthClaimTypes.RoleId, usuario.id_rol.ToString())
    };

    if (!string.IsNullOrWhiteSpace(usuario.email))
    {
        claims.Add(new Claim(ClaimTypes.Email, usuario.email));
    }

    if (usuario.id_rolNavigation is not null && !string.IsNullOrWhiteSpace(usuario.id_rolNavigation.nombre))
    {
        claims.Add(new Claim(ClaimTypes.Role, usuario.id_rolNavigation.nombre));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    return new ClaimsPrincipal(identity);
}

static int? GetUserId(ClaimsPrincipal user)
{
    var rawUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    return int.TryParse(rawUserId, out var userId) ? userId : null;
}

static PasswordVerificationResult VerifyPassword(
    IPasswordHasher<tbl_usuario> passwordHasher,
    tbl_usuario usuario,
    string? storedPassword,
    string providedPassword)
{
    if (string.IsNullOrWhiteSpace(storedPassword) || string.IsNullOrEmpty(providedPassword))
    {
        return PasswordVerificationResult.Failed;
    }

    try
    {
        return passwordHasher.VerifyHashedPassword(usuario, storedPassword, providedPassword);
    }
    catch (FormatException)
    {
        return string.Equals(storedPassword, providedPassword, StringComparison.Ordinal)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Failed;
    }
}

static AuthenticationProperties BuildAuthenticationProperties(bool rememberMe)
{
    return new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
        AllowRefresh = true
    };
}

static bool IsRemembered(ClaimsPrincipal user)
{
    return string.Equals(user.FindFirstValue(AuthClaimTypes.RememberMe), bool.TrueString, StringComparison.OrdinalIgnoreCase);
}

static bool IsChecked(Microsoft.Extensions.Primitives.StringValues rawValue)
{
    var value = rawValue.ToString();
    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}

static string NormalizeValue(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

static bool IsValidEmail(string email)
{
    try
    {
        _ = new MailAddress(email);
        return true;
    }
    catch
    {
        return false;
    }
}

const int PasswordMaxAgeDays = 90;

static bool HasPasswordExpired(DateOnly? ultimoCambioPassword, DateTime now)
{
    if (!ultimoCambioPassword.HasValue)
    {
        return true;
    }

    var fechaLimite = ultimoCambioPassword.Value.AddDays(PasswordMaxAgeDays);
    var fechaActual = DateOnly.FromDateTime(now);

    return fechaActual >= fechaLimite;
}

static string? ValidatePassword(string password, string usuario, string nombres, string apellidos, string email)
{
    if (password.Length < 12)
    {
        return "La contrasena debe tener al menos 12 caracteres";
    }

    if (!Regex.IsMatch(password, "[A-Z]"))
    {
        return "La contrasena debe incluir al menos una letra mayuscula";
    }

    if (!Regex.IsMatch(password, "[a-z]"))
    {
        return "La contrasena debe incluir al menos una letra minuscula";
    }

    if (!Regex.IsMatch(password, "[0-9]"))
    {
        return "La contrasena debe incluir al menos un numero";
    }

    if (!Regex.IsMatch(password, "[^a-zA-Z0-9\\s]"))
    {
        return "La contrasena debe incluir al menos un caracter especial";
    }

    if (password.Any(char.IsWhiteSpace))
    {
        return "La contrasena no debe contener espacios";
    }

    var loweredPassword = password.ToLowerInvariant();
    var forbiddenFragments = new[]
    {
        usuario,
        nombres,
        apellidos,
        email.Contains('@') ? email[..email.IndexOf('@')] : email
    }
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Select(value => value.Trim().ToLowerInvariant())
    .Where(value => value.Length >= 3);

    foreach (var fragment in forbiddenFragments)
    {
        if (loweredPassword.Contains(fragment))
        {
            return "La contrasena no debe contener partes de tu usuario, nombre o correo";
        }
    }

    return null;
}

static bool IsValidAvatarFileName(string avatarFileName)
{
    var normalizedAvatar = avatarFileName.Trim().ToLowerInvariant();
    var validPrefixes = new[] { "l", "m", "n", "p", "r", "s", "t", "u", "v", "w" };
    var validSuffixes = new[] { "sin_lentes", "con_lentes" };

    return validPrefixes.Any(prefix =>
        validSuffixes.Any(suffix =>
            normalizedAvatar == $"{prefix}_{suffix}.png"));
}

static class AuthStages
{
    public const string FullAccess = "FullAccess";
    public const string TwoFactorPending = "TwoFactorPending";
    public const string TwoFactorSetupRequired = "TwoFactorSetupRequired";
}

static class AuthClaimTypes
{
    public const string AuthStage = "AuthStage";
    public const string ForcePasswordChange = "ForcePasswordChange";
    public const string RememberMe = "RememberMe";
    public const string RoleId = "RoleId";
}
