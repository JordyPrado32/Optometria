using Microsoft.AspNetCore.Authorization;

namespace OptometriaApp.Configuration;

public static class AccessPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        foreach (var name in new[] { "FullAccess", "OperationalAccess" })
        {
            options.AddPolicy(name, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("AuthStage", "FullAccess")
                .RequireAssertion(context => !string.Equals(
                    context.User.FindFirst("ForcePasswordChange")?.Value,
                    bool.TrueString, StringComparison.OrdinalIgnoreCase)));
        }

        options.AddPolicy("PasswordChangeAccess", policy => policy
            .RequireAuthenticatedUser().RequireClaim("AuthStage", "FullAccess"));
        options.AddPolicy("TwoFactorSetup", policy => policy
            .RequireAuthenticatedUser().RequireClaim("AuthStage", "TwoFactorSetupRequired", "FullAccess"));
        options.AddPolicy("TwoFactorVerification", policy => policy
            .RequireAuthenticatedUser().RequireClaim("AuthStage", "TwoFactorPending"));
    }
}
