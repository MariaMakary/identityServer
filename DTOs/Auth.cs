namespace identityServer.DTOs;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string? RefreshToken = null);
public record RefreshRequest(string RefreshToken);
