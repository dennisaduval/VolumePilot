using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace VolumePilot.HQ.Api.Persistence;

public static class HqClaimTypes
{
    public const string ActiveTenantId = "vp:active_tenant_id";
}

public sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private string? _bootstrapTenantId;

    public string? TenantId => _bootstrapTenantId ??
        httpContextAccessor.HttpContext?.User.FindFirstValue(HqClaimTypes.ActiveTenantId);

    internal void SetBootstrapTenant(string tenantId) => _bootstrapTenantId = tenantId;
}
