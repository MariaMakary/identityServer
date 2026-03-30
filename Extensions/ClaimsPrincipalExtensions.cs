using System.Security.Claims;
using IdentityModel;

namespace identityServer.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(JwtClaimTypes.Subject);

    public static bool IsAdmin(this ClaimsPrincipal principal)
        => principal.IsInRole("Admin");
}
