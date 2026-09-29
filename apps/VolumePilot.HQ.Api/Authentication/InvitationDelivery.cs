using System.Collections.Concurrent;

namespace VolumePilot.HQ.Api.Authentication;

public interface IInvitationDelivery
{
    bool IsAvailable { get; }
    Task SendAsync(string companyName, string email, string invitationId, string token, CancellationToken cancellationToken);
}

// A local inbox makes the invitation flow testable without sending real mail.
// No delivery implementation is registered outside Development/Testing yet.
public sealed class DevelopmentInvitationDelivery : IInvitationDelivery
{
    private readonly ConcurrentDictionary<string, DeliveredInvitation> _messages = new();

    public bool IsAvailable => true;

    public Task SendAsync(string companyName, string email, string invitationId, string token, CancellationToken cancellationToken)
    {
        _messages[invitationId] = new DeliveredInvitation(invitationId, companyName, email, token);
        return Task.CompletedTask;
    }

    public DeliveredInvitation? Get(string invitationId) =>
        _messages.TryGetValue(invitationId, out var message) ? message : null;
}

public sealed record DeliveredInvitation(string InvitationId, string CompanyName, string Email, string Token);

public sealed class UnavailableInvitationDelivery : IInvitationDelivery
{
    public bool IsAvailable => false;

    public Task SendAsync(string companyName, string email, string invitationId, string token, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Configure an invitation delivery provider before inviting staff.");
}
