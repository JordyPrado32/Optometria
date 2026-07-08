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
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var rawUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(rawUserId, out var userId) || userId <= 0)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<OpticaDbContext>();
                var userExists = await dbContext.tbl_usuarios
                    .AsNoTracking()
                    .AnyAsync(u => u.id_usuario == userId && u.activo == true);

                if (!userExists)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });
builder.Services.AddScoped<IPasswordHasher<tbl_usuario>, PasswordHasher<tbl_usuario>>();
builder.Services.AddScoped<AuthenticatorService>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<EmailBackgroundQueue>();
builder.Services.AddSingleton<IEmailBackgroundQueue>(sp => sp.GetRequiredService<EmailBackgroundQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailBackgroundQueue>());
builder.Services.AddScoped<MenuAccessService>();
builder.Services.AddScoped<KardexService>();
builder.Services.AddScoped<BillingDraftService>();
builder.Services.AddScoped<AccountStatementService>();
builder.Services.AddHostedService<AppointmentReminderService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await EnsureSecuritySchemaAsync(app);
await EnsureNavigationSchemaAsync(app);
await EnsureUserProfileSchemaAsync(app);
await EnsureAuditSchemaAsync(app);
await EnsureElectronicBillingSchemaAsync(app);
await EnsureProductSchemaAsync(app);
await EnsureSupplierSchemaAsync(app);
await EnsureProcurementSchemaAsync(app);
await EnsureAppointmentSchemaAsync(app);
await EnsureClinicalHistorySchemaAsync(app);

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

    return Results.LocalRedirect(forcePasswordChange ? "/cambiar-contrasena" : "/dashboard");
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
        return Results.LocalRedirect("/registro?error=Completa+todos+los+campos+obligatorios");
    }

    if (!IsValidEmail(email))
    {
        return Results.LocalRedirect("/registro?error=Ingresa+un+correo+electronico+valido");
    }

    if (password != confirmPassword)
    {
        return Results.LocalRedirect("/registro?error=Las+contrasenas+no+coinciden");
    }

    if (!acceptedTerms)
    {
        return Results.LocalRedirect("/registro?error=Debes+aceptar+los+terminos+y+condiciones");
    }

    var passwordValidationError = ValidatePassword(password, usuario, nombres, apellidos, email);
    if (passwordValidationError is not null)
    {
        return Results.LocalRedirect($"/registro?error={Uri.EscapeDataString(passwordValidationError)}");
    }

    var existeUsuario = await dbContext.tbl_usuarios.AnyAsync(u => u.usuario.ToLower() == usuarioNormalizado);
    if (existeUsuario)
    {
        return Results.LocalRedirect("/registro?error=El+nombre+de+usuario+ya+esta+registrado");
    }

    var existeEmail = await dbContext.tbl_usuarios.AnyAsync(u => u.email != null && u.email.ToLower() == emailNormalizado);
    if (existeEmail)
    {
        return Results.LocalRedirect("/registro?error=El+correo+electronico+ya+esta+registrado");
    }

    var rol = await dbContext.tbl_rols.FirstOrDefaultAsync(r => r.id_rol == 2);
    if (rol is null)
    {
        return Results.LocalRedirect("/registro?error=No+existe+el+rol+2+en+la+base+de+datos");
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
        return Results.LocalRedirect("/recuperar-contrasena?error=SMTP+no+configurado.+Completa+la+seccion+Smtp+en+appsettings.json");
    }

    var form = await httpContext.Request.ReadFormAsync();
    var credential = form["credential"].ToString().Trim();
    var normalizedCredential = NormalizeValue(credential);

    if (string.IsNullOrWhiteSpace(credential))
    {
        return Results.LocalRedirect("/recuperar-contrasena?error=Ingresa+tu+usuario+o+correo+electronico");
    }

    var usuarioDb = await dbContext.tbl_usuarios
        .AsTracking()
        .Include(u => u.tbl_usuario_seguridad)
        .FirstOrDefaultAsync(u =>
            u.usuario.ToLower() == normalizedCredential ||
            (u.email != null && u.email.ToLower() == normalizedCredential));

    if (usuarioDb is null || string.IsNullOrWhiteSpace(usuarioDb.email))
    {
        return Results.LocalRedirect("/?message=Si+la+cuenta+existe,+se+ha+programado+el+envio+de+una+clave+temporal+al+correo+registrado");
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

    return Results.LocalRedirect("/?message=Si+la+cuenta+existe,+se+ha+programado+el+envio+de+una+clave+temporal+al+correo+registrado");
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
        return Results.LocalRedirect("/configurar-2fa?error=No+hay+una+clave+de+autenticador+activa+para+configurar");
    }

    if (!authenticatorService.ValidateCode(seguridad.authenticator_secret, code))
    {
        return Results.LocalRedirect("/configurar-2fa?error=El+codigo+de+Google+Authenticator+no+es+valido");
    }

    seguridad.two_factor_enabled = true;
    seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    var forcePasswordChange = seguridad.must_change_password;
    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, forcePasswordChange, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect(forcePasswordChange ? "/cambiar-contrasena" : "/dashboard");
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
        return Results.LocalRedirect("/verificar-2fa?error=El+codigo+de+Google+Authenticator+no+es+valido");
    }

    usuarioDb.tbl_usuario_seguridad.updated_at = DateTime.Now;
    await dbContext.SaveChangesAsync();

    var forcePasswordChange = usuarioDb.tbl_usuario_seguridad.must_change_password;
    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        BuildPrincipal(usuarioDb, AuthStages.FullAccess, forcePasswordChange, IsRemembered(httpContext.User)),
        BuildAuthenticationProperties(IsRemembered(httpContext.User)));

    return Results.LocalRedirect(forcePasswordChange ? "/cambiar-contrasena" : "/dashboard");
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
        return Results.LocalRedirect("/cambiar-contrasena?error=Ingresa+la+nueva+contrasena");
    }

    if (password != confirmPassword)
    {
        return Results.LocalRedirect("/cambiar-contrasena?error=Las+contrasenas+no+coinciden");
    }

    var passwordValidationError = ValidatePassword(password, usuarioDb.usuario, usuarioDb.nombres, usuarioDb.apellidos, usuarioDb.email ?? string.Empty);
    if (passwordValidationError is not null)
    {
        return Results.LocalRedirect($"/cambiar-contrasena?error={Uri.EscapeDataString(passwordValidationError)}");
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
        return Results.LocalRedirect("/perfil?error=Completa+los+campos+obligatorios+del+perfil");
    }

    DateOnly? fechaNacimiento = null;
    if (!string.IsNullOrWhiteSpace(fechaNacimientoRaw))
    {
        if (!DateOnly.TryParseExact(fechaNacimientoRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFechaNacimiento))
        {
            return Results.LocalRedirect("/perfil?error=La+fecha+de+nacimiento+no+es+valida");
        }

        fechaNacimiento = parsedFechaNacimiento;
    }

    if (!string.IsNullOrWhiteSpace(avatarUrl) && !IsValidAvatarFileName(avatarUrl))
    {
        return Results.LocalRedirect("/perfil?error=El+avatar+seleccionado+no+es+valido");
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
            return Results.LocalRedirect("/perfil?error=Completa+los+campos+actual%2C+nueva+y+confirmacion+de+contrasena");
        }

        var currentPasswordResult = VerifyPassword(passwordHasher, usuarioDb, usuarioDb.password_hash, currentPassword);
        if (currentPasswordResult == PasswordVerificationResult.Failed)
        {
            return Results.LocalRedirect("/perfil?error=La+contrasena+actual+no+coincide");
        }

        if (newPassword != confirmNewPassword)
        {
            return Results.LocalRedirect("/perfil?error=La+nueva+contrasena+y+su+confirmacion+no+coinciden");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return Results.LocalRedirect("/perfil?error=La+nueva+contrasena+debe+ser+diferente+a+la+actual");
        }

        var passwordValidationError = ValidatePassword(newPassword, usuarioDb.usuario, nombres, apellidos, usuarioDb.email ?? string.Empty);
        if (passwordValidationError is not null)
        {
            return Results.LocalRedirect($"/perfil?error={Uri.EscapeDataString(passwordValidationError)}");
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

    return Results.LocalRedirect("/perfil?message=Perfil+actualizado");
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/usuarios"),
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/pacientes"),
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/doctor/ingresar-pacientes"),
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/doctor/mis-pacientes"),
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

app.MapGet("/exports/appointments.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var roleName = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var currentUserId))
    {
        return Results.Forbid();
    }

    var canViewAppointments = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/citas"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewAppointments)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var period = httpContext.Request.Query["period"].ToString().Trim().ToLowerInvariant();
    var status = httpContext.Request.Query["status"].ToString().Trim();
    var doctorIdRaw = httpContext.Request.Query["doctorId"].ToString().Trim();
    var dateFromRaw = httpContext.Request.Query["dateFrom"].ToString().Trim();
    var dateToRaw = httpContext.Request.Query["dateTo"].ToString().Trim();
    var selectedDoctorId = int.TryParse(doctorIdRaw, out var parsedDoctorId) ? parsedDoctorId : 0;
    var isAdmin = roleId == 1 || roleName.Contains("admin", StringComparison.OrdinalIgnoreCase);
    var isDoctor = roleName.Contains("doctor", StringComparison.OrdinalIgnoreCase)
        || roleName.Contains("medic", StringComparison.OrdinalIgnoreCase)
        || roleName.Contains("optomet", StringComparison.OrdinalIgnoreCase);

    var appointmentsQuery = dbContext.tbl_citas
        .AsNoTracking()
        .Include(c => c.id_medicoNavigation).ThenInclude(m => m.id_usuarioNavigation)
        .Include(c => c.id_pacienteNavigation)
        .Include(c => c.id_estadoNavigation)
        .AsQueryable();

    if (isAdmin)
    {
    }
    else if (isDoctor)
    {
        appointmentsQuery = appointmentsQuery.Where(c => c.id_medicoNavigation.id_usuario == currentUserId);
    }
    else
    {
        appointmentsQuery = appointmentsQuery.Where(c =>
            c.id_pacienteNavigation.id_usuario == currentUserId ||
            c.id_pacienteNavigation.id_usuario_registro == currentUserId);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        appointmentsQuery = appointmentsQuery.Where(c =>
            c.id_pacienteNavigation.nombres.ToLower().Contains(loweredSearch) ||
            c.id_pacienteNavigation.apellidos.ToLower().Contains(loweredSearch) ||
            c.id_medicoNavigation.id_usuarioNavigation.nombres.ToLower().Contains(loweredSearch) ||
            c.id_medicoNavigation.id_usuarioNavigation.apellidos.ToLower().Contains(loweredSearch) ||
            (c.motivo_cita != null && c.motivo_cita.ToLower().Contains(loweredSearch)) ||
            (c.tipo_cita != null && c.tipo_cita.ToLower().Contains(loweredSearch)) ||
            (c.id_estadoNavigation != null && c.id_estadoNavigation.nombre_estado.ToLower().Contains(loweredSearch)));
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        var loweredStatus = status.ToLowerInvariant();
        appointmentsQuery = appointmentsQuery.Where(c => c.id_estadoNavigation != null && c.id_estadoNavigation.nombre_estado.ToLower() == loweredStatus);
    }

    if (selectedDoctorId > 0)
    {
        appointmentsQuery = appointmentsQuery.Where(c => c.id_medico == selectedDoctorId);
    }

    if (DateOnly.TryParse(dateFromRaw, out var dateFrom))
    {
        appointmentsQuery = appointmentsQuery.Where(c => c.fecha_cita >= dateFrom);
    }

    if (DateOnly.TryParse(dateToRaw, out var dateTo))
    {
        appointmentsQuery = appointmentsQuery.Where(c => c.fecha_cita <= dateTo);
    }

    var today = DateOnly.FromDateTime(DateTime.Today);
    appointmentsQuery = period switch
    {
        "past" => appointmentsQuery.Where(c => c.fecha_cita < today),
        "today" => appointmentsQuery.Where(c => c.fecha_cita == today),
        "all" => appointmentsQuery,
        _ => appointmentsQuery.Where(c => c.fecha_cita >= today)
    };

    var appointments = await appointmentsQuery
        .OrderBy(c => c.fecha_cita)
        .ThenBy(c => c.hora_inicio)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Fecha,HoraInicio,HoraFin,Estado,Tipo,Motivo,Paciente,Medico,RazonCancelacion,Creada,Actualizada");

    foreach (var appointment in appointments)
    {
        var doctorName = $"{appointment.id_medicoNavigation.id_usuarioNavigation.nombres} {appointment.id_medicoNavigation.id_usuarioNavigation.apellidos}".Trim();
        var patientName = $"{appointment.id_pacienteNavigation.nombres} {appointment.id_pacienteNavigation.apellidos}".Trim();

        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(appointment.id_cita.ToString()),
            EscapeCsv(appointment.fecha_cita.ToString("yyyy-MM-dd")),
            EscapeCsv(appointment.hora_inicio.ToString("HH:mm")),
            EscapeCsv(appointment.hora_fin.ToString("HH:mm")),
            EscapeCsv(appointment.id_estadoNavigation?.nombre_estado),
            EscapeCsv(appointment.tipo_cita),
            EscapeCsv(appointment.motivo_cita),
            EscapeCsv(patientName),
            EscapeCsv(doctorName),
            EscapeCsv(appointment.razon_cancelacion),
            EscapeCsv(appointment.fecha_creacion?.ToString("yyyy-MM-dd HH:mm:ss")),
            EscapeCsv(appointment.fecha_actualizacion?.ToString("yyyy-MM-dd HH:mm:ss"))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = currentUserId,
        accion = "Exportar CSV",
        modulo = "Citas",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|period:{period}|status:{status}|doctorId:{doctorIdRaw}|dateFrom:{dateFromRaw}|dateTo:{dateToRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"citas-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/appointment-availability.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var roleName = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var currentUserId))
    {
        return Results.Forbid();
    }

    var canViewModule = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/disponibilidad-medica"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewModule)
    {
        return Results.Forbid();
    }

    var requestedDoctorId = int.TryParse(httpContext.Request.Query["doctorId"].ToString(), out var parsedDoctorId) ? parsedDoctorId : 0;
    var requestedDay = byte.TryParse(httpContext.Request.Query["day"].ToString(), out var parsedDay) ? parsedDay : (byte)0;
    var requestedBlockType = httpContext.Request.Query["blockType"].ToString().Trim();
    var isAdmin = roleId == 1 || roleName.Contains("admin", StringComparison.OrdinalIgnoreCase);
    var isDoctor = roleName.Contains("doctor", StringComparison.OrdinalIgnoreCase)
        || roleName.Contains("medic", StringComparison.OrdinalIgnoreCase)
        || roleName.Contains("optomet", StringComparison.OrdinalIgnoreCase);

    var doctorId = requestedDoctorId;
    if (!isAdmin && isDoctor)
    {
        doctorId = await dbContext.tbl_medico
            .AsNoTracking()
            .Where(m => m.id_usuario == currentUserId)
            .Select(m => m.id_medico)
            .FirstOrDefaultAsync();
    }

    if (doctorId <= 0)
    {
        return Results.File(
            Encoding.UTF8.GetBytes("Tipo,Detalle\n"),
            "text/csv; charset=utf-8",
            $"disponibilidad-medica-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    var availabilityQuery = dbContext.tbl_disponibilidad_medico
        .AsNoTracking()
        .Where(x => x.id_medico == doctorId);

    if (requestedDay > 0)
    {
        availabilityQuery = availabilityQuery.Where(x => x.dia_semana == requestedDay);
    }

    var blockQuery = dbContext.tbl_bloqueo_horarios
        .AsNoTracking()
        .Where(x => x.id_medico == doctorId && x.activo == true);

    if (!string.IsNullOrWhiteSpace(requestedBlockType))
    {
        var loweredBlockType = requestedBlockType.ToLowerInvariant();
        blockQuery = blockQuery.Where(x => x.tipo_bloqueo != null && x.tipo_bloqueo.ToLower() == loweredBlockType);
    }

    var doctor = await dbContext.tbl_medico
        .AsNoTracking()
        .Include(m => m.id_usuarioNavigation)
        .FirstOrDefaultAsync(m => m.id_medico == doctorId);

    var availabilityRows = await availabilityQuery
        .OrderBy(x => x.dia_semana)
        .ThenBy(x => x.hora_inicio)
        .ToListAsync();

    var blocks = await blockQuery
        .OrderByDescending(x => x.fecha_inicio)
        .ToListAsync();

    var doctorName = doctor is null
        ? doctorId.ToString()
        : $"{doctor.id_usuarioNavigation.nombres} {doctor.id_usuarioNavigation.apellidos}".Trim();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Tipo,Medico,Dia,HoraInicio,HoraFin,DescansoInicio,DescansoFin,FechaInicio,FechaFin,Clase,Razon,Observaciones");

    foreach (var row in availabilityRows)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv("Disponibilidad"),
            EscapeCsv(doctorName),
            EscapeCsv(row.nombre_dia),
            EscapeCsv(row.hora_inicio.ToString("HH:mm")),
            EscapeCsv(row.hora_fin.ToString("HH:mm")),
            EscapeCsv(row.hora_descanso_inicio?.ToString("HH:mm")),
            EscapeCsv(row.hora_descanso_fin?.ToString("HH:mm")),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(row.observaciones)));
    }

    foreach (var block in blocks)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv("Bloqueo"),
            EscapeCsv(doctorName),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(null),
            EscapeCsv(block.fecha_inicio.ToString("yyyy-MM-dd")),
            EscapeCsv(block.fecha_fin.ToString("yyyy-MM-dd")),
            EscapeCsv(block.tipo_bloqueo),
            EscapeCsv(block.razon_bloqueo),
            EscapeCsv(null)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = currentUserId,
        accion = "Exportar CSV",
        modulo = "Disponibilidad medica",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=doctorId:{doctorId}|day:{requestedDay}|blockType:{requestedBlockType}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"disponibilidad-medica-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/laboratorios"),
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
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/proveedores"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewSuppliers)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();

    var suppliersQuery = dbContext.tbl_proveedors
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        suppliersQuery = suppliersQuery.Where(s =>
            s.nombre.ToLower().Contains(loweredSearch) ||
            (s.razon_social != null && s.razon_social.ToLower().Contains(loweredSearch)) ||
            (s.ruc != null && s.ruc.ToLower().Contains(loweredSearch)) ||
            (s.telefono != null && s.telefono.ToLower().Contains(loweredSearch)) ||
            (s.email != null && s.email.ToLower().Contains(loweredSearch)) ||
            (s.direccion != null && s.direccion.ToLower().Contains(loweredSearch)) ||
            (s.observaciones != null && s.observaciones.ToLower().Contains(loweredSearch)));
    }

    if (status is "active" or "inactive")
    {
        var expectedStatus = status == "active";
        suppliersQuery = suppliersQuery.Where(s => (s.es_activo ?? true) == expectedStatus);
    }

    var suppliers = await suppliersQuery
        .OrderBy(s => s.nombre)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Nombre,RazonSocial,Ruc,Telefono,Correo,Ciudad,Provincia,CondicionPago,LimiteCredito,SaldoPendiente,Estado");

    foreach (var supplier in suppliers)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(supplier.id_proveedor.ToString()),
            EscapeCsv(supplier.nombre),
            EscapeCsv(supplier.razon_social),
            EscapeCsv(supplier.ruc),
            EscapeCsv(supplier.telefono),
            EscapeCsv(supplier.email),
            EscapeCsv(supplier.ciudad),
            EscapeCsv(supplier.provincia),
            EscapeCsv(supplier.condicion_pago),
            EscapeCsv(supplier.limite_credito?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(supplier.saldo_pendiente?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv((supplier.es_activo ?? true) ? "Activo" : "Inactivo")));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null,
        accion = "Exportar CSV",
        modulo = "Proveedores",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"proveedores-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/clients.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var actorUserId = GetUserId(httpContext.User);
    if (!int.TryParse(roleIdValue, out var roleId) || actorUserId is null)
    {
        return Results.Forbid();
    }

    var canViewClients = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/clientes"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewClients)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var clientType = httpContext.Request.Query["type"].ToString().Trim();

    var clientsQuery = dbContext.clients
        .AsNoTracking()
        .Where(c => c.id_usuario_creacion == actorUserId.Value)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        clientsQuery = clientsQuery.Where(c =>
            c.razon_social.ToLower().Contains(loweredSearch) ||
            c.numero_identificacion.ToLower().Contains(loweredSearch) ||
            (c.nombres != null && c.nombres.ToLower().Contains(loweredSearch)) ||
            (c.apellidos != null && c.apellidos.ToLower().Contains(loweredSearch)) ||
            (c.ciudad != null && c.ciudad.ToLower().Contains(loweredSearch)) ||
            (c.correo_electronico != null && c.correo_electronico.ToLower().Contains(loweredSearch)));
    }

    if (status is "active" or "inactive")
    {
        var expectedStatus = status == "active";
        clientsQuery = clientsQuery.Where(c => c.estado == expectedStatus);
    }

    if (!string.IsNullOrWhiteSpace(clientType))
    {
        clientsQuery = clientsQuery.Where(c => c.tipo_cliente == clientType);
    }

    var clients = await clientsQuery
        .OrderBy(c => c.razon_social)
        .ThenBy(c => c.numero_identificacion)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,TipoCliente,TipoIdentificacion,NumeroIdentificacion,RazonSocial,Nombres,Apellidos,Ciudad,Provincia,Telefono,Correo,CondicionPago,Estado");

    foreach (var client in clients)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(client.cliente_id.ToString()),
            EscapeCsv(client.tipo_cliente),
            EscapeCsv(client.tipo_identificacion),
            EscapeCsv(client.numero_identificacion),
            EscapeCsv(client.razon_social),
            EscapeCsv(client.nombres),
            EscapeCsv(client.apellidos),
            EscapeCsv(client.ciudad),
            EscapeCsv(client.provincia),
            EscapeCsv(client.telefono),
            EscapeCsv(client.correo_electronico),
            EscapeCsv(client.condicion_pago),
            EscapeCsv(client.estado ? "Activo" : "Inactivo")));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = actorUserId,
        accion = "Exportar CSV",
        modulo = "Clientes",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|type:{clientType}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"clientes-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/emisor.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var actorUserId = GetUserId(httpContext.User);
    if (!int.TryParse(roleIdValue, out var roleId) || actorUserId is null)
    {
        return Results.Forbid();
    }

    var canViewEmisor = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/emisor"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewEmisor)
    {
        return Results.Forbid();
    }

    var emisores = await dbContext.emisor
        .AsNoTracking()
        .Where(e => e.id_usuario_creacion == actorUserId.Value)
        .OrderBy(e => e.razon_social)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Ruc,RazonSocial,NombreComercial,TipoPersona,TipoIdentificacion,Correo,Telefono,Establecimiento,PuntoEmision,Estado");

    foreach (var issuer in emisores)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(issuer.emisor_id.ToString()),
            EscapeCsv(issuer.ruc),
            EscapeCsv(issuer.razon_social),
            EscapeCsv(issuer.nombre_comercial),
            EscapeCsv(issuer.tipo_persona),
            EscapeCsv(issuer.tipo_identificacion),
            EscapeCsv(issuer.correo),
            EscapeCsv(issuer.telefono),
            EscapeCsv(issuer.establecimiento_codigo),
            EscapeCsv(issuer.punto_emision_codigo),
            EscapeCsv(issuer.estado ? "Activo" : "Inactivo")));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = actorUserId,
        accion = "Exportar CSV",
        modulo = "Emisor",
        fecha = DateTime.Now,
        detalle = "Tipo=Exportacion"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"emisor-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/products.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    if (!int.TryParse(roleIdValue, out var roleId))
    {
        return Results.Forbid();
    }

    var canViewProducts = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/productos"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canViewProducts)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim().ToLowerInvariant();
    var state = httpContext.Request.Query["state"].ToString().Trim();
    var type = httpContext.Request.Query["type"].ToString().Trim();
    var categoryIdRaw = httpContext.Request.Query["categoryId"].ToString().Trim();
    var supplierIdRaw = httpContext.Request.Query["supplierId"].ToString().Trim();

    var productsQuery = dbContext.tbl_productos
        .AsNoTracking()
        .Include(p => p.id_categoriaNavigation)
        .Include(p => p.id_proveedorNavigation)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        productsQuery = productsQuery.Where(p =>
            p.codigo_producto.ToLower().Contains(loweredSearch) ||
            p.nombre_producto.ToLower().Contains(loweredSearch) ||
            (p.descripcion != null && p.descripcion.ToLower().Contains(loweredSearch)) ||
            (p.codigo_barras != null && p.codigo_barras.ToLower().Contains(loweredSearch)) ||
            (p.marca != null && p.marca.ToLower().Contains(loweredSearch)) ||
            (p.modelo != null && p.modelo.ToLower().Contains(loweredSearch)) ||
            (p.etiquetas != null && p.etiquetas.ToLower().Contains(loweredSearch)));
    }

    if (status is "active" or "inactive")
    {
        var expectedStatus = status == "active";
        productsQuery = productsQuery.Where(p => (p.activo ?? false) == expectedStatus);
    }

    if (!string.IsNullOrWhiteSpace(state))
    {
        productsQuery = productsQuery.Where(p => p.estado_producto == state);
    }

    if (!string.IsNullOrWhiteSpace(type))
    {
        productsQuery = productsQuery.Where(p => (p.tipo_item ?? "Producto") == type);
    }

    if (int.TryParse(categoryIdRaw, out var categoryId) && categoryId > 0)
    {
        productsQuery = productsQuery.Where(p => p.id_categoria == categoryId);
    }

    if (int.TryParse(supplierIdRaw, out var supplierId) && supplierId > 0)
    {
        productsQuery = productsQuery.Where(p => p.id_proveedor == supplierId);
    }

    var products = await productsQuery
        .OrderBy(p => p.nombre_producto)
        .ThenBy(p => p.codigo_producto)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Codigo,Nombre,TipoItem,Categoria,Proveedor,PrecioCosto,PrecioVenta,StockActual,StockMinimo,StockMaximo,Estado,EstadoProducto,CodigoBarras,Marca,Modelo");

    foreach (var product in products)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(product.id_producto.ToString()),
            EscapeCsv(product.codigo_producto),
            EscapeCsv(product.nombre_producto),
            EscapeCsv(product.tipo_item ?? "Producto"),
            EscapeCsv(product.id_categoriaNavigation?.nombre),
            EscapeCsv(product.id_proveedorNavigation?.nombre),
            EscapeCsv(product.precio_costo?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(product.precio_venta.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(product.stock_actual?.ToString()),
            EscapeCsv(product.stock_minimo?.ToString()),
            EscapeCsv(product.stock_maximo?.ToString()),
            EscapeCsv((product.activo ?? false) ? "Activo" : "Inactivo"),
            EscapeCsv(product.estado_producto),
            EscapeCsv(product.codigo_barras),
            EscapeCsv(product.marca),
            EscapeCsv(product.modelo)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) ? actorUserId : null,
        accion = "Exportar CSV",
        modulo = "Productos",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|status:{status}|state:{state}|type:{type}|categoryId:{categoryIdRaw}|supplierId:{supplierIdRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"productos-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/purchase-orders.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var canView = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/ordenes-de-compra"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canView)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var state = httpContext.Request.Query["state"].ToString().Trim();
    var supplierIdRaw = httpContext.Request.Query["supplierId"].ToString().Trim();

    var ordersQuery = dbContext.tbl_orden_compra
        .AsNoTracking()
        .Include(x => x.id_proveedorNavigation)
        .Include(x => x.tbl_detalle_orden_compra)
        .Where(x => x.id_usuario_solicita == userId)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        ordersQuery = ordersQuery.Where(x =>
            x.numero_orden.ToLower().Contains(loweredSearch) ||
            (x.referencia_externa != null && x.referencia_externa.ToLower().Contains(loweredSearch)) ||
            x.id_proveedorNavigation.nombre.ToLower().Contains(loweredSearch));
    }

    if (!string.IsNullOrWhiteSpace(state))
    {
        ordersQuery = ordersQuery.Where(x => x.estado_orden == state);
    }

    if (int.TryParse(supplierIdRaw, out var supplierId) && supplierId > 0)
    {
        ordersQuery = ordersQuery.Where(x => x.id_proveedor == supplierId);
    }

    var orders = await ordersQuery.OrderByDescending(x => x.fecha_orden).ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,NumeroOrden,Proveedor,FechaOrden,Estado,CondicionPago,Moneda,Total,Lineas,ReferenciaExterna");

    foreach (var item in orders)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(item.id_orden_compra.ToString()),
            EscapeCsv(item.numero_orden),
            EscapeCsv(item.id_proveedorNavigation?.nombre),
            EscapeCsv(item.fecha_orden?.ToString("yyyy-MM-dd HH:mm")),
            EscapeCsv(item.estado_orden),
            EscapeCsv(item.condicion_pago),
            EscapeCsv(item.moneda),
            EscapeCsv(item.total?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.tbl_detalle_orden_compra.Count.ToString()),
            EscapeCsv(item.referencia_externa)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = userId,
        accion = "Exportar CSV",
        modulo = "OrdenesCompra",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|state:{state}|supplierId:{supplierIdRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"ordenes-compra-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/purchase-receptions.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var canView = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/recepciones-de-compra"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canView)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var state = httpContext.Request.Query["state"].ToString().Trim();
    var orderIdRaw = httpContext.Request.Query["orderId"].ToString().Trim();

    var receptionsQuery = dbContext.tbl_recepcion_compra
        .AsNoTracking()
        .Include(x => x.id_orden_compraNavigation)
        .Where(x => x.id_usuario_recibe == userId)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        receptionsQuery = receptionsQuery.Where(x =>
            x.numero_recepcion.ToLower().Contains(loweredSearch) ||
            (x.numero_guia_remision != null && x.numero_guia_remision.ToLower().Contains(loweredSearch)) ||
            x.id_orden_compraNavigation.numero_orden.ToLower().Contains(loweredSearch));
    }

    if (!string.IsNullOrWhiteSpace(state))
    {
        receptionsQuery = receptionsQuery.Where(x => x.estado_recepcion == state);
    }

    if (int.TryParse(orderIdRaw, out var orderId) && orderId > 0)
    {
        receptionsQuery = receptionsQuery.Where(x => x.id_orden_compra == orderId);
    }

    var receptions = await receptionsQuery.OrderByDescending(x => x.fecha_recepcion).ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,NumeroRecepcion,OrdenCompra,FechaRecepcion,Estado,CantidadRecibida,GuiaRemision,Observaciones");

    foreach (var item in receptions)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(item.id_recepcion.ToString()),
            EscapeCsv(item.numero_recepcion),
            EscapeCsv(item.id_orden_compraNavigation?.numero_orden),
            EscapeCsv(item.fecha_recepcion?.ToString("yyyy-MM-dd HH:mm")),
            EscapeCsv(item.estado_recepcion),
            EscapeCsv(item.cantidad_total_recibida?.ToString()),
            EscapeCsv(item.numero_guia_remision),
            EscapeCsv(item.observaciones_recepcion)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = userId,
        accion = "Exportar CSV",
        modulo = "RecepcionesCompra",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|state:{state}|orderId:{orderIdRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"recepciones-compra-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/purchase-liquidations.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var canView = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/liquidaciones-de-compra"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canView)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var state = httpContext.Request.Query["state"].ToString().Trim();
    var orderIdRaw = httpContext.Request.Query["orderId"].ToString().Trim();

    var liquidationsQuery = dbContext.tbl_liquidacion_compra
        .AsNoTracking()
        .Include(x => x.id_orden_compraNavigation)
        .Where(x => x.id_usuario_registro == userId)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        liquidationsQuery = liquidationsQuery.Where(x =>
            x.numero_liquidacion.ToLower().Contains(loweredSearch) ||
            (x.numero_factura != null && x.numero_factura.ToLower().Contains(loweredSearch)) ||
            (x.numero_autorizacion != null && x.numero_autorizacion.ToLower().Contains(loweredSearch)) ||
            x.id_orden_compraNavigation.numero_orden.ToLower().Contains(loweredSearch));
    }

    if (!string.IsNullOrWhiteSpace(state))
    {
        liquidationsQuery = liquidationsQuery.Where(x => x.estado_liquidacion == state);
    }

    if (int.TryParse(orderIdRaw, out var orderId) && orderId > 0)
    {
        liquidationsQuery = liquidationsQuery.Where(x => x.id_orden_compra == orderId);
    }

    var liquidations = await liquidationsQuery.OrderByDescending(x => x.fecha_liquidacion).ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,NumeroLiquidacion,OrdenCompra,FechaLiquidacion,Factura,Autorizacion,Estado,Subtotal,Impuesto,Total,Pagado,Pendiente");

    foreach (var item in liquidations)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(item.id_liquidacion_compra.ToString()),
            EscapeCsv(item.numero_liquidacion),
            EscapeCsv(item.id_orden_compraNavigation?.numero_orden),
            EscapeCsv(item.fecha_liquidacion?.ToString("yyyy-MM-dd HH:mm")),
            EscapeCsv(item.numero_factura),
            EscapeCsv(item.numero_autorizacion),
            EscapeCsv(item.estado_liquidacion),
            EscapeCsv(item.subtotal?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.impuesto_total?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.total?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.saldo_pagado?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.saldo_pendiente?.ToString("0.00", CultureInfo.InvariantCulture))));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = userId,
        accion = "Exportar CSV",
        modulo = "LiquidacionesCompra",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|state:{state}|orderId:{orderIdRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"liquidaciones-compra-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/inventories.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var canView = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/inventarios"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canView)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var type = httpContext.Request.Query["type"].ToString().Trim();
    var productIdRaw = httpContext.Request.Query["productId"].ToString().Trim();

    var movementsQuery = dbContext.tbl_movimiento_inventarios
        .AsNoTracking()
        .Include(x => x.id_productoNavigation)
        .Where(x => x.id_usuario == userId)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        movementsQuery = movementsQuery.Where(x =>
            x.id_productoNavigation.nombre_producto.ToLower().Contains(loweredSearch) ||
            (x.comprobante_numero != null && x.comprobante_numero.ToLower().Contains(loweredSearch)) ||
            (x.tipo_documento_referencia != null && x.tipo_documento_referencia.ToLower().Contains(loweredSearch)) ||
            (x.numero_lote != null && x.numero_lote.ToLower().Contains(loweredSearch)));
    }

    if (!string.IsNullOrWhiteSpace(type))
    {
        movementsQuery = movementsQuery.Where(x => x.tipo_movimiento == type);
    }

    if (int.TryParse(productIdRaw, out var productId) && productId > 0)
    {
        movementsQuery = movementsQuery.Where(x => x.id_producto == productId);
    }

    var movements = await movementsQuery.OrderByDescending(x => x.fecha_movimiento).ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Fecha,Producto,Tipo,Cantidad,StockAnterior,StockResultante,Documento,Referencia,Lote,MetodoValuacion");

    foreach (var item in movements)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(item.id_movimiento_inventario.ToString()),
            EscapeCsv(item.fecha_movimiento?.ToString("yyyy-MM-dd HH:mm")),
            EscapeCsv(item.id_productoNavigation?.nombre_producto),
            EscapeCsv(item.tipo_movimiento),
            EscapeCsv(item.cantidad.ToString()),
            EscapeCsv(item.stock_anterior?.ToString()),
            EscapeCsv(item.stock_resultante?.ToString()),
            EscapeCsv(item.comprobante_numero),
            EscapeCsv(item.tipo_documento_referencia),
            EscapeCsv(item.numero_lote),
            EscapeCsv(item.metodo_valuacion)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = userId,
        accion = "Exportar CSV",
        modulo = "Inventarios",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|type:{type}|productId:{productIdRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"inventarios-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapGet("/exports/kardex.csv", async (
    HttpContext httpContext,
    OpticaDbContext dbContext) =>
{
    var roleIdValue = httpContext.User.FindFirstValue(AuthClaimTypes.RoleId);
    var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(roleIdValue, out var roleId) || !int.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var canView = await dbContext.tbl_rol_menu_permisos
        .AsNoTracking()
        .Where(p => p.id_rol == roleId && p.puede_ver)
        .Join(
            dbContext.tbl_menu_apps.AsNoTracking().Where(m => m.ruta == "/kardex"),
            permission => permission.id_menu,
            menu => menu.id_menu,
            (_, _) => true)
        .AnyAsync();

    if (!canView)
    {
        return Results.Forbid();
    }

    var search = httpContext.Request.Query["search"].ToString().Trim();
    var type = httpContext.Request.Query["type"].ToString().Trim();
    var status = httpContext.Request.Query["status"].ToString().Trim();
    var productIdRaw = httpContext.Request.Query["productId"].ToString().Trim();
    var fromRaw = httpContext.Request.Query["from"].ToString().Trim();
    var toRaw = httpContext.Request.Query["to"].ToString().Trim();

    var userProductIds = await dbContext.tbl_movimiento_inventarios
        .AsNoTracking()
        .Where(x => x.id_usuario == userId)
        .Select(x => x.id_producto)
        .Distinct()
        .ToListAsync();

    var kardexQuery = dbContext.tbl_kardex
        .AsNoTracking()
        .Include(x => x.id_productoNavigation)
        .Where(x => userProductIds.Contains(x.id_producto))
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var loweredSearch = search.ToLowerInvariant();
        kardexQuery = kardexQuery.Where(x =>
            x.id_productoNavigation.nombre_producto.ToLower().Contains(loweredSearch) ||
            (x.comprobante_numero != null && x.comprobante_numero.ToLower().Contains(loweredSearch)) ||
            (x.tipo_referencia != null && x.tipo_referencia.ToLower().Contains(loweredSearch)) ||
            (x.numero_lote != null && x.numero_lote.ToLower().Contains(loweredSearch)) ||
            (x.descripcion_movimiento != null && x.descripcion_movimiento.ToLower().Contains(loweredSearch)));
    }

    if (!string.IsNullOrWhiteSpace(type))
    {
        kardexQuery = kardexQuery.Where(x => x.tipo_movimiento == type);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        kardexQuery = kardexQuery.Where(x => x.estado_kardex == status);
    }

    if (int.TryParse(productIdRaw, out var productId) && productId > 0)
    {
        kardexQuery = kardexQuery.Where(x => x.id_producto == productId);
    }

    if (DateOnly.TryParse(fromRaw, out var fromDate))
    {
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        kardexQuery = kardexQuery.Where(x => x.fecha_movimiento >= fromDateTime);
    }

    if (DateOnly.TryParse(toRaw, out var toDate))
    {
        var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue);
        kardexQuery = kardexQuery.Where(x => x.fecha_movimiento <= toDateTime);
    }

    var rows = await kardexQuery
        .OrderByDescending(x => x.fecha_movimiento)
        .ThenByDescending(x => x.id_kardex)
        .ToListAsync();

    var csvBuilder = new StringBuilder();
    csvBuilder.AppendLine("Id,Fecha,Producto,Movimiento,Cantidad,CostoUnitario,CostoTotal,StockAnterior,StockNuevo,SaldoNuevo,PromedioPonderado,Referencia,Estado");

    foreach (var item in rows)
    {
        csvBuilder.AppendLine(string.Join(",",
            EscapeCsv(item.id_kardex.ToString()),
            EscapeCsv(item.fecha_movimiento?.ToString("yyyy-MM-dd HH:mm")),
            EscapeCsv(item.id_productoNavigation?.nombre_producto),
            EscapeCsv(item.tipo_movimiento),
            EscapeCsv(item.cantidad_movimiento.ToString()),
            EscapeCsv(item.costo_unitario?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.costo_total?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.stock_anterior?.ToString()),
            EscapeCsv(item.stock_nuevo?.ToString()),
            EscapeCsv(item.saldo_nuevo_dinero?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.precio_promedio_ponderado?.ToString("0.00", CultureInfo.InvariantCulture)),
            EscapeCsv(item.comprobante_numero ?? item.tipo_referencia),
            EscapeCsv(item.estado_kardex)));
    }

    dbContext.tbl_log_auditoria.Add(new tbl_log_auditoria
    {
        id_usuario = userId,
        accion = "Exportar CSV",
        modulo = "Kardex",
        fecha = DateTime.Now,
        detalle = $"Tipo=Exportacion; Filtros=search:{search}|type:{type}|status:{status}|productId:{productIdRaw}|from:{fromRaw}|to:{toRaw}"
    });

    await dbContext.SaveChangesAsync();

    return Results.File(
        Encoding.UTF8.GetBytes(csvBuilder.ToString()),
        "text/csv; charset=utf-8",
        $"kardex-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
}).RequireAuthorization("FullAccess");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/account-statements/{accountId:int}/print", async (
    int accountId,
    ClaimsPrincipal user,
    AccountStatementService statementService,
    CancellationToken cancellationToken) =>
{
    var currentUserId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;
    var currentRoleId = int.TryParse(user.FindFirstValue("RoleId"), out var roleId) ? roleId : 0;
    var statement = await statementService.BuildAsync(accountId, currentUserId, currentRoleId, cancellationToken);
    if (statement is null)
    {
        return Results.NotFound();
    }

    var html = statementService.BuildPrintableHtml(statement);
    return Results.Content(html, "text/html; charset=utf-8");
}).RequireAuthorization("OperationalAccess");

app.MapGet("/exports/account-statements/{accountId:int}.pdf", async (
    int accountId,
    ClaimsPrincipal user,
    AccountStatementService statementService,
    CancellationToken cancellationToken) =>
{
    var currentUserId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;
    var currentRoleId = int.TryParse(user.FindFirstValue("RoleId"), out var roleId) ? roleId : 0;
    var statement = await statementService.BuildAsync(accountId, currentUserId, currentRoleId, cancellationToken);
    if (statement is null)
    {
        return Results.NotFound();
    }

    var pdf = statementService.BuildPdf(statement);
    var fileName = $"{Regex.Replace(statement.InvoiceNumber, "[^A-Za-z0-9_-]", "_")}-estado-cuenta.pdf";
    return Results.File(pdf, "application/pdf", fileName);
}).RequireAuthorization("OperationalAccess");

// Automatic database menu route updates to Spanish at startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpticaDbContext>>();
        using var context = await contextFactory.CreateDbContextAsync();

        var routeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "/patients", "/pacientes" },
            { "/doctor/patient-entry", "/doctor/ingresar-pacientes" },
            { "/doctor/my-patients", "/doctor/mis-pacientes" },
            { "/doctor/clinical-history", "/doctor/historia-clinica" },
            { "/appointments", "/citas" },
            { "/appointment-availability", "/disponibilidad-medica" },
            { "/doctors", "/doctores" },
            { "/laboratories", "/laboratorios" },
            { "/suppliers", "/proveedores" },
            { "/clients", "/clientes" },
            { "/products", "/productos" },
            { "/product-categories", "/categorias-de-productos" },
            { "/purchase-orders", "/ordenes-de-compra" },
            { "/purchase-receptions", "/recepciones-de-compra" },
            { "/purchase-liquidations", "/liquidaciones-de-compra" },
            { "/inventories", "/inventarios" },
            { "/invoices", "/facturas" },
            { "/my-invoices", "/mis-facturas" },
            { "/my-credit-notes", "/mis-notas-de-credito" },
            { "/accounts-receivable", "/cuentas-por-cobrar" },
            { "/users", "/usuarios" },
            { "/profile", "/perfil" },
            { "/setup-2fa", "/configurar-2fa" }
        };

        var menus = await context.tbl_menu_apps.ToListAsync();
        bool anyChanged = false;
        foreach (var menu in menus)
        {
            if (!string.IsNullOrWhiteSpace(menu.ruta) && routeMappings.TryGetValue(menu.ruta.Trim(), out var newRoute))
            {
                var existingSpanishMenu = menus.FirstOrDefault(m =>
                    m.id_menu != menu.id_menu &&
                    !string.IsNullOrWhiteSpace(m.ruta) &&
                    string.Equals(m.ruta.Trim(), newRoute, StringComparison.OrdinalIgnoreCase));

                if (existingSpanishMenu is not null)
                {
                    if (menu.activo)
                    {
                        menu.activo = false;
                        anyChanged = true;
                    }
                }
                else if (menu.ruta != newRoute)
                {
                    menu.ruta = newRoute;
                    anyChanged = true;
                }
            }
        }
        if (anyChanged)
        {
            await context.SaveChangesAsync();
        }

        // IMPORTANT: Also execute SQL to sync the role menu permissions for any roles that lost access
        // because of the mismatch of English routes.
        // We'll update the records in tbl_rol_menu_permiso to make sure the Admin role (id_rol = 1) has all permissions,
        // and role 2 has the correct Spanish permissions.
        
        // 1. Admin Role (id_rol = 1) gets all permissions on all menus
        await context.Database.ExecuteSqlRawAsync(@"
            IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 1)
            BEGIN
                MERGE dbo.tbl_rol_menu_permiso AS target
                USING (
                    SELECT 1 AS id_rol, id_menu, 1 AS puede_ver, 1 AS puede_crear, 1 AS puede_editar, 1 AS puede_eliminar
                    FROM dbo.tbl_menu_app
                ) AS source
                ON target.id_rol = source.id_rol AND target.id_menu = source.id_menu
                WHEN MATCHED THEN
                    UPDATE SET target.puede_ver = 1, target.puede_crear = 1, target.puede_editar = 1, target.puede_eliminar = 1
                WHEN NOT MATCHED THEN
                    INSERT (id_rol, id_menu, puede_ver, puede_crear, puede_editar, puede_eliminar)
                    VALUES (1, source.id_menu, 1, 1, 1, 1);
            END;
        ");

        // 2. Role 2 gets correct Spanish permissions
        await context.Database.ExecuteSqlRawAsync(@"
            IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 2)
            BEGIN
                MERGE dbo.tbl_rol_menu_permiso AS target
                USING (
                    SELECT
                        2 AS id_rol,
                        m.id_menu,
                        CAST(CASE WHEN m.ruta IN ('/dashboard', '/perfil', '/configurar-2fa', '/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_ver,
                        CAST(CASE WHEN m.ruta IN ('/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_crear,
                        CAST(CASE WHEN m.ruta IN ('/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_editar,
                        CAST(CASE WHEN m.ruta = '/citas' THEN 1 ELSE 0 END AS BIT) AS puede_eliminar
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
                    VALUES (2, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);
            END;
        ");

        // 3. Doctor/Medic/Optometrist role gets correct Spanish permissions
        await context.Database.ExecuteSqlRawAsync(@"
            IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE LOWER(nombre) LIKE '%doctor%' OR LOWER(nombre) LIKE '%medic%' OR LOWER(nombre) LIKE '%optomet%')
            BEGIN
                -- First find the role IDs
                DECLARE @roles TABLE (id_rol INT);
                INSERT INTO @roles (id_rol)
                SELECT id_rol FROM dbo.tbl_rol WHERE LOWER(nombre) LIKE '%doctor%' OR LOWER(nombre) LIKE '%medic%' OR LOWER(nombre) LIKE '%optomet%';

                -- Run merge for each role
                DECLARE @curr_rol INT;
                DECLARE rol_cursor CURSOR FOR SELECT id_rol FROM @roles;
                OPEN rol_cursor;
                FETCH NEXT FROM rol_cursor INTO @curr_rol;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    MERGE dbo.tbl_rol_menu_permiso AS target
                    USING (
                        SELECT
                            @curr_rol AS id_rol,
                            m.id_menu,
                            CAST(1 AS BIT) AS puede_ver,
                            CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores') THEN 1 ELSE 0 END AS BIT) AS puede_crear,
                            CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores') THEN 1 ELSE 0 END AS BIT) AS puede_editar,
                            CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/citas') THEN 1 ELSE 0 END AS BIT) AS puede_eliminar
                        FROM dbo.tbl_menu_app m
                        WHERE m.ruta IN ('/dashboard', '/perfil', '/configurar-2fa', '/doctor/ingresar-pacientes', '/doctor/mis-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores')
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
                        VALUES (@curr_rol, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);

                    FETCH NEXT FROM rol_cursor INTO @curr_rol;
                END;
                CLOSE rol_cursor;
                DEALLOCATE rol_cursor;
            END;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error al actualizar las rutas del menú y permisos: {ex.Message}");
    }
}

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
                ruta VARCHAR(200) NULL,
                icono VARCHAR(100) NULL,
                orden INT NOT NULL CONSTRAINT DF_tbl_menu_app_orden DEFAULT (0),
                id_menu_padre INT NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_menu_app_activo DEFAULT (1),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_menu_app_fecha_creacion DEFAULT (GETDATE()),
                CONSTRAINT FK_tbl_menu_app_padre FOREIGN KEY (id_menu_padre) REFERENCES dbo.tbl_menu_app(id_menu)
            );
        END;

        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_tbl_menu_app_ruta')
        BEGIN
            ALTER TABLE dbo.tbl_menu_app DROP CONSTRAINT UQ_tbl_menu_app_ruta;
        END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_menu_app_ruta' AND object_id = OBJECT_ID('dbo.tbl_menu_app'))
        BEGIN
            DROP INDEX UQ_tbl_menu_app_ruta ON dbo.tbl_menu_app;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.tbl_menu_app')
              AND name = 'ruta'
              AND is_nullable = 0
        )
        BEGIN
            ALTER TABLE dbo.tbl_menu_app ALTER COLUMN ruta VARCHAR(200) NULL;
        END;

        UPDATE dbo.tbl_menu_app
        SET ruta = NULL
        WHERE ruta = '';

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_menu_app_ruta_not_null' AND object_id = OBJECT_ID('dbo.tbl_menu_app'))
        BEGIN
            CREATE UNIQUE INDEX IX_tbl_menu_app_ruta_not_null
            ON dbo.tbl_menu_app(ruta)
            WHERE ruta IS NOT NULL AND ruta <> '';
        END;

        IF COL_LENGTH('dbo.tbl_menu_app', 'id_menu_padre') IS NULL
        BEGIN
            ALTER TABLE dbo.tbl_menu_app ADD id_menu_padre INT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_menu_app_padre')
        BEGIN
            ALTER TABLE dbo.tbl_menu_app ADD CONSTRAINT FK_tbl_menu_app_padre FOREIGN KEY (id_menu_padre) REFERENCES dbo.tbl_menu_app(id_menu);
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

        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_menu_app)
        BEGIN
            MERGE dbo.tbl_menu_app AS target
            USING
            (
                VALUES
                    ('Dashboard', '/dashboard', 'dashboard', 1, 1),
                    ('Mi perfil', '/perfil', 'user', 2, 1),
                    ('Pacientes', '/pacientes', 'patients', 3, 1),
                    ('Ingresar pacientes', '/doctor/ingresar-pacientes', 'doctor-entry', 4, 1),
                    ('Ver mis pacientes', '/doctor/mis-pacientes', 'doctor-patients', 5, 1),
                    ('Historia clinica', '/doctor/historia-clinica', 'journal-medical', 5, 1),
                    ('Citas y turnos', '/citas', 'calendar-check', 6, 1),
                    ('Disponibilidad medica', '/disponibilidad-medica', 'calendar-availability', 7, 1),
                    ('Medicos', '/doctores', 'doctor-profile', 8, 1),
                    ('Laboratorios', '/laboratorios', 'lab', 9, 1),
                    ('Proveedores', '/proveedores', 'suppliers', 10, 1),
                    ('Productos', '/productos', 'products', 11, 1),
                    ('Categorias de productos', '/categorias-de-productos', 'tags', 12, 1),
                    ('Ordenes de compra', '/ordenes-de-compra', 'purchase-orders', 14, 1),
                    ('Recepciones de compra', '/recepciones-de-compra', 'purchase-receptions', 15, 1),
                    ('Liquidaciones de compra', '/liquidaciones-de-compra', 'purchase-liquidations', 16, 1),
                    ('Inventarios', '/inventarios', 'inventories', 17, 1),
                    ('Kardex', '/kardex', 'kardex', 18, 1),
                    ('Clientes', '/clientes', 'clients', 19, 1),
                    ('Emisor', '/emisor', 'issuer', 20, 1),
                    ('Facturas', '/facturas', 'invoice', 21, 1),
                    ('Mis facturas', '/mis-facturas', 'receipt', 22, 1),
                    ('Mis notas de credito', '/mis-notas-de-credito', 'arrow-counterclockwise', 23, 1),
                    ('Cuentas por cobrar', '/cuentas-por-cobrar', 'cash-coin', 24, 1),
                    ('Usuarios', '/usuarios', 'users', 25, 1),
                    ('Roles', '/roles', 'roles', 26, 1),
                    ('Menus', '/menus', 'menu', 27, 1),
                    ('Registrar usuario', '/registro', 'user-plus', 28, 1),
                    ('Seguridad', '/configurar-2fa', 'shield', 29, 1)
            ) AS source(nombre, ruta, icono, orden, activo)
            ON target.ruta = source.ruta
            WHEN MATCHED THEN
                UPDATE SET
                    target.nombre = source.nombre,
                    target.icono = source.icono
            WHEN NOT MATCHED THEN
                INSERT (nombre, ruta, icono, orden, activo)
                VALUES (source.nombre, source.ruta, source.icono, source.orden, source.activo);
        END;

        EXEC(N'
            DECLARE @comprasMenuId INT = (SELECT TOP 1 id_menu FROM dbo.tbl_menu_app WHERE nombre = ''Compras'' OR ruta = ''/compras'' ORDER BY CASE WHEN nombre = ''Compras'' THEN 0 ELSE 1 END);
            DECLARE @hasPurchaseChildren BIT = CASE WHEN EXISTS (
                SELECT 1 FROM dbo.tbl_menu_app
                WHERE ruta IN (''/ordenes-de-compra'', ''/recepciones-de-compra'', ''/liquidaciones-de-compra'', ''/inventarios'', ''/kardex'')
            ) THEN 1 ELSE 0 END;

            IF @comprasMenuId IS NULL
               AND @hasPurchaseChildren = 1
            BEGIN
                INSERT INTO dbo.tbl_menu_app (nombre, ruta, icono, orden, activo)
                VALUES (''Compras'', NULL, ''purchases'', 13, 1);

                SET @comprasMenuId = SCOPE_IDENTITY();
            END
            ELSE IF @comprasMenuId IS NOT NULL
            BEGIN
                UPDATE dbo.tbl_menu_app
                SET nombre = ''Compras'',
                    ruta = NULL,
                    icono = ''purchases''
                WHERE id_menu = @comprasMenuId;
            END;

            IF @comprasMenuId IS NOT NULL
            BEGIN
                UPDATE dbo.tbl_menu_app
                SET id_menu_padre = @comprasMenuId
                WHERE ruta IN (''/ordenes-de-compra'', ''/recepciones-de-compra'', ''/liquidaciones-de-compra'', ''/inventarios'', ''/kardex'')
                  AND id_menu_padre IS NULL;
            END;
        ');

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
                    CAST(CASE WHEN m.ruta IN ('/dashboard', '/perfil', '/configurar-2fa', '/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_ver,
                    CAST(CASE WHEN m.ruta IN ('/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_crear,
                    CAST(CASE WHEN m.ruta IN ('/citas', '/facturas', '/mis-facturas', '/mis-notas-de-credito', '/cuentas-por-cobrar') THEN 1 ELSE 0 END AS BIT) AS puede_editar,
                    CAST(CASE WHEN m.ruta = '/citas' THEN 1 ELSE 0 END AS BIT) AS puede_eliminar
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
                    CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores') THEN 1 ELSE 0 END AS BIT) AS puede_crear,
                    CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores') THEN 1 ELSE 0 END AS BIT) AS puede_editar,
                    CAST(CASE WHEN m.ruta IN ('/doctor/ingresar-pacientes', '/citas') THEN 1 ELSE 0 END AS BIT) AS puede_eliminar
                FROM dbo.tbl_rol r
                CROSS JOIN dbo.tbl_menu_app m
                WHERE
                    (
                        LOWER(r.nombre) LIKE '%doctor%'
                        OR LOWER(r.nombre) LIKE '%medic%'
                        OR LOWER(r.nombre) LIKE '%optomet%'
                    )
                    AND m.ruta IN ('/dashboard', '/perfil', '/configurar-2fa', '/doctor/ingresar-pacientes', '/doctor/mis-pacientes', '/doctor/historia-clinica', '/citas', '/disponibilidad-medica', '/doctores')
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

static async Task EnsureAppointmentSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF COL_LENGTH('dbo.tbl_paciente', 'id_usuario') IS NULL
        BEGIN
            ALTER TABLE dbo.tbl_paciente ADD id_usuario INT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_paciente_tbl_usuario')
        BEGIN
            ALTER TABLE dbo.tbl_paciente
            ADD CONSTRAINT FK_tbl_paciente_tbl_usuario FOREIGN KEY (id_usuario) REFERENCES dbo.tbl_usuario(id_usuario);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_paciente_id_usuario' AND object_id = OBJECT_ID('dbo.tbl_paciente'))
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_paciente_id_usuario ON dbo.tbl_paciente(id_usuario) WHERE id_usuario IS NOT NULL;
        END;

        IF OBJECT_ID('dbo.tbl_medico', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_medico
            (
                id_medico INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_usuario INT NOT NULL UNIQUE,
                numero_licencia VARCHAR(50) NOT NULL UNIQUE,
                especialidad VARCHAR(100) NULL,
                cedula_profesional VARCHAR(50) NULL,
                institucion_egreso VARCHAR(200) NULL,
                anio_egreso INT NULL,
                telefono_consultorio VARCHAR(20) NULL,
                biografia VARCHAR(MAX) NULL,
                certificaciones VARCHAR(MAX) NULL,
                idiomas VARCHAR(200) NULL,
                precio_consulta_base DECIMAL(10,2) NULL,
                descuento_porcentaje DECIMAL(5,2) NULL,
                aceptar_citas_telefonicas BIT NOT NULL CONSTRAINT DF_tbl_medico_tel DEFAULT (1),
                aceptar_citas_presenciales BIT NOT NULL CONSTRAINT DF_tbl_medico_pre DEFAULT (1),
                duracion_consulta_minutos INT NOT NULL CONSTRAINT DF_tbl_medico_duracion DEFAULT (30),
                observaciones VARCHAR(MAX) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_medico_activo DEFAULT (1),
                fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_medico_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_medico_fecha_actualizacion DEFAULT (GETDATE()),
                usuario_creacion VARCHAR(100) NULL,
                usuario_actualizacion VARCHAR(100) NULL,
                CONSTRAINT fk_medico_usuario FOREIGN KEY (id_usuario) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF OBJECT_ID('dbo.tbl_disponibilidad_medico', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_disponibilidad_medico
            (
                id_disponibilidad INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_medico INT NOT NULL,
                dia_semana TINYINT NOT NULL,
                nombre_dia VARCHAR(20) NULL,
                hora_inicio TIME NOT NULL,
                hora_fin TIME NOT NULL,
                permitir_descanso_medio_dia BIT NOT NULL CONSTRAINT DF_tbl_disp_desc DEFAULT (0),
                hora_descanso_inicio TIME NULL,
                hora_descanso_fin TIME NULL,
                disponible BIT NOT NULL CONSTRAINT DF_tbl_disp_estado DEFAULT (1),
                observaciones VARCHAR(500) NULL,
                fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_disp_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_disp_fecha_actualizacion DEFAULT (GETDATE()),
                usuario_actualizacion VARCHAR(100) NULL,
                CONSTRAINT fk_disponibilidad_medico FOREIGN KEY (id_medico) REFERENCES dbo.tbl_medico(id_medico) ON DELETE CASCADE ON UPDATE CASCADE,
                CONSTRAINT chk_disponibilidad_horas CHECK (hora_inicio < hora_fin)
            );
        END;

        IF OBJECT_ID('dbo.tbl_estado_cita', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_estado_cita
            (
                id_estado INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                nombre_estado VARCHAR(50) NOT NULL UNIQUE,
                descripcion VARCHAR(255) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_estado_cita_activo DEFAULT (1)
            );
        END;

        MERGE dbo.tbl_estado_cita AS target
        USING
        (
            VALUES
                ('Programada', 'Cita agendada pero no confirmada'),
                ('Confirmada', 'Cita confirmada por el paciente o el sistema'),
                ('Realizada', 'Cita ya se llevo a cabo'),
                ('Cancelada', 'Cita cancelada por el paciente o medico'),
                ('No presento', 'Paciente no se presento a la cita'),
                ('Reprogramada', 'Cita fue reprogramada para otra fecha'),
                ('Pendiente pago', 'Cita realizada pero pendiente de pago')
        ) AS source(nombre_estado, descripcion)
        ON target.nombre_estado = source.nombre_estado
        WHEN MATCHED THEN
            UPDATE SET
                target.descripcion = source.descripcion,
                target.activo = 1
        WHEN NOT MATCHED THEN
            INSERT (nombre_estado, descripcion, activo)
            VALUES (source.nombre_estado, source.descripcion, 1);

        IF OBJECT_ID('dbo.tbl_citas', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_citas
            (
                id_cita INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_medico INT NOT NULL,
                id_paciente INT NOT NULL,
                id_disponibilidad INT NULL,
                fecha_cita DATE NOT NULL,
                hora_inicio TIME NOT NULL,
                hora_fin TIME NOT NULL,
                tipo_cita VARCHAR(50) NULL,
                motivo_cita VARCHAR(255) NULL,
                descripcion_adicional VARCHAR(MAX) NULL,
                id_estado INT NOT NULL CONSTRAINT DF_tbl_citas_estado DEFAULT (1),
                fecha_confirmacion DATETIME2 NULL,
                usuario_confirmacion VARCHAR(100) NULL,
                razon_cancelacion VARCHAR(500) NULL,
                fecha_cancelacion DATETIME2 NULL,
                usuario_cancelacion VARCHAR(100) NULL,
                notificacion_enviada BIT NOT NULL CONSTRAINT DF_tbl_citas_notificacion DEFAULT (0),
                fecha_notificacion_enviada DATETIME2 NULL,
                tipo_notificacion VARCHAR(50) NULL,
                recordatorio_24hrs BIT NOT NULL CONSTRAINT DF_tbl_citas_recordatorio_24 DEFAULT (0),
                recordatorio_1hr BIT NOT NULL CONSTRAINT DF_tbl_citas_recordatorio_1 DEFAULT (0),
                id_consulta INT NULL,
                notas_medico VARCHAR(MAX) NULL,
                fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_citas_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_citas_fecha_actualizacion DEFAULT (GETDATE()),
                usuario_creacion VARCHAR(100) NULL,
                usuario_actualizacion VARCHAR(100) NULL,
                CONSTRAINT fk_citas_medico FOREIGN KEY (id_medico) REFERENCES dbo.tbl_medico(id_medico),
                CONSTRAINT fk_citas_paciente FOREIGN KEY (id_paciente) REFERENCES dbo.tbl_paciente(id_paciente),
                CONSTRAINT fk_citas_disponibilidad FOREIGN KEY (id_disponibilidad) REFERENCES dbo.tbl_disponibilidad_medico(id_disponibilidad) ON DELETE SET NULL,
                CONSTRAINT fk_citas_consulta FOREIGN KEY (id_consulta) REFERENCES dbo.tbl_consulta(id_consulta) ON DELETE SET NULL,
                CONSTRAINT fk_citas_estado FOREIGN KEY (id_estado) REFERENCES dbo.tbl_estado_cita(id_estado),
                CONSTRAINT chk_citas_horas CHECK (hora_inicio < hora_fin)
            );
        END;

        IF OBJECT_ID('dbo.tbl_bloqueo_horarios', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_bloqueo_horarios
            (
                id_bloqueo INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_medico INT NOT NULL,
                fecha_inicio DATE NOT NULL,
                fecha_fin DATE NOT NULL,
                tipo_bloqueo VARCHAR(50) NULL,
                razon_bloqueo VARCHAR(300) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_bloqueo_horarios_activo DEFAULT (1),
                fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_bloqueo_horarios_fecha DEFAULT (GETDATE()),
                usuario_creacion VARCHAR(100) NULL,
                CONSTRAINT fk_bloqueo_medico FOREIGN KEY (id_medico) REFERENCES dbo.tbl_medico(id_medico) ON DELETE CASCADE,
                CONSTRAINT chk_bloqueo_fechas CHECK (fecha_inicio <= fecha_fin)
            );
        END;

        IF OBJECT_ID('dbo.tbl_cancelaciones_paciente', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_cancelaciones_paciente
            (
                id_cancelacion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_cita INT NOT NULL,
                id_paciente INT NOT NULL,
                fecha_cancelacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_cancelaciones_paciente_fecha DEFAULT (GETDATE()),
                razon_cancelacion VARCHAR(500) NULL,
                quien_cancelo VARCHAR(50) NULL,
                penalizacion_aplicada BIT NOT NULL CONSTRAINT DF_tbl_cancelaciones_paciente_penal DEFAULT (0),
                dias_espera_proxima_cita INT NULL,
                usuario_cancelacion VARCHAR(100) NULL,
                CONSTRAINT fk_cancelaciones_cita FOREIGN KEY (id_cita) REFERENCES dbo.tbl_citas(id_cita) ON DELETE CASCADE,
                CONSTRAINT fk_cancelaciones_paciente FOREIGN KEY (id_paciente) REFERENCES dbo.tbl_paciente(id_paciente) ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_medico_activo' AND object_id = OBJECT_ID('dbo.tbl_medico'))
            CREATE NONCLUSTERED INDEX idx_medico_activo ON dbo.tbl_medico(activo);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_medico_especialidad' AND object_id = OBJECT_ID('dbo.tbl_medico'))
            CREATE NONCLUSTERED INDEX idx_medico_especialidad ON dbo.tbl_medico(especialidad);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_disponibilidad_medico' AND object_id = OBJECT_ID('dbo.tbl_disponibilidad_medico'))
            CREATE NONCLUSTERED INDEX idx_disponibilidad_medico ON dbo.tbl_disponibilidad_medico(id_medico);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_disponibilidad_dia' AND object_id = OBJECT_ID('dbo.tbl_disponibilidad_medico'))
            CREATE NONCLUSTERED INDEX idx_disponibilidad_dia ON dbo.tbl_disponibilidad_medico(dia_semana);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_citas_medico' AND object_id = OBJECT_ID('dbo.tbl_citas'))
            CREATE NONCLUSTERED INDEX idx_citas_medico ON dbo.tbl_citas(id_medico);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_citas_paciente' AND object_id = OBJECT_ID('dbo.tbl_citas'))
            CREATE NONCLUSTERED INDEX idx_citas_paciente ON dbo.tbl_citas(id_paciente);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_citas_fecha' AND object_id = OBJECT_ID('dbo.tbl_citas'))
            CREATE NONCLUSTERED INDEX idx_citas_fecha ON dbo.tbl_citas(fecha_cita);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_citas_estado' AND object_id = OBJECT_ID('dbo.tbl_citas'))
            CREATE NONCLUSTERED INDEX idx_citas_estado ON dbo.tbl_citas(id_estado);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_citas_medico_fecha' AND object_id = OBJECT_ID('dbo.tbl_citas'))
            CREATE NONCLUSTERED INDEX idx_citas_medico_fecha ON dbo.tbl_citas(id_medico, fecha_cita);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_bloqueo_medico' AND object_id = OBJECT_ID('dbo.tbl_bloqueo_horarios'))
            CREATE NONCLUSTERED INDEX idx_bloqueo_medico ON dbo.tbl_bloqueo_horarios(id_medico);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_bloqueo_fechas' AND object_id = OBJECT_ID('dbo.tbl_bloqueo_horarios'))
            CREATE NONCLUSTERED INDEX idx_bloqueo_fechas ON dbo.tbl_bloqueo_horarios(fecha_inicio, fecha_fin);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_cancelaciones_paciente' AND object_id = OBJECT_ID('dbo.tbl_cancelaciones_paciente'))
            CREATE NONCLUSTERED INDEX idx_cancelaciones_paciente ON dbo.tbl_cancelaciones_paciente(id_paciente);

        UPDATE dbo.tbl_menu_app
        SET ruta = '/citas',
            nombre = 'Citas y turnos',
            icono = 'calendar-check'
        WHERE ruta = '/appointments'
          AND NOT EXISTS (SELECT 1 FROM dbo.tbl_menu_app WHERE ruta = '/citas');

        UPDATE dbo.tbl_menu_app
        SET activo = 0
        WHERE ruta = '/appointments'
          AND EXISTS (SELECT 1 FROM dbo.tbl_menu_app WHERE ruta = '/citas');

        UPDATE dbo.tbl_menu_app
        SET nombre = 'Citas y turnos',
            icono = 'calendar-check'
        WHERE ruta = '/citas';
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

static async Task EnsureElectronicBillingSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.emisor', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.emisor
            (
                emisor_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                ruc VARCHAR(13) NOT NULL,
                razon_social VARCHAR(300) NOT NULL,
                nombre_comercial VARCHAR(300) NULL,
                tipo_persona CHAR(1) NOT NULL,
                tipo_identificacion VARCHAR(2) NOT NULL,
                direccion VARCHAR(500) NULL,
                telefono VARCHAR(20) NULL,
                correo VARCHAR(100) NULL,
                provincia VARCHAR(100) NULL,
                ciudad VARCHAR(100) NULL,
                codigo_postal VARCHAR(10) NULL,
                establecimiento_codigo VARCHAR(3) NOT NULL,
                punto_emision_codigo VARCHAR(3) NOT NULL,
                nombre_representante_legal VARCHAR(300) NULL,
                cedula_representante VARCHAR(10) NULL,
                es_contribuyente_especial BIT NOT NULL CONSTRAINT DF_emisor_es_contribuyente_especial DEFAULT (0),
                numero_contribuyente_especial VARCHAR(10) NULL,
                estado BIT NOT NULL CONSTRAINT DF_emisor_estado DEFAULT (1),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_emisor_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME NOT NULL CONSTRAINT DF_emisor_fecha_actualizacion DEFAULT (GETDATE()),
                id_usuario_creacion INT NOT NULL,
                id_usuario_actualizacion INT NULL,
                CONSTRAINT FK_emisor_usuario_creacion FOREIGN KEY (id_usuario_creacion) REFERENCES dbo.tbl_usuario(id_usuario),
                CONSTRAINT FK_emisor_usuario_actualizacion FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF OBJECT_ID('dbo.clients', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.clients
            (
                cliente_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                tipo_cliente VARCHAR(20) NOT NULL,
                tipo_identificacion VARCHAR(2) NOT NULL,
                numero_identificacion VARCHAR(20) NOT NULL,
                razon_social VARCHAR(300) NOT NULL,
                nombres VARCHAR(200) NULL,
                apellidos VARCHAR(200) NULL,
                nombre_comercial VARCHAR(300) NULL,
                direccion VARCHAR(500) NULL,
                ciudad VARCHAR(100) NULL,
                provincia VARCHAR(100) NULL,
                codigo_postal VARCHAR(10) NULL,
                telefono VARCHAR(20) NULL,
                correo_electronico VARCHAR(100) NULL,
                es_contribuyente_especial BIT NOT NULL CONSTRAINT DF_clients_es_contribuyente_especial DEFAULT (0),
                numero_contribuyente_especial VARCHAR(10) NULL,
                pais_codigo VARCHAR(2) NOT NULL CONSTRAINT DF_clients_pais_codigo DEFAULT ('EC'),
                es_residente_exterior BIT NOT NULL CONSTRAINT DF_clients_es_residente_exterior DEFAULT (0),
                es_consumidor_final BIT NOT NULL CONSTRAINT DF_clients_es_consumidor_final DEFAULT (0),
                es_obligado_contabilidad BIT NOT NULL CONSTRAINT DF_clients_es_obligado_contabilidad DEFAULT (0),
                contacto_nombre VARCHAR(200) NULL,
                contacto_telefono VARCHAR(20) NULL,
                contacto_correo VARCHAR(100) NULL,
                condicion_pago VARCHAR(50) NULL,
                dias_plazo INT NOT NULL CONSTRAINT DF_clients_dias_plazo DEFAULT (0),
                limite_credito DECIMAL(15,2) NOT NULL CONSTRAINT DF_clients_limite_credito DEFAULT (0),
                saldo_deudor DECIMAL(15,2) NOT NULL CONSTRAINT DF_clients_saldo_deudor DEFAULT (0),
                estado BIT NOT NULL CONSTRAINT DF_clients_estado DEFAULT (1),
                observaciones VARCHAR(500) NULL,
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_clients_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME NOT NULL CONSTRAINT DF_clients_fecha_actualizacion DEFAULT (GETDATE()),
                id_usuario_creacion INT NOT NULL,
                id_usuario_actualizacion INT NULL,
                CONSTRAINT FK_clients_usuario_creacion FOREIGN KEY (id_usuario_creacion) REFERENCES dbo.tbl_usuario(id_usuario),
                CONSTRAINT FK_clients_usuario_actualizacion FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_emisor_ruc' AND object_id = OBJECT_ID('dbo.emisor'))
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX UQ_emisor_ruc ON dbo.emisor (ruc);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_emisor_usuario' AND object_id = OBJECT_ID('dbo.emisor'))
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX UQ_emisor_usuario ON dbo.emisor (id_usuario_creacion);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_emisor_ruc' AND object_id = OBJECT_ID('dbo.emisor'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_emisor_ruc ON dbo.emisor (ruc);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_emisor_estado' AND object_id = OBJECT_ID('dbo.emisor'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_emisor_estado ON dbo.emisor (estado);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_clients_usuario_identificacion' AND object_id = OBJECT_ID('dbo.clients'))
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX UQ_clients_usuario_identificacion ON dbo.clients (id_usuario_creacion, tipo_identificacion, numero_identificacion);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_numero_identificacion' AND object_id = OBJECT_ID('dbo.clients'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_clients_numero_identificacion ON dbo.clients (numero_identificacion);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_estado' AND object_id = OBJECT_ID('dbo.clients'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_clients_estado ON dbo.clients (estado);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_razon_social' AND object_id = OBJECT_ID('dbo.clients'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_clients_razon_social ON dbo.clients (razon_social);
        END;

        """);
}

static async Task EnsureProductSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF COL_LENGTH('dbo.tbl_producto', 'almacen') IS NULL ALTER TABLE dbo.tbl_producto ADD almacen VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_categoria_producto', 'activo') IS NULL ALTER TABLE dbo.tbl_categoria_producto ADD activo BIT NOT NULL CONSTRAINT DF_tbl_categoria_producto_activo DEFAULT (1);
        UPDATE dbo.tbl_categoria_producto SET activo = 1 WHERE activo IS NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'pasillo') IS NULL ALTER TABLE dbo.tbl_producto ADD pasillo VARCHAR(10) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'estante') IS NULL ALTER TABLE dbo.tbl_producto ADD estante VARCHAR(10) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'nivel') IS NULL ALTER TABLE dbo.tbl_producto ADD nivel VARCHAR(10) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'stock_maximo') IS NULL ALTER TABLE dbo.tbl_producto ADD stock_maximo INT NOT NULL CONSTRAINT DF_tbl_producto_stock_maximo DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'punto_reorden') IS NULL ALTER TABLE dbo.tbl_producto ADD punto_reorden INT NOT NULL CONSTRAINT DF_tbl_producto_punto_reorden DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'cantidad_empaque') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_empaque INT NOT NULL CONSTRAINT DF_tbl_producto_cantidad_empaque DEFAULT (1);
        IF COL_LENGTH('dbo.tbl_producto', 'peso_unitario') IS NULL ALTER TABLE dbo.tbl_producto ADD peso_unitario DECIMAL(10,4) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_largo') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_largo DECIMAL(10,4) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_ancho') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_ancho DECIMAL(10,4) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_alto') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_alto DECIMAL(10,4) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'volumen_m3') IS NULL ALTER TABLE dbo.tbl_producto ADD volumen_m3 DECIMAL(15,8) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'requiere_lote') IS NULL ALTER TABLE dbo.tbl_producto ADD requiere_lote BIT NOT NULL CONSTRAINT DF_tbl_producto_requiere_lote DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'requiere_fecha_vencimiento') IS NULL ALTER TABLE dbo.tbl_producto ADD requiere_fecha_vencimiento BIT NOT NULL CONSTRAINT DF_tbl_producto_requiere_fecha_vencimiento DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'dias_vencimiento') IS NULL ALTER TABLE dbo.tbl_producto ADD dias_vencimiento INT NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'cuenta_contable') IS NULL ALTER TABLE dbo.tbl_producto ADD cuenta_contable VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'centro_costo') IS NULL ALTER TABLE dbo.tbl_producto ADD centro_costo VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'naturaleza_item') IS NULL ALTER TABLE dbo.tbl_producto ADD naturaleza_item VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'tipo_item') IS NULL ALTER TABLE dbo.tbl_producto ADD tipo_item VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'porcentaje_margen') IS NULL ALTER TABLE dbo.tbl_producto ADD porcentaje_margen DECIMAL(5,2) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'descuento_mayorista') IS NULL ALTER TABLE dbo.tbl_producto ADD descuento_mayorista DECIMAL(5,2) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'descuento_cliente_fijo') IS NULL ALTER TABLE dbo.tbl_producto ADD descuento_cliente_fijo DECIMAL(5,2) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'movimiento_frecuencia') IS NULL ALTER TABLE dbo.tbl_producto ADD movimiento_frecuencia VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'dias_rotacion_promedio') IS NULL ALTER TABLE dbo.tbl_producto ADD dias_rotacion_promedio INT NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'fecha_ultima_compra') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_ultima_compra DATETIME NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'fecha_ultima_venta') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_ultima_venta DATETIME NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'fecha_actualizacion_precio') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_actualizacion_precio DATETIME NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'id_usuario_actualizacion') IS NULL ALTER TABLE dbo.tbl_producto ADD id_usuario_actualizacion INT NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'cantidad_movimientos_mes') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_movimientos_mes INT NOT NULL CONSTRAINT DF_tbl_producto_cantidad_movimientos_mes DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'etiquetas') IS NULL ALTER TABLE dbo.tbl_producto ADD etiquetas VARCHAR(500) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'marca') IS NULL ALTER TABLE dbo.tbl_producto ADD marca VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'modelo') IS NULL ALTER TABLE dbo.tbl_producto ADD modelo VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'color') IS NULL ALTER TABLE dbo.tbl_producto ADD color VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'talla') IS NULL ALTER TABLE dbo.tbl_producto ADD talla VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'es_promocion') IS NULL ALTER TABLE dbo.tbl_producto ADD es_promocion BIT NOT NULL CONSTRAINT DF_tbl_producto_es_promocion DEFAULT (0);
        IF COL_LENGTH('dbo.tbl_producto', 'porcentaje_descuento_promo') IS NULL ALTER TABLE dbo.tbl_producto ADD porcentaje_descuento_promo DECIMAL(5,2) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'material_principal') IS NULL ALTER TABLE dbo.tbl_producto ADD material_principal VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'tratamiento_lente') IS NULL ALTER TABLE dbo.tbl_producto ADD tratamiento_lente VARCHAR(200) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'estado_producto') IS NULL ALTER TABLE dbo.tbl_producto ADD estado_producto VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_producto_estado_producto DEFAULT ('Disponible');
        IF COL_LENGTH('dbo.tbl_producto', 'motivo_estado') IS NULL ALTER TABLE dbo.tbl_producto ADD motivo_estado VARCHAR(300) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'codigo_barras') IS NULL ALTER TABLE dbo.tbl_producto ADD codigo_barras VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'sku_alterno') IS NULL ALTER TABLE dbo.tbl_producto ADD sku_alterno VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'referencia_fabricante') IS NULL ALTER TABLE dbo.tbl_producto ADD referencia_fabricante VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'proveedor_preferente') IS NULL ALTER TABLE dbo.tbl_producto ADD proveedor_preferente VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'tiempo_entrega_dias') IS NULL ALTER TABLE dbo.tbl_producto ADD tiempo_entrega_dias INT NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'cantidad_pedido_optima') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_pedido_optima INT NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'fecha_creacion') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_producto_fecha_creacion DEFAULT (GETDATE());
        IF COL_LENGTH('dbo.tbl_producto', 'usuario_creacion') IS NULL ALTER TABLE dbo.tbl_producto ADD usuario_creacion VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_producto', 'notas_internas') IS NULL ALTER TABLE dbo.tbl_producto ADD notas_internas VARCHAR(MAX) NULL;

        IF OBJECT_ID('dbo.tbl_tipo_item', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_tipo_item
            (
                id_tipo_item INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                nombre_tipo VARCHAR(50) NOT NULL UNIQUE,
                descripcion VARCHAR(255) NULL
            );
        END;

        MERGE dbo.tbl_tipo_item AS target
        USING
        (
            VALUES
                ('Producto', 'Articulos fisicos'),
                ('Servicio', 'Servicios de la clinica')
        ) AS source(nombre_tipo, descripcion)
        ON target.nombre_tipo = source.nombre_tipo
        WHEN MATCHED THEN
            UPDATE SET target.descripcion = source.descripcion
        WHEN NOT MATCHED THEN
            INSERT (nombre_tipo, descripcion)
            VALUES (source.nombre_tipo, source.descripcion);

        UPDATE dbo.tbl_producto
        SET tipo_item = 'Producto'
        WHERE tipo_item IS NULL OR LTRIM(RTRIM(tipo_item)) = '';

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = 'CK_tipo_item'
                AND parent_object_id = OBJECT_ID('dbo.tbl_producto')
        )
        BEGIN
            ALTER TABLE dbo.tbl_producto
            ADD CONSTRAINT CK_tipo_item CHECK (tipo_item IN ('Producto', 'Servicio'));
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_producto_tipo_item' AND object_id = OBJECT_ID('dbo.tbl_producto'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_tbl_producto_tipo_item ON dbo.tbl_producto(tipo_item);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_categoria_producto_activo' AND object_id = OBJECT_ID('dbo.tbl_categoria_producto'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_tbl_categoria_producto_activo ON dbo.tbl_categoria_producto(activo);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_producto_usuario_actualizacion')
        BEGIN
            ALTER TABLE dbo.tbl_producto
            ADD CONSTRAINT FK_tbl_producto_usuario_actualizacion
                FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_producto_codigo_barras' AND object_id = OBJECT_ID('dbo.tbl_producto'))
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_producto_codigo_barras
            ON dbo.tbl_producto (codigo_barras)
            WHERE codigo_barras IS NOT NULL;
        END;

        DECLARE @tipoItemDefaultName sysname;
        SELECT @tipoItemDefaultName = dc.name
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.default_object_id = dc.object_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.tbl_producto')
            AND c.name = 'tipo_item';

        IF @tipoItemDefaultName IS NULL
        BEGIN
            ALTER TABLE dbo.tbl_producto
            ADD CONSTRAINT DF_tbl_producto_tipo_item DEFAULT ('Producto') FOR tipo_item;
        END;
        """);
}

static async Task EnsureSupplierSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF COL_LENGTH('dbo.tbl_proveedor', 'ruc') IS NULL ALTER TABLE dbo.tbl_proveedor ADD ruc VARCHAR(13) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'razon_social') IS NULL ALTER TABLE dbo.tbl_proveedor ADD razon_social VARCHAR(300) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'nombre_comercial') IS NULL ALTER TABLE dbo.tbl_proveedor ADD nombre_comercial VARCHAR(300) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'tipo_identificacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tipo_identificacion VARCHAR(2) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'ciudad') IS NULL ALTER TABLE dbo.tbl_proveedor ADD ciudad VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'provincia') IS NULL ALTER TABLE dbo.tbl_proveedor ADD provincia VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'codigo_postal') IS NULL ALTER TABLE dbo.tbl_proveedor ADD codigo_postal VARCHAR(10) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_nombre') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_nombre VARCHAR(200) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_telefono') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_telefono VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_correo') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_correo VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'dias_credito_promedio') IS NULL ALTER TABLE dbo.tbl_proveedor ADD dias_credito_promedio INT NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'saldo_pendiente') IS NULL ALTER TABLE dbo.tbl_proveedor ADD saldo_pendiente DECIMAL(15,2) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'limite_credito') IS NULL ALTER TABLE dbo.tbl_proveedor ADD limite_credito DECIMAL(15,2) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'condicion_pago') IS NULL ALTER TABLE dbo.tbl_proveedor ADD condicion_pago VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'banco_nombre') IS NULL ALTER TABLE dbo.tbl_proveedor ADD banco_nombre VARCHAR(100) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'cuenta_bancaria') IS NULL ALTER TABLE dbo.tbl_proveedor ADD cuenta_bancaria VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'tipo_cuenta') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tipo_cuenta VARCHAR(20) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'calificacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD calificacion VARCHAR(1) NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'tiempo_entrega_promedio') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tiempo_entrega_promedio INT NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'es_activo') IS NULL ALTER TABLE dbo.tbl_proveedor ADD es_activo BIT NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'fecha_registro') IS NULL ALTER TABLE dbo.tbl_proveedor ADD fecha_registro DATETIME NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'fecha_actualizacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD fecha_actualizacion DATETIME NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'id_usuario_registro') IS NULL ALTER TABLE dbo.tbl_proveedor ADD id_usuario_registro INT NULL;
        IF COL_LENGTH('dbo.tbl_proveedor', 'id_usuario_actualizacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD id_usuario_actualizacion INT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_proveedor_usuario_registro')
        BEGIN
            ALTER TABLE dbo.tbl_proveedor
            ADD CONSTRAINT FK_tbl_proveedor_usuario_registro
                FOREIGN KEY (id_usuario_registro) REFERENCES dbo.tbl_usuario(id_usuario);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_proveedor_usuario_actualizacion')
        BEGIN
            ALTER TABLE dbo.tbl_proveedor
            ADD CONSTRAINT FK_tbl_proveedor_usuario_actualizacion
                FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario);
        END;
        """);
}

static async Task EnsureProcurementSchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.tbl_lote_producto', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_lote_producto
            (
                id_lote INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_producto INT NOT NULL,
                numero_lote VARCHAR(50) NOT NULL,
                numero_serie VARCHAR(50) NULL,
                id_orden_compra INT NULL,
                cantidad_inicial INT NOT NULL,
                cantidad_disponible INT NOT NULL,
                cantidad_vendida INT NOT NULL CONSTRAINT DF_tbl_lote_producto_cantidad_vendida DEFAULT (0),
                cantidad_devuelta INT NOT NULL CONSTRAINT DF_tbl_lote_producto_cantidad_devuelta DEFAULT (0),
                cantidad_merma INT NOT NULL CONSTRAINT DF_tbl_lote_producto_cantidad_merma DEFAULT (0),
                fecha_fabricacion DATE NULL,
                fecha_vencimiento DATE NULL,
                costo_unitario DECIMAL(15,2) NULL,
                precio_venta_unitario DECIMAL(15,2) NULL,
                valor_total_costo DECIMAL(15,2) NULL,
                estado_lote VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_lote_producto_estado_lote DEFAULT ('Disponible'),
                almacen VARCHAR(50) NULL,
                pasillo VARCHAR(10) NULL,
                estante VARCHAR(10) NULL,
                nivel VARCHAR(10) NULL,
                fecha_ingreso DATETIME NOT NULL CONSTRAINT DF_tbl_lote_producto_fecha_ingreso DEFAULT (GETDATE()),
                fecha_ultima_salida DATETIME NULL,
                id_usuario_ingreso INT NULL,
                observaciones VARCHAR(500) NULL,
                CONSTRAINT UQ_lote_numero UNIQUE (numero_lote, id_producto),
                CONSTRAINT FK_lote_producto FOREIGN KEY (id_producto) REFERENCES dbo.tbl_producto(id_producto)
            );
        END;

        IF OBJECT_ID('dbo.tbl_orden_compra', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_orden_compra
            (
                id_orden_compra INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                numero_orden VARCHAR(20) NOT NULL UNIQUE,
                id_proveedor INT NOT NULL,
                id_usuario_solicita INT NOT NULL,
                id_usuario_autoriza INT NULL,
                fecha_orden DATETIME NOT NULL CONSTRAINT DF_tbl_orden_compra_fecha_orden DEFAULT (GETDATE()),
                fecha_requerida DATE NULL,
                fecha_recepcion_esperada DATE NULL,
                fecha_recepcion_real DATETIME NULL,
                subtotal DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_orden_compra_subtotal DEFAULT (0),
                descuento_general DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_orden_compra_descuento DEFAULT (0),
                impuesto_total DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_orden_compra_impuesto DEFAULT (0),
                total DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_orden_compra_total DEFAULT (0),
                condicion_pago VARCHAR(50) NULL,
                dias_credito INT NULL,
                fecha_vencimiento_pago DATE NULL,
                moneda VARCHAR(3) NOT NULL CONSTRAINT DF_tbl_orden_compra_moneda DEFAULT ('USD'),
                tasa_cambio DECIMAL(10,6) NULL,
                estado_orden VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_orden_compra_estado DEFAULT ('Pendiente'),
                tipo_orden VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_orden_compra_tipo DEFAULT ('Compra'),
                referencia_externa VARCHAR(100) NULL,
                observaciones VARCHAR(MAX) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_orden_compra_activo DEFAULT (1),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_orden_compra_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME NULL,
                CONSTRAINT FK_orden_proveedor FOREIGN KEY (id_proveedor) REFERENCES dbo.tbl_proveedor(id_proveedor),
                CONSTRAINT FK_orden_usuario_solicita FOREIGN KEY (id_usuario_solicita) REFERENCES dbo.tbl_usuario(id_usuario),
                CONSTRAINT FK_orden_usuario_autoriza FOREIGN KEY (id_usuario_autoriza) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF OBJECT_ID('dbo.tbl_detalle_orden_compra', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_detalle_orden_compra
            (
                id_detalle_orden_compra INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_orden_compra INT NOT NULL,
                id_producto INT NOT NULL,
                id_lote INT NULL,
                cantidad_solicitada INT NOT NULL,
                cantidad_recibida INT NOT NULL CONSTRAINT DF_tbl_detalle_orden_compra_cantidad_recibida DEFAULT (0),
                cantidad_rechazada INT NOT NULL CONSTRAINT DF_tbl_detalle_orden_compra_cantidad_rechazada DEFAULT (0),
                cantidad_pendiente INT NULL,
                precio_unitario DECIMAL(15,2) NOT NULL,
                precio_total_linea DECIMAL(15,2) NULL,
                descuento_linea DECIMAL(5,2) NULL,
                impuesto_linea DECIMAL(15,2) NULL,
                codigo_fiscal_fe VARCHAR(10) NULL,
                unidad_medida_fe VARCHAR(10) NULL,
                estado_linea VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_detalle_orden_compra_estado DEFAULT ('Pendiente'),
                fecha_recepcion_esperada DATE NULL,
                observaciones VARCHAR(500) NULL,
                CONSTRAINT FK_detalle_orden FOREIGN KEY (id_orden_compra) REFERENCES dbo.tbl_orden_compra(id_orden_compra) ON DELETE CASCADE,
                CONSTRAINT FK_detalle_producto FOREIGN KEY (id_producto) REFERENCES dbo.tbl_producto(id_producto)
            );
        END;

        IF OBJECT_ID('dbo.tbl_recepcion_compra', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_recepcion_compra
            (
                id_recepcion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_orden_compra INT NOT NULL,
                numero_recepcion VARCHAR(20) NOT NULL UNIQUE,
                numero_guia_remision VARCHAR(30) NULL,
                id_usuario_recibe INT NOT NULL,
                fecha_recepcion DATETIME NOT NULL CONSTRAINT DF_tbl_recepcion_compra_fecha_recepcion DEFAULT (GETDATE()),
                cantidad_total_recibida INT NULL,
                cantidad_total_rechazada INT NOT NULL CONSTRAINT DF_tbl_recepcion_compra_cantidad_rechazada DEFAULT (0),
                observaciones_recepcion VARCHAR(MAX) NULL,
                estado_recepcion VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_recepcion_compra_estado DEFAULT ('Completa'),
                activo BIT NOT NULL CONSTRAINT DF_tbl_recepcion_compra_activo DEFAULT (1),
                CONSTRAINT FK_recepcion_orden FOREIGN KEY (id_orden_compra) REFERENCES dbo.tbl_orden_compra(id_orden_compra),
                CONSTRAINT FK_recepcion_usuario FOREIGN KEY (id_usuario_recibe) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF OBJECT_ID('dbo.tbl_liquidacion_compra', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_liquidacion_compra
            (
                id_liquidacion_compra INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_orden_compra INT NOT NULL,
                numero_liquidacion VARCHAR(20) NOT NULL UNIQUE,
                id_usuario_registro INT NOT NULL,
                fecha_liquidacion DATETIME NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_fecha DEFAULT (GETDATE()),
                numero_factura VARCHAR(50) NULL,
                numero_autorizacion VARCHAR(100) NULL,
                subtotal DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_subtotal DEFAULT (0),
                descuento_total DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_descuento DEFAULT (0),
                impuesto_total DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_impuesto DEFAULT (0),
                total DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_total DEFAULT (0),
                saldo_pagado DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_pagado DEFAULT (0),
                saldo_pendiente DECIMAL(15,2) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_pendiente DEFAULT (0),
                estado_liquidacion VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_estado DEFAULT ('Pendiente'),
                observaciones VARCHAR(MAX) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_activo DEFAULT (1),
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_liquidacion_compra_fecha_creacion DEFAULT (GETDATE()),
                fecha_actualizacion DATETIME NULL,
                CONSTRAINT FK_liquidacion_orden_compra FOREIGN KEY (id_orden_compra) REFERENCES dbo.tbl_orden_compra(id_orden_compra),
                CONSTRAINT FK_liquidacion_usuario_registro FOREIGN KEY (id_usuario_registro) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_referencia_documento') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_referencia_documento INT NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'tipo_documento_referencia') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD tipo_documento_referencia VARCHAR(30) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_lote') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_lote INT NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'numero_lote') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD numero_lote VARCHAR(50) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'costo_unitario') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD costo_unitario DECIMAL(15,2) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'costo_total_movimiento') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD costo_total_movimiento DECIMAL(15,2) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'saldo_en_dinero') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD saldo_en_dinero DECIMAL(15,2) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'metodo_valuacion') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD metodo_valuacion VARCHAR(30) NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_usuario_autoriza') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_usuario_autoriza INT NULL;
        IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'comprobante_numero') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD comprobante_numero VARCHAR(50) NULL;

        IF OBJECT_ID('dbo.tbl_kardex', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_kardex
            (
                id_kardex INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_producto INT NOT NULL,
                id_lote INT NULL,
                numero_lote VARCHAR(50) NULL,
                fecha_movimiento DATETIME NOT NULL CONSTRAINT DF_tbl_kardex_fecha_movimiento DEFAULT (GETDATE()),
                tipo_movimiento VARCHAR(30) NOT NULL,
                id_referencia INT NULL,
                tipo_referencia VARCHAR(30) NULL,
                comprobante_numero VARCHAR(50) NULL,
                cantidad_movimiento INT NOT NULL,
                costo_unitario DECIMAL(15,2) NULL,
                costo_total DECIMAL(15,2) NULL,
                stock_anterior INT NULL,
                stock_nuevo INT NULL,
                saldo_anterior_dinero DECIMAL(15,2) NULL,
                saldo_nuevo_dinero DECIMAL(15,2) NULL,
                precio_promedio_ponderado DECIMAL(15,2) NULL,
                metodo_valuacion VARCHAR(30) NOT NULL CONSTRAINT DF_tbl_kardex_metodo DEFAULT ('Promedio'),
                id_usuario_movimiento INT NULL,
                descripcion_movimiento VARCHAR(500) NULL,
                glosa_contable VARCHAR(255) NULL,
                cuenta_contable_debito VARCHAR(20) NULL,
                cuenta_contable_credito VARCHAR(20) NULL,
                centro_costo VARCHAR(20) NULL,
                estado_kardex VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_kardex_estado DEFAULT ('Registrado'),
                observaciones VARCHAR(MAX) NULL,
                fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_kardex_fecha_creacion DEFAULT (GETDATE()),
                CONSTRAINT FK_kardex_producto FOREIGN KEY (id_producto) REFERENCES dbo.tbl_producto(id_producto)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_lote_orden_compra')
            ALTER TABLE dbo.tbl_lote_producto ADD CONSTRAINT FK_lote_orden_compra FOREIGN KEY (id_orden_compra) REFERENCES dbo.tbl_orden_compra(id_orden_compra);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_lote_usuario_ingreso')
            ALTER TABLE dbo.tbl_lote_producto ADD CONSTRAINT FK_lote_usuario_ingreso FOREIGN KEY (id_usuario_ingreso) REFERENCES dbo.tbl_usuario(id_usuario);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_detalle_lote')
            ALTER TABLE dbo.tbl_detalle_orden_compra ADD CONSTRAINT FK_detalle_lote FOREIGN KEY (id_lote) REFERENCES dbo.tbl_lote_producto(id_lote);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_movimiento_lote')
            ALTER TABLE dbo.tbl_movimiento_inventario ADD CONSTRAINT FK_movimiento_lote FOREIGN KEY (id_lote) REFERENCES dbo.tbl_lote_producto(id_lote);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_movimiento_usuario_autoriza')
            ALTER TABLE dbo.tbl_movimiento_inventario ADD CONSTRAINT FK_movimiento_usuario_autoriza FOREIGN KEY (id_usuario_autoriza) REFERENCES dbo.tbl_usuario(id_usuario);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_kardex_lote')
            ALTER TABLE dbo.tbl_kardex ADD CONSTRAINT FK_kardex_lote FOREIGN KEY (id_lote) REFERENCES dbo.tbl_lote_producto(id_lote);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_kardex_usuario')
            ALTER TABLE dbo.tbl_kardex ADD CONSTRAINT FK_kardex_usuario FOREIGN KEY (id_usuario_movimiento) REFERENCES dbo.tbl_usuario(id_usuario);
        """);
}

static async Task EnsureClinicalHistorySchemaAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OpticaDbContext>();

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        IF OBJECT_ID('dbo.tbl_historia_clinica_optometria', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tbl_historia_clinica_optometria
            (
                id_historia_clinica INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                id_paciente INT NOT NULL,
                id_optometra_apertura INT NOT NULL,
                id_optometra_ultima_actualizacion INT NULL,
                fecha_apertura DATETIME NOT NULL CONSTRAINT DF_tbl_historia_clinica_optometria_fecha_apertura DEFAULT (GETDATE()),
                fecha_ultima_actualizacion DATETIME NOT NULL CONSTRAINT DF_tbl_historia_clinica_optometria_fecha_actualizacion DEFAULT (GETDATE()),
                numero_historia VARCHAR(50) NULL,
                consultorio VARCHAR(120) NULL,
                llave_clinica VARCHAR(120) NULL,
                lugar_nacimiento VARCHAR(150) NULL,
                procedencia VARCHAR(150) NULL,
                ultimo_control VARCHAR(150) NULL,
                datos_apertura_json VARCHAR(MAX) NULL,
                motivo_consulta VARCHAR(MAX) NULL,
                anamnesis VARCHAR(MAX) NULL,
                antecedentes_json VARCHAR(MAX) NULL,
                usa_lentes BIT NOT NULL CONSTRAINT DF_tbl_historia_clinica_optometria_usa_lentes DEFAULT (0),
                lentes_json VARCHAR(MAX) NULL,
                agudeza_visual_json VARCHAR(MAX) NULL,
                biomicroscopia_json VARCHAR(MAX) NULL,
                oftalmoscopia_json VARCHAR(MAX) NULL,
                examen_motor_json VARCHAR(MAX) NULL,
                queratometria_json VARCHAR(MAX) NULL,
                refraccion_json VARCHAR(MAX) NULL,
                diagnostico_json VARCHAR(MAX) NULL,
                observaciones_generales VARCHAR(MAX) NULL,
                nombre_examinador VARCHAR(200) NULL,
                nivel_paralelo_jornada VARCHAR(200) NULL,
                consentimiento_json VARCHAR(MAX) NULL,
                activo BIT NOT NULL CONSTRAINT DF_tbl_historia_clinica_optometria_activo DEFAULT (1),
                CONSTRAINT UQ_tbl_historia_clinica_optometria_paciente UNIQUE (id_paciente),
                CONSTRAINT FK_tbl_historia_clinica_optometria_paciente FOREIGN KEY (id_paciente) REFERENCES dbo.tbl_paciente(id_paciente) ON DELETE CASCADE,
                CONSTRAINT FK_tbl_historia_clinica_optometria_optometra_apertura FOREIGN KEY (id_optometra_apertura) REFERENCES dbo.tbl_usuario(id_usuario),
                CONSTRAINT FK_tbl_historia_clinica_optometria_optometra_actualiza FOREIGN KEY (id_optometra_ultima_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_historia_clinica_optometria_optometra' AND object_id = OBJECT_ID('dbo.tbl_historia_clinica_optometria'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_tbl_historia_clinica_optometria_optometra ON dbo.tbl_historia_clinica_optometria(id_optometra_apertura);
        END;
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
