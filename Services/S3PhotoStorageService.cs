using Amazon.S3;
using Amazon.S3.Model;

namespace identityServer.Services;

public class S3PhotoStorageService : IPhotoStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public S3PhotoStorageService(IAmazonS3 s3, IConfiguration config)
    {
        _s3 = s3;
        _bucketName = config["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS:BucketName is not configured.");
    }

    public async Task<string> UploadAsync(string userId, IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName);
        var key = $"photos/{userId}{ext}";

        // Delete any existing photo first
        await DeleteAsync(userId);

        using var stream = file.OpenReadStream();
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType
        };

        await _s3.PutObjectAsync(request);
        return key;
    }

    public async Task DeleteAsync(string userId)
    {
        // List objects with the user's photo prefix to find any extension
        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = $"photos/{userId}."
        };

        var response = await _s3.ListObjectsV2Async(listRequest);
        if (response.S3Objects is not null)
        {
            foreach (var obj in response.S3Objects)
            {
                await _s3.DeleteObjectAsync(_bucketName, obj.Key);
            }
        }
    }

    public string? GetPresignedUrl(string? s3Key)
    {
        if (string.IsNullOrEmpty(s3Key)) return null;

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddHours(1)
        };

        return _s3.GetPreSignedURL(request);
    }
}
