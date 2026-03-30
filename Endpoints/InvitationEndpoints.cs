using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using identityServer.Extensions;
using Microsoft.EntityFrameworkCore;
using identityServer.Data;
using identityServer.DTOs;
using identityServer.Models;
using identityServer.Services;

namespace identityServer.Endpoints;

public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // POST /api/projects/{id}/invite
        api.MapPost("/projects/{id:int}/invite", async (
            AppDbContext db, int id, InviteRequest request,
            ClaimsPrincipal principal, UserManager<User> userManager,
            IEmailService emailService, IConfiguration config) =>
        {
            var currentUserId = principal.GetUserId();
            if (currentUserId is null) return Results.Unauthorized();

            var project = await db.Projects.FindAsync(id);
            if (project is null) return Results.NotFound();

            var inviteeEmail = request.Email.Trim().ToLower();

            var currentUser = await userManager.FindByIdAsync(currentUserId);
            if (currentUser?.Email?.ToLower() == inviteeEmail)
                return Results.BadRequest("You cannot invite yourself.");

            var existingInvite = await db.ProjectInvitations
                .AnyAsync(i => i.ProjectId == id
                    && i.InviteeEmail == inviteeEmail
                    && i.Status == InvitationStatus.Pending
                    && i.ExpiresAt > DateTime.UtcNow);
            if (existingInvite)
                return Results.BadRequest("A pending invitation already exists for this email.");

            var inviteeUser = await userManager.FindByEmailAsync(inviteeEmail);

            var invitation = new ProjectInvitation
            {
                ProjectId = id,
                InvitedByUserId = currentUserId,
                InviteeEmail = inviteeEmail,
                InviteeUserId = inviteeUser?.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                Status = InvitationStatus.Pending
            };

            db.ProjectInvitations.Add(invitation);
            await db.SaveChangesAsync();

            var token = await userManager.GenerateUserTokenAsync(
                currentUser!, "Default", $"InviteToProject:{invitation.Id}");

            var frontendUrl = config["FrontendUrl"] ?? "http://localhost:5173";
            var acceptUrl = $"{frontendUrl}/invitations/accept/{invitation.Id}?token={Uri.EscapeDataString(token)}";
            var inviterName = $"{currentUser!.FirstName} {currentUser.LastName}";

            await emailService.SendInvitationEmailAsync(inviteeEmail, inviterName, project.Name, acceptUrl);

            return Results.Ok(new InvitationDto(
                invitation.Id, invitation.ProjectId, project.Name,
                invitation.InviteeEmail, inviterName,
                invitation.Status.ToString(), invitation.ExpiresAt));
        }).RequireAuthorization("ProjectOwner");

        // POST /api/invitations/accept/{invitationId}?token=...
        api.MapPost("/invitations/accept/{invitationId:int}", async (
            AppDbContext db, int invitationId, string token,
            ClaimsPrincipal principal, UserManager<User> userManager) =>
        {
            var currentUserId = principal.GetUserId();
            if (currentUserId is null) return Results.Unauthorized();

            var invitation = await db.ProjectInvitations
                .Include(i => i.Project)
                .FirstOrDefaultAsync(i => i.Id == invitationId);
            if (invitation is null) return Results.NotFound("Invitation not found.");

            if (invitation.Status != InvitationStatus.Pending)
                return Results.BadRequest($"Invitation is already {invitation.Status.ToString().ToLower()}.");

            if (invitation.ExpiresAt < DateTime.UtcNow)
            {
                invitation.Status = InvitationStatus.Expired;
                await db.SaveChangesAsync();
                return Results.BadRequest("Invitation has expired.");
            }

            var inviter = await userManager.FindByIdAsync(invitation.InvitedByUserId);
            var isValidToken = await userManager.VerifyUserTokenAsync(
                inviter!, "Default", $"InviteToProject:{invitationId}", token);
            if (!isValidToken)
                return Results.BadRequest("Invalid or expired invitation link.");

            var currentUser = await userManager.FindByIdAsync(currentUserId);
            if (currentUser?.Email?.ToLower() != invitation.InviteeEmail)
                return Results.BadRequest("This invitation was sent to a different email address.");

            invitation.Status = InvitationStatus.Accepted;
            invitation.InviteeUserId = currentUserId;

            // Add user as project member if not already
            var alreadyMember = await db.ProjectMembers
                .AnyAsync(m => m.ProjectId == invitation.ProjectId && m.UserId == currentUserId);
            if (!alreadyMember)
            {
                db.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = invitation.ProjectId,
                    UserId = currentUserId
                });
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Member already exists (race condition) — still mark as accepted
                db.ChangeTracker.Clear();
                invitation = await db.ProjectInvitations
                    .Include(i => i.Project)
                    .FirstOrDefaultAsync(i => i.Id == invitation.Id);
                if (invitation is not null)
                {
                    invitation.Status = InvitationStatus.Accepted;
                    invitation.InviteeUserId = currentUserId;
                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok(new
            {
                projectId = invitation!.ProjectId,
                projectName = invitation.Project.Name
            });
        });
    }
}
