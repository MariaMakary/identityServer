namespace identityServer.DTOs;

public record InviteRequest(string Email);
public record InvitationDto(int Id, int ProjectId, string ProjectName, string InviteeEmail, string InvitedByName, string Status, DateTime ExpiresAt);
