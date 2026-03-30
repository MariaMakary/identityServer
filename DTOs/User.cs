namespace identityServer.DTOs;

public record UserDto(string Id, string FirstName, string LastName, string Email);
public record UserDetailDto(string Id, string FirstName, string LastName, string Email, string? PhotoUrl);
public record UpdateUserDto(string FirstName, string LastName, string Email);
public record UploadPhotoDto(string Data, string ContentType, string FileName);
