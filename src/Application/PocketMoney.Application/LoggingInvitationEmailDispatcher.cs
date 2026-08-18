using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PocketMoney.Application.Contract;

namespace PocketMoney.Application;

/// <summary>
/// V1 invitation dispatch: logs the accept URL instead of sending email.
/// SDS §5 mandates SendGrid; that implementation plugs into
/// <see cref="IInvitationEmailDispatcher"/> when a SendGrid key is
/// provisioned (SDS §1.4). Until then the logged URL is how the invited
/// parent accepts in dev/test.
/// </summary>
public sealed class LoggingInvitationEmailDispatcher(
    IConfiguration configuration,
    ILogger<LoggingInvitationEmailDispatcher> logger) : IInvitationEmailDispatcher
{
    public Task DispatchAsync(string invitedEmail, string token, CancellationToken ct = default)
    {
        var baseUrl = configuration["ClientBaseUrl"] ?? "https://pocketmoney.app";
        var acceptUrl = $"{baseUrl.TrimEnd('/')}/invitations/accept?token=***";

        logger.LogInformation(
            "Invitation for {Email} created — SendGrid not wired in V1-dev; accept link: {AcceptUrl}",
            invitedEmail, acceptUrl);

        return Task.CompletedTask;
    }
}
