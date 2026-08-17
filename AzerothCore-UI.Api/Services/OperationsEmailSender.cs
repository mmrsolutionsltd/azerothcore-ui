using System.Net;
using System.Net.Mail;

namespace AzerothCore_UI.Api.Services;

public sealed class OperationsEmailSender(IConfiguration configuration)
{
    private readonly string? host = configuration["Operations:Email:SmtpHost"];
    private readonly int port = configuration.GetValue("Operations:Email:SmtpPort", 587);
    private readonly bool useSsl = configuration.GetValue("Operations:Email:UseSsl", true);
    private readonly string? username = configuration["Operations:Email:Username"];
    private readonly string? password = configuration["Operations:Email:Password"];
    private readonly string? fromAddress = configuration["Operations:Email:FromAddress"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(host)
        && !string.IsNullOrWhiteSpace(fromAddress)
        && (string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password));

    public string Status => IsConfigured
        ? $"SMTP delivery is configured through {host}:{port}."
        : "Email delivery is not configured on the server; alerts will still appear on this dashboard.";

    public async Task SendAsync(
        string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        using var message = new MailMessage(fromAddress!, recipient, subject, body);
        using var client = new SmtpClient(host!, port)
        {
            EnableSsl = useSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(username),
            Credentials = string.IsNullOrWhiteSpace(username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(username, password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
