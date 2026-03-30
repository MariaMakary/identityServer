using Duende.IdentityServer.Models;

namespace identityServer.Config;

public static class IdentityServerConfig
{
    public static IEnumerable<IdentityResource> IdentityResources =>
        new IdentityResource[]
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile()
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new[] { new ApiScope("api", "Dashboard API") };

    public static IEnumerable<ApiResource> ApiResources =>
        new[]
        {
            new ApiResource("api", "Dashboard API")
            {
                Scopes = { "api" }
            }
        };

    public static IEnumerable<Client> Clients =>
        new[]
        {
            new Client
            {
                ClientId = "dashboard",
                AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                RequireClientSecret = false,
                AllowOfflineAccess = true,
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                RefreshTokenExpiration = TokenExpiration.Sliding,
                SlidingRefreshTokenLifetime = 2592000, // 30 days
                AllowedScopes = { "openid", "profile", "api", "offline_access" }
            }
        };
}
