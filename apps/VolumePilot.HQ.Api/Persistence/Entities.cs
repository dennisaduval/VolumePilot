namespace VolumePilot.HQ.Api.Persistence;

public interface ITenantOwned
{
    string TenantId { get; set; }
}

public sealed class CompanyAccount
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CompanyMembership : ITenantOwned
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ClientOrganization : ITenantOwned
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? OrganizationType { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class Job : ITenantOwned
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientOrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? InternalReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class Event : ITenantOwned
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? StartsAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public string? TimeZoneId { get; set; }
    public string? LocationName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
