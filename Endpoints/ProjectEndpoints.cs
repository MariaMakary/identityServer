using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using identityServer.Extensions;
using identityServer.Data;
using identityServer.DTOs;
using identityServer.Models;
using identityServer.QueryHelpers;

namespace identityServer.Endpoints;

public static class ProjectEndpoints
{
    private static readonly HashSet<string> ReservedParams = ["page", "pageSize", "_sort", "_order", "search", "userId"];

    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/projects").RequireAuthorization();

        // GET all projects (with pagination, sorting, filtering, search)
        api.MapGet("/", async (AppDbContext db, HttpContext ctx, ClaimsPrincipal principal,
            int page = 1, int pageSize = 10, string? _sort = null, string? _order = null,
            string? search = null, string? userId = null) =>
        {
            var currentUserId = principal.GetUserId();
            var isAdmin = principal.IsAdmin();

            var query = db.Projects.AsQueryable();

            // Non-admins can see their own projects + projects they are a member of
            if (!isAdmin)
            {
                var memberProjectIds = db.ProjectMembers
                    .Where(m => m.UserId == currentUserId)
                    .Select(m => m.ProjectId);

                query = query.Where(p => p.UserId == currentUserId || memberProjectIds.Contains(p.Id));
            }
            else if (userId is not null)
                query = query.Where(p => p.UserId == userId);

            var filters = ctx.Request.Query
                .Where(kvp => !ReservedParams.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

            query = query
                .ApplySearch(search, p => p.Name, p => p.Description)
                .ApplyFilters(filters)
                .ApplySort(_sort, _order);

            var total = await query.CountAsync();

            var projects = await query
                .ApplyPagination(page, pageSize)
                .Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.TimeToFinishInDays, p.UserId))
                .ToListAsync();

            return Results.Ok(new PagedResult<ProjectDto>(projects, total));
        });

        // GET project by id
        api.MapGet("/{id:int}", async (AppDbContext db, int id) =>
        {
            var project = await db.Projects.FindAsync(id);
            if (project is null) return Results.NotFound();

            return Results.Ok(new ProjectDto(project.Id, project.Name, project.Description, project.TimeToFinishInDays, project.UserId));
        }).RequireAuthorization("ProjectOwner");

        // POST create project
        api.MapPost("/", async (AppDbContext db, CreateProjectDto dto, ClaimsPrincipal principal) =>
        {
            var currentUserId = principal.GetUserId();
            if (currentUserId is null) return Results.Unauthorized();

            var project = new Project
            {
                Name = dto.Name,
                Description = dto.Description,
                TimeToFinishInDays = dto.TimeToFinishInDays,
                UserId = currentUserId
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync();

            return Results.Created($"/api/projects/{project.Id}",
                new ProjectDto(project.Id, project.Name, project.Description, project.TimeToFinishInDays, project.UserId));
        });

        // PUT update project
        api.MapPut("/{id:int}", async (AppDbContext db, int id, UpdateProjectDto dto) =>
        {
            var project = await db.Projects.FindAsync(id);
            if (project is null) return Results.NotFound();

            project.Name = dto.Name;
            project.Description = dto.Description;
            project.TimeToFinishInDays = dto.TimeToFinishInDays;

            await db.SaveChangesAsync();

            return Results.Ok(new ProjectDto(project.Id, project.Name, project.Description, project.TimeToFinishInDays, project.UserId));
        }).RequireAuthorization("ProjectOwner");

        // DELETE project
        api.MapDelete("/{id:int}", async (AppDbContext db, int id) =>
        {
            var project = await db.Projects.FindAsync(id);
            if (project is null) return Results.NotFound();

            db.Projects.Remove(project);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization("ProjectOwner");
    }
}
