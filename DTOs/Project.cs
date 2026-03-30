namespace identityServer.DTOs;

public record ProjectDto(int Id, string Name, string Description, int TimeToFinishInDays, string UserId);
public record CreateProjectDto(string Name, string Description, int TimeToFinishInDays);
public record UpdateProjectDto(string Name, string Description, int TimeToFinishInDays);
