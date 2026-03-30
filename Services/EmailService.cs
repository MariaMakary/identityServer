using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace identityServer.Services;

public class EmailService(IConfiguration config) : IEmailService
{
    public async Task SendInvitationEmailAsync(string toEmail, string inviterName, string projectName, string acceptUrl)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            config["Smtp:FromName"] ?? "Users Dashboard",
            config["Smtp:FromEmail"]));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"You've been invited to join \"{projectName}\"";

        message.Body = new TextPart("html")
        {
            Text = $"""
                <h2>Project Invitation</h2>
                <p><strong>{inviterName}</strong> has invited you to join the project <strong>"{projectName}"</strong>.</p>
                <p><a href="{acceptUrl}" style="display:inline-block;padding:12px 24px;background:#1890ff;color:#fff;text-decoration:none;border-radius:4px;">Accept Invitation</a></p>
                <p>If you don't have an account, please register first, then click the link again.</p>
                """
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            config["Smtp:Host"],
            int.Parse(config["Smtp:Port"] ?? "587"),
            SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(config["Smtp:Username"], config["Smtp:Password"]);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
