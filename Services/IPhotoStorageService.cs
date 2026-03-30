using Microsoft.AspNetCore.Http;

namespace identityServer.Services;

public interface IPhotoStorageService
{
    Task<string> UploadAsync(string userId, IFormFile file);
    Task DeleteAsync(string userId);
    string? GetPresignedUrl(string? s3Key);
}
