namespace VolumePilot.HQ.Api.Persistence;

/// <summary>
/// Identifies the company selected by the authenticated request or device credential.
/// Implementations must derive this value from trusted authentication state, never from
/// an unvalidated request parameter.
/// </summary>
public interface ITenantContext
{
    string? TenantId { get; }
}
