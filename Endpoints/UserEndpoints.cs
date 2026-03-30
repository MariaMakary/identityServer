using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using identityServer.DTOs;
using identityServer.Models;
using identityServer.QueryHelpers;
using identityServer.Services;

namespace identityServer.Endpoints;

public static class UserEndpoints
{
    private static readonly HashSet<string> ReservedParams = ["page", "pageSize", "_sort", "_order", "search"];

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/users").RequireAuthorization();

        // GET all users (with pagination, sorting, filtering, search)
        api.MapGet("/", async (UserManager<User> userManager, HttpContext ctx,
            int page = 1, int pageSize = 10, string? _sort = null, string? _order = null, string? search = null) =>
        {
            var filters = ctx.Request.Query
                .Where(kvp => !ReservedParams.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

            var query = userManager.Users.AsQueryable()
                .ApplySearch(search, u => u.FirstName, u => u.LastName, u => u.Email)
                .ApplyFilters(filters)
                .ApplySort(_sort, _order);

            var total = await query.CountAsync();

            var users = await query
                .ApplyPagination(page, pageSize)
                .Select(u => new UserDto(u.Id, u.FirstName, u.LastName, u.Email!))
                .ToListAsync();

            return Results.Ok(new PagedResult<UserDto>(users, total));
        });
        // GET user by id
        api.MapGet("/{id}", async (UserManager<User> userManager, string id, IPhotoStorageService photoStorage) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();

            var photoUrl = photoStorage.GetPresignedUrl(user.PhotoUrl);
            return Results.Ok(new UserDetailDto(user.Id, user.FirstName, user.LastName, user.Email!, photoUrl));
        });

        // POST upload user photo (Admin or own profile)
        api.MapPost("/{id}/photo", async (UserManager<User> userManager, string id, IFormFile photo, IPhotoStorageService photoStorage) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();

            if (photo.Length == 0)
                return Results.BadRequest("No photo file provided.");

            var allowedTypes = new HashSet<string> { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowedTypes.Contains(photo.ContentType))
                return Results.BadRequest("Only JPEG, PNG, WebP, and GIF images are allowed.");

            var s3Key = await photoStorage.UploadAsync(id, photo);
            user.PhotoUrl = s3Key;
            await userManager.UpdateAsync(user);

            var photoUrl = photoStorage.GetPresignedUrl(s3Key);
            return Results.Ok(new { photoUrl });
        }).DisableAntiforgery().RequireAuthorization("AdminOrOwner");

        // DELETE user photo (Admin or own profile)
        api.MapDelete("/{id}/photo", async (UserManager<User> userManager, string id, IPhotoStorageService photoStorage) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();

            await photoStorage.DeleteAsync(id);
            user.PhotoUrl = null;
            await userManager.UpdateAsync(user);

            return Results.NoContent();
        }).RequireAuthorization("AdminOrOwner");

        // PUT update user (Admin only)
        api.MapPut("/{id}", async (UserManager<User> userManager, string id, UpdateUserDto dto) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.Email = dto.Email;
            user.UserName = dto.Email;
            await userManager.UpdateAsync(user);
            return Results.Ok(new UserDto(user.Id, user.FirstName, user.LastName, user.Email!));
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));

        // DELETE user (Admin only)
        api.MapDelete("/{id}", async (UserManager<User> userManager, string id) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            await userManager.DeleteAsync(user);
            return Results.NoContent();
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }
}
