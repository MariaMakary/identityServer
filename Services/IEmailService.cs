namespace identityServer.Services;

public interface IEmailService
{
    Task SendInvitationEmailAsync(string toEmail, string inviterName, string projectName, string acceptUrl);
}
