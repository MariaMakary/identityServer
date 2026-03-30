using System.Security.Claims;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Stores;
using IdentityModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using identityServer.Data;
using identityServer.DTOs;
using identityServer.Models;

namespace identityServer.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/auth");

        // POST /api/auth/register
        api.MapPost("/register", async (RegisterRequest request, UserManager<User> userManager, AppDbContext db) =>
        {
            var existingUser = await userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
                return Results.BadRequest("Email already exists");

            var user = new User
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return Results.BadRequest(string.Join(", ", result.Errors.Select(e => e.Description)));

            await userManager.AddToRoleAsync(user, "User");

            // Auto-accept pending invitations for this email
            var email = request.Email.Trim().ToLower();
            var pendingInvitations = await db.ProjectInvitations
                .Where(i => i.InviteeEmail == email
                    && i.Status == InvitationStatus.Pending
                    && i.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            foreach (var invitation in pendingInvitations)
            {
                invitation.Status = InvitationStatus.Accepted;
                invitation.InviteeUserId = user.Id;
            }

            if (pendingInvitations.Count > 0)
                await db.SaveChangesAsync();

            return Results.Ok("User registered");
        });

        // POST /api/auth/login
        api.MapPost("/login", async (
            LoginRequest request,
            UserManager<User> userManager,
            ITokenCreationService tokenCreation,
            IRefreshTokenService refreshTokenService,
            IIssuerNameService issuerNameService,
            IClientStore clientStore) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            var issuer = await issuerNameService.GetCurrentAsync();

            var accessToken = new Token(IdentityServerConstants.TokenTypes.AccessToken)
            {
                CreationTime = DateTime.UtcNow,
                Issuer = issuer,
                Lifetime = 3600,
                Claims =
                {
                    new Claim(JwtClaimTypes.Subject, user.Id),
                    new Claim(JwtClaimTypes.ClientId, "dashboard"),
                    new Claim(JwtClaimTypes.Email, user.Email!),
                    new Claim(JwtClaimTypes.Role, role),
                    new Claim(JwtClaimTypes.Scope, "api")
                },
                Audiences = { "api" }
            };

            var jwt = await tokenCreation.CreateTokenAsync(accessToken);

            var client = await clientStore.FindClientByIdAsync("dashboard");
            var subject = new ClaimsPrincipal(new ClaimsIdentity(accessToken.Claims, "idsrv", JwtClaimTypes.Name, JwtClaimTypes.Role));

            var refreshTokenHandle = await refreshTokenService.CreateRefreshTokenAsync(
                new RefreshTokenCreationRequest
                {
                    Subject = subject,
                    AccessToken = accessToken,
                    Client = client!
                });

            return Results.Ok(new LoginResponse(jwt, refreshTokenHandle));
        });

        // POST /api/auth/refresh
        api.MapPost("/refresh", async (
            RefreshRequest request,
            IRefreshTokenService refreshTokenService,
            ITokenCreationService tokenCreation,
            IClientStore clientStore,
            IIssuerNameService issuerNameService,
            UserManager<User> userManager) =>
        {
            var client = await clientStore.FindClientByIdAsync("dashboard");
            var result = await refreshTokenService.ValidateRefreshTokenAsync(request.RefreshToken, client!);

            if (result.IsError)
                return Results.Unauthorized();

            var userId = result.RefreshToken!.SubjectId;
            var user = await userManager.FindByIdAsync(userId);
            if (user is null) return Results.Unauthorized();

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            var issuer = await issuerNameService.GetCurrentAsync();

            var newAccessToken = new Token(IdentityServerConstants.TokenTypes.AccessToken)
            {
                CreationTime = DateTime.UtcNow,
                Issuer = issuer,
                Lifetime = 3600,
                Claims =
                {
                    new Claim(JwtClaimTypes.Subject, user.Id),
                    new Claim(JwtClaimTypes.ClientId, "dashboard"),
                    new Claim(JwtClaimTypes.Email, user.Email!),
                    new Claim(JwtClaimTypes.Role, role),
                    new Claim(JwtClaimTypes.Scope, "api")
                },
                Audiences = { "api" }
            };

            var jwt = await tokenCreation.CreateTokenAsync(newAccessToken);

            var subject = new ClaimsPrincipal(new ClaimsIdentity(newAccessToken.Claims, "idsrv", JwtClaimTypes.Name, JwtClaimTypes.Role));
            var newRefreshTokenHandle = await refreshTokenService.CreateRefreshTokenAsync(
                new RefreshTokenCreationRequest
                {
                    Subject = subject,
                    AccessToken = newAccessToken,
                    Client = client!
                });

            return Results.Ok(new LoginResponse(jwt, newRefreshTokenHandle));
        });

        // GET /api/auth/me
        api.MapGet("/me", async (UserManager<User> userManager, ClaimsPrincipal principal) =>
        {
            var email = principal.FindFirstValue(JwtClaimTypes.Email);
            if (email is null) return Results.Unauthorized();

            var user = await userManager.FindByEmailAsync(email);
            if (user is null) return Results.NotFound();

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            return Results.Ok(new { user.Id, user.FirstName, user.LastName, user.Email, Role = role });
        }).RequireAuthorization();
    }
}
