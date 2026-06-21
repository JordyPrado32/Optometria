using System.Net.Mail;
using System.Security.Claims;
using System.Text;
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
        policy.RequireAssertion(context =>
            !string.IsNullOrWhiteSpace(context.User.FindFirstValue(AuthClaimTypes.AuthStage))));

    options.AddPolicy("OperationalAccess", policy =>
        policy.RequireAssertion(context =>
            !string.IsNullOrWhiteSpace(context.User.FindFirstValue(AuthClaimTypes.AuthStage)) &&
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
await EnsureAuditSchemaAsync(app);

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

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, forcePasswordChange, rememberMe),
        BuildAuthenticationProperties(rememberMe));

    return Results.LocalRedirect(forcePasswordChange ? "/change-password" : "/dashboard");
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
        BuildPrincipal(nuevoUsuario, AuthStages.FullAccess, false, false),
        BuildAuthenticationProperties(false));

    return Results.LocalRedirect("/dashboard");
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
    var telefono = form["telefono"].ToString().Trim();
    var avatarUrl = form["avatar_url"].ToString().Trim();
    var fechaNacimientoRaw = form["fechaNacimiento"].ToString().Trim();

    var currentPassword = form["currentPassword"].ToString();
    var newPassword = form["newPassword"].ToString();
    var confirmNewPassword = form["confirmNewPassword"].ToString();

    if (string.IsNullOrWhiteSpace(nombres) ||
        string.IsNullOrWhiteSpace(apellidos) ||
        string.IsNullOrWhiteSpace(usuarioDb.usuario))
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

    if (!string.IsNullOrWhiteSpace(avatarUrl) && !IsValidAvatarFileName(avatarUrl))
    {
        return Results.LocalRedirect("/profile?error=El+avatar+seleccionado+no+es+valido");
    }

    usuarioDb.nombres = nombres;
    usuarioDb.apellidos = apellidos;
    usuarioDb.telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono;
    usuarioDb.avatar_url = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl;
    usuarioDb.fecha_nacimiento = fechaNacimiento;

    var hasCurrentPassword = !string.IsNullOrWhiteSpace(currentPassword);
    var hasNewPassword = !string.IsNullOrWhiteSpace(newPassword);
    var hasConfirmNewPassword = !string.IsNullOrWhiteSpace(confirmNewPassword);

    var wantsPasswordChange = hasNewPassword || hasConfirmNewPassword;

    if (wantsPasswordChange)
    {
        if (!hasCurrentPassword || !hasNewPassword || !hasConfirmNewPassword)
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

        var passwordValidationError = ValidatePassword(newPassword, usuarioDb.usuario, nombres, apellidos, usuarioDb.email ?? string.Empty);
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

app.MapGet("/exports/users.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    if (!int.TryParse(roleIdValue, out var roleId))
    {
        return Results.Forbid();
    }

    var canViewUsers = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/users"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewUsers)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var passwordState = httpContext.Request.Query["passwordState"].ToString().Trim().ToLowerInvariant();
    var roleFilterRaw = httpContext.Request.Query["roleId"].ToString().Trim();
    int? roleFilter = int.TryParse(roleFilterRaw, out var parsedRoleFilter) ? parsedRoleFilter : null;

    var usersQuery = dbContext.tbl_usuarios
        .AsNoTracking()
        .Include(u => u.id_rolNavigation)
        .Include(u => u.tbl_usuario_seguridad)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        usersQuery = usersQuery.Where(u =>
            u.usuario.ToLower().Contains(loweredSearch) ||
            u.nombres.ToLower().Contains(loweredSearch) ||
            u.apellidos.ToLower().Contains(loweredSearch) ||
            (u.email != null && u.email.ToLower().Contains(loweredSearch)) ||
            (u.telefono != null && u.telefono.ToLower().Contains(loweredSearch)));
    }

    if (roleFilter.HasValue)
    {
        usersQuery = usersQuery.Where(u => u.id_rol == roleFilter.Value);
    }

    usersQuery = status switch
    {
        "active" => usersQuery.Where(u => u.activo == true),
        "inactive" => usersQuery.Where(u => u.activo != true),
        "blocked" => usersQuery.Where(u => u.bloqueado == true),
        _ => usersQuery
    };

    var users = await usersQuery
        .OrderBy(u => u.apellidos)
        .ThenBy(u => u.nombres)
        .ToListAsync();

    users = passwordState switch
    {
        "expired" => users.Where(u => u.ultimo_cambio_password == null || DateOnly.FromDateTime(DateTime.Now) >= u.ultimo_cambio_password.Value.AddDays(90)).ToList(),
        "mustchange" => users.Where(u => u.tbl_usuario_seguridad != null && u.tbl_usuario_seguridad.must_change_password).ToList(),
        "current" => users.Where(u => u.ultimo_cambio_password != null && DateOnly.FromDateTime(DateTime.Now) < u.ultimo_cambio_password.Value.AddDays(90)).ToList(),
        _ => users
    };

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Usuario,Nombres,Apellidos,Correo,Telefono,FechaNacimiento,Rol,Activo,Bloqueado,DebeCambiarClave,UltimoCambioPassword,FechaCreacion");

    foreach (var user in users)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(user.id_usuario.ToString()),
            EscapeCsv(user.usuario),
            EscapeCsv(user.nombres),
            EscapeCsv(user.apellidos),
            EscapeCsv(user.email),
            EscapeCsv(user.telefono),
            EscapeCsv(user.fecha_nacimiento?.ToString("yyyy-MM-dd")),
            EscapeCsv(user.id_rolNavigation?.nombre),
            EscapeCsv(user.activo == true ? "Activo" : "Inactivo"),
            EscapeCsv(user.bloqueado == true ? "Si" : "No"),
            EscapeCsv(user.tbl_usuario_seguridad?.must_change_password == true ? "Si" : "No"),
            EscapeCsv(user.ultimo_cambio_password?.ToString("yyyy-MM-dd")),
            EscapeCsv(user.fecha_creacion?.ToString("yyyy-MM-dd HH:mm:ss"))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = roleId > 0 ? int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null : null,
        accion = "Exportar CSV",
        modulo = "Usuarios",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|passwordState:{passwordState}|roleId:{roleFilterRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"usuarios-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/patients.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    if (!int.TryParse(roleIdValue, out var roleId))
    {
        return Results.Forbid();
    }

    var canViewPatients = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/patients"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewPatients)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var gender = httpContext.Request.Query["gender"].ToString().Trim();
    var civilStatus = httpContext.Request.Query["civilStatus"].ToString().Trim();

    var patientsQuery = dbContext.tbl_pacientes
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p =>
            (p.codigo_paciente != null && p.codigo_paciente.ToLower().Contains(loweredSearch)) ||
            p.cedula.ToLower().Contains(loweredSearch) ||
            p.nombres.ToLower().Contains(loweredSearch) ||
            p.apellidos.ToLower().Contains(loweredSearch) ||
            (p.email != null && p.email.ToLower().Contains(loweredSearch)) ||
            (p.telefono != null && p.telefono.ToLower().Contains(loweredSearch)));
    }

    patientsQuery = status switch
    {
        "active" => patientsQuery.Where(p => p.activo == true),
        "inactive" => patientsQuery.Where(p => p.activo != true),
        _ => patientsQuery
    };

    if (!string.IsNullOrWhiteSpace(gender))
    {
        var loweredGender = gender.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.genero != null && p.genero.ToLower() == loweredGender);
    }

    if (!string.IsNullOrWhiteSpace(civilStatus))
    {
        var loweredCivilStatus = civilStatus.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.estado_civil != null && p.estado_civil.ToLower() == loweredCivilStatus);
    }

    var patients = await patientsQuery
        .OrderBy(p => p.apellidos)
        .ThenBy(p => p.nombres)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Codigo,Cedula,Nombres,Apellidos,FechaNacimiento,Edad,Genero,EstadoCivil,Ocupacion,Direccion,Telefono,Correo,Activo,FechaRegistro");

    foreach (var patient in patients)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(patient.id_paciente.ToString()),
            EscapeCsv(patient.codigo_paciente),
            EscapeCsv(patient.cedula),
            EscapeCsv(patient.nombres),
            EscapeCsv(patient.apellidos),
            EscapeCsv(patient.fecha_nacimiento?.ToString("yyyy-MM-dd")),
            EscapeCsv(patient.edad?.ToString()),
            EscapeCsv(patient.genero),
            EscapeCsv(patient.estado_civil),
            EscapeCsv(patient.ocupacion),
            EscapeCsv(patient.direccion),
            EscapeCsv(patient.telefono),
            EscapeCsv(patient.email),
            EscapeCsv(patient.activo == true ? "Activo" : "Inactivo"),
            EscapeCsv(patient.fecha_registro?.ToString("yyyy-MM-dd HH:mm:ss"))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null,
        accion = "Exportar CSV",
        modulo = "Pacientes",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|gender:{gender}|civilStatus:{civilStatus}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"pacientes-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/doctor-patient-entry.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var doctorUserId))
    {
        return Results.Forbid();
    }

    var canViewModule = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/doctor/patient-entry"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewModule)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var gender = httpContext.Request.Query["gender"].ToString().Trim();
    var civilStatus = httpContext.Request.Query["civilStatus"].ToString().Trim();

    var patientsQuery = dbContext.tbl_pacientes
        .AsNoTracking()
        .Where(p => p.tbl_consulta.Any(c => c.id_optometra == doctorUserId))
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p =>
            (p.codigo_paciente != null && p.codigo_paciente.ToLower().Contains(loweredSearch)) ||
            p.cedula.ToLower().Contains(loweredSearch) ||
            p.nombres.ToLower().Contains(loweredSearch) ||
            p.apellidos.ToLower().Contains(loweredSearch) ||
            (p.email != null && p.email.ToLower().Contains(loweredSearch)) ||
            (p.telefono != null && p.telefono.ToLower().Contains(loweredSearch)));
    }

    patientsQuery = status switch
    {
        "active" => patientsQuery.Where(p => p.activo == true),
        "inactive" => patientsQuery.Where(p => p.activo != true),
        _ => patientsQuery
    };

    if (!string.IsNullOrWhiteSpace(gender))
    {
        var loweredGender = gender.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.genero != null && p.genero.ToLower() == loweredGender);
    }

    if (!string.IsNullOrWhiteSpace(civilStatus))
    {
        var loweredCivilStatus = civilStatus.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.estado_civil != null && p.estado_civil.ToLower() == loweredCivilStatus);
    }

    var patients = await patientsQuery
        .OrderBy(p => p.apellidos)
        .ThenBy(p => p.nombres)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Codigo,Cedula,Nombres,Apellidos,FechaNacimiento,Edad,Genero,EstadoCivil,Ocupacion,Direccion,Telefono,Correo,Activo,FechaRegistro");

    foreach (var patient in patients)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(patient.id_paciente.ToString()),
            EscapeCsv(patient.codigo_paciente),
            EscapeCsv(patient.cedula),
            EscapeCsv(patient.nombres),
            EscapeCsv(patient.apellidos),
            EscapeCsv(patient.fecha_nacimiento?.ToString("yyyy-MM-dd")),
            EscapeCsv(patient.edad?.ToString()),
            EscapeCsv(patient.genero),
            EscapeCsv(patient.estado_civil),
            EscapeCsv(patient.ocupacion),
            EscapeCsv(patient.direccion),
            EscapeCsv(patient.telefono),
            EscapeCsv(patient.email),
            EscapeCsv(patient.activo == true ? "Activo" : "Inactivo"),
            EscapeCsv(patient.fecha_registro?.ToString("yyyy-MM-dd HH:mm:ss"))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = doctorUserId,
        accion = "Exportar CSV",
        modulo = "Ingresar pacientes",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|gender:{gender}|civilStatus:{civilStatus}|DoctorId={doctorUserId}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"ingresar-pacientes-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/doctor-my-patients.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var doctorUserId))
    {
        return Results.Forbid();
    }

    var canViewModule = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/doctor/my-patients"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewModule)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var gender = httpContext.Request.Query["gender"].ToString().Trim();
    var civilStatus = httpContext.Request.Query["civilStatus"].ToString().Trim();

    var patientsQuery = dbContext.tbl_pacientes
        .AsNoTracking()
        .Where(p => p.tbl_consulta.Any(c => c.id_optometra == doctorUserId))
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p =>
            (p.codigo_paciente != null && p.codigo_paciente.ToLower().Contains(loweredSearch)) ||
            p.cedula.ToLower().Contains(loweredSearch) ||
            p.nombres.ToLower().Contains(loweredSearch) ||
            p.apellidos.ToLower().Contains(loweredSearch) ||
            (p.email != null && p.email.ToLower().Contains(loweredSearch)) ||
            (p.telefono != null && p.telefono.ToLower().Contains(loweredSearch)));
    }

    patientsQuery = status switch
    {
        "active" => patientsQuery.Where(p => p.activo == true),
        "inactive" => patientsQuery.Where(p => p.activo != true),
        _ => patientsQuery
    };

    if (!string.IsNullOrWhiteSpace(gender))
    {
        var loweredGender = gender.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.genero != null && p.genero.ToLower() == loweredGender);
    }

    if (!string.IsNullOrWhiteSpace(civilStatus))
    {
        var loweredCivilStatus = civilStatus.ToLowerInvariant();
        patientsQuery = patientsQuery.Where(p => p.estado_civil != null && p.estado_civil.ToLower() == loweredCivilStatus);
    }

    var patients = await patientsQuery
        .OrderBy(p => p.apellidos)
        .ThenBy(p => p.nombres)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Codigo,Cedula,Nombres,Apellidos,FechaNacimiento,Edad,Genero,EstadoCivil,Ocupacion,Direccion,Telefono,Correo,Activo,FechaRegistro");

    foreach (var patient in patients)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(patient.id_paciente.ToString()),
            EscapeCsv(patient.codigo_paciente),
            EscapeCsv(patient.cedula),
            EscapeCsv(patient.nombres),
            EscapeCsv(patient.apellidos),
            EscapeCsv(patient.fecha_nacimiento?.ToString("yyyy-MM-dd")),
            EscapeCsv(patient.edad?.ToString()),
            EscapeCsv(patient.genero),
            EscapeCsv(patient.estado_civil),
            EscapeCsv(patient.ocupacion),
            EscapeCsv(patient.direccion),
            EscapeCsv(patient.telefono),
            EscapeCsv(patient.email),
            EscapeCsv(patient.activo == true ? "Activo" : "Inactivo"),
            EscapeCsv(patient.fecha_registro?.ToString("yyyy-MM-dd HH:mm:ss"))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = doctorUserId,
        accion = "Exportar CSV",
        modulo = "Ver mis pacientes",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|gender:{gender}|civilStatus:{civilStatus}|DoctorId={doctorUserId}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"mis-pacientes-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/laboratories.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    if (!int.TryParse(roleIdValue, out var roleId))
    {
        return Results.Forbid();
    }

    var canViewLaboratories = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/laboratories"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewLaboratories)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();

    var laboratoriesQuery = dbContext.tbl_laboratorios
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        laboratoriesQuery = laboratoriesQuery.Where(l =>
            l.nombre.ToLower().Contains(loweredSearch) ||
            (l.correo != null && l.correo.ToLower().Contains(loweredSearch)) ||
            (l.persona_contacto != null && l.persona_contacto.ToLower().Contains(loweredSearch)) ||
            (l.direccion != null && l.direccion.ToLower().Contains(loweredSearch)) ||
            (l.whatsapp != null && l.whatsapp.ToLower().Contains(loweredSearch)));
    }

    laboratoriesQuery = status switch
    {
        "active" => laboratoriesQuery.Where(l => l.activo == true),
        "inactive" => laboratoriesQuery.Where(l => l.activo != true),
        _ => laboratoriesQuery
    };

    var laboratories = await laboratoriesQuery
        .OrderBy(l => l.nombre)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Nombre,Correo,Whatsapp,PersonaContacto,Direccion,Activo");

    foreach (var laboratory in laboratories)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(laboratory.id_laboratorio.ToString()),
            EscapeCsv(laboratory.nombre),
            EscapeCsv(laboratory.correo),
            EscapeCsv(laboratory.whatsapp),
            EscapeCsv(laboratory.persona_contacto),
            EscapeCsv(laboratory.direccion),
            EscapeCsv(laboratory.activo == true ? "Activo" : "Inactivo")));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null,
        accion = "Exportar CSV",
        modulo = "Laboratorios",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"laboratorios-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/suppliers.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    if (!int.TryParse(roleIdValue, out var roleId))
    {
        return Results.Forbid();
    }

    var canViewSuppliers = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/suppliers"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewSuppliers)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();

    var suppliersQuery = dbContext.tbl_proveedors
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        suppliersQuery = suppliersQuery.Where(s =>
            s.nombre.ToLower().Contains(loweredSearch) ||
            (s.telefono != null && s.telefono.ToLower().Contains(loweredSearch)) ||
            (s.email != null && s.email.ToLower().Contains(loweredSearch)) ||
            (s.direccion != null && s.direccion.ToLower().Contains(loweredSearch)) ||
            (s.observaciones != null && s.observaciones.ToLower().Contains(loweredSearch)));
    }

    var suppliers = await suppliersQuery
        .OrderBy(s => s.nombre)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Nombre,Telefono,Correo,Direccion,Observaciones");

    foreach (var supplier in suppliers)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(supplier.id_proveedor.ToString()),
            EscapeCsv(supplier.nombre),
            EscapeCsv(supplier.telefono),
            EscapeCsv(supplier.email),
            EscapeCsv(supplier.direccion),
            EscapeCsv(supplier.observaciones)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null,
        accion = "Exportar CSV",
        modulo = "Proveedores",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"proveedores-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

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
                ('Pacientes', '/patients', 'patients', 3, 1),
                ('Ingresar pacientes', '/doctor/patient-entry', 'doctor-entry', 4, 1),
                ('Ver mis pacientes', '/doctor/my-patients', 'doctor-patients', 5, 1),
                ('Laboratorios', '/laboratories', 'lab', 6, 1),
                ('Proveedores', '/suppliers', 'suppliers', 7, 1),
                ('Usuarios', '/users', 'users', 8, 1),
                ('Roles', '/roles', 'roles', 9, 1),
                ('Menus', '/menus', 'menu', 10, 1),
                ('Registrar usuario', '/register', 'user-plus', 11, 1),
                ('Seguridad', '/setup-2fa', 'shield', 12, 1)
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

        IF EXISTS
        (
            SELECT 1
            FROM dbo.tbl_rol
            WHERE LOWER(nombre) LIKE '%doctor%'
                OR LOWER(nombre) LIKE '%medic%'
                OR LOWER(nombre) LIKE '%optomet%'
        )
        BEGIN
            MERGE dbo.tbl_rol_menu_permiso AS target
            USING
            (
                SELECT
                    r.id_rol,
                    m.id_menu,
                    CAST(1 AS BIT) AS puede_ver,
                    CAST(CASE WHEN m.ruta = '/doctor/patient-entry' THEN 1 ELSE 0 END AS BIT) AS puede_crear,
                    CAST(CASE WHEN m.ruta = '/doctor/patient-entry' THEN 1 ELSE 0 END AS BIT) AS puede_editar,
                    CAST(CASE WHEN m.ruta = '/doctor/patient-entry' THEN 1 ELSE 0 END AS BIT) AS puede_eliminar
                FROM dbo.tbl_rol r
                CROSS JOIN dbo.tbl_menu_app m
                WHERE
                    (
                        LOWER(r.nombre) LIKE '%doctor%'
                        OR LOWER(r.nombre) LIKE '%medic%'
                        OR LOWER(r.nombre) LIKE '%optomet%'
                    )
                    AND m.ruta IN ('/dashboard', '/profile', '/setup-2fa', '/doctor/patient-entry', '/doctor/my-patients')
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

static async Task EnsureAuditSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.tbl_log_auditoria', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_log_auditoria
            (
                id_log_auditoria INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_usuario INT NULL,
                accion VARCHAR(100) NULL,
                modulo VARCHAR(100) NULL,
                fecha DATETIME NOT NULL CONSTRAINT DF_tbl_log_auditoria_fecha DEFAULT (GETDATE()),
                detalle VARCHAR(MAX) NULL,
                CONSTRAINT FK_tbl_log_auditoria_tbl_usuario
                    FOREIGN KEY (id_usuario) REFERENCES dbo.tbl_usuario(id_usuario)
            );
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
        if (LooksLikeBcryptHash(storedPassword))
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, storedPassword)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        return string.Equals(storedPassword, providedPassword, StringComparison.Ordinal)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Failed;
    }
}

static bool LooksLikeBcryptHash(string storedPassword)
{
    return storedPassword.StartsWith("$2a$", StringComparison.Ordinal) ||
           storedPassword.StartsWith("$2b$", StringComparison.Ordinal) ||
           storedPassword.StartsWith("$2x$", StringComparison.Ordinal) ||
           storedPassword.StartsWith("$2y$", StringComparison.Ordinal);
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

static string EscapeCsv(string? value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    var escapedValue = value.Replace("\"", "\"\"");
    return $"\"{escapedValue}\"";
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
