namespace identityServer.Models;

public enum InvitationStatus { Pending, Accepted, Expired, Cancelled }

public class ProjectInvitation
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string InvitedByUserId { get; set; } = string.Empty;
    public User InvitedByUser { get; set; } = null!;
    public string InviteeEmail { get; set; } = string.Empty;
    public string? InviteeUserId { get; set; }
    public User? InviteeUser { get; set; }
    public DateTime ExpiresAt { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
