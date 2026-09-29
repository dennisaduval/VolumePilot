using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using VolumePilot.HQ.Api.Persistence;

namespace VolumePilot.HQ.Api.Work;

public static class CorrectionEndpoints
{
    public static IEndpointRouteBuilder MapCorrectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/organizations/{id}", UpdateOrganizationAsync).RequireAuthorization();
        endpoints.MapPut("/api/jobs/{id}", UpdateJobAsync).RequireAuthorization();
        endpoints.MapPut("/api/events/{id}", UpdateEventAsync).RequireAuthorization();
        endpoints.MapGet("/api/activity", ListActivityAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> UpdateOrganizationAsync(
        string id, OrganizationCorrection request, HttpContext context, IAntiforgery antiforgery,
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        var check = await CheckWriteAsync(context, antiforgery, principal, tenant, db, ct);
        if (check is not null) return check;
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || request.OrganizationType?.Length > 80 || request.Revision < 1)
            return Invalid("name", "Enter a name of 1 to 200 characters and a valid revision.");

        var item = await db.ClientOrganizations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (item.Revision != request.Revision) return Stale();
        var before = JsonSerializer.Serialize(new { item.Name, item.OrganizationType });
        item.Name = name;
        item.OrganizationType = Normalize(request.OrganizationType);
        item.Revision++;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivity(db, tenant.TenantId!, principal, "Organization", item.Id, before,
            JsonSerializer.Serialize(new { item.Name, item.OrganizationType }), item.UpdatedAtUtc.Value);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        return Results.Ok(new OrganizationCorrectionResponse(item.Id, item.Name, item.OrganizationType, item.CreatedAtUtc, item.UpdatedAtUtc, item.Revision));
    }

    private static async Task<IResult> UpdateJobAsync(
        string id, JobCorrection request, HttpContext context, IAntiforgery antiforgery,
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        var check = await CheckWriteAsync(context, antiforgery, principal, tenant, db, ct);
        if (check is not null) return check;
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || request.InternalReference?.Length > 100 || request.Revision < 1)
            return Invalid("name", "Enter a Job name of 1 to 200 characters and a valid revision.");

        var item = await db.Jobs.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (item.Revision != request.Revision) return Stale();
        var before = JsonSerializer.Serialize(new { item.Name, item.InternalReference });
        item.Name = name;
        item.InternalReference = Normalize(request.InternalReference);
        item.Revision++;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivity(db, tenant.TenantId!, principal, "Job", item.Id, before,
            JsonSerializer.Serialize(new { item.Name, item.InternalReference }), item.UpdatedAtUtc.Value);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        return Results.Ok(new JobCorrectionResponse(item.Id, item.ClientOrganizationId, item.Name, item.Status,
            item.InternalReference, item.CreatedAtUtc, item.UpdatedAtUtc, item.Revision));
    }

    private static async Task<IResult> UpdateEventAsync(
        string id, EventCorrection request, HttpContext context, IAntiforgery antiforgery,
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        var check = await CheckWriteAsync(context, antiforgery, principal, tenant, db, ct);
        if (check is not null) return check;
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || request.TimeZoneId?.Length > 100 ||
            request.LocationName?.Length > 200 || request.Revision < 1)
            return Invalid("name", "Enter an Event name of 1 to 200 characters and a valid revision.");
        if (request.StartsAtUtc.HasValue && request.EndsAtUtc.HasValue && request.EndsAtUtc.Value < request.StartsAtUtc.Value)
            return Invalid("endsAtUtc", "The Event end time must be after its start time.");

        var item = await db.Events.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (item.Revision != request.Revision) return Stale();
        var before = JsonSerializer.Serialize(new
        {
            item.Name, item.StartsAtUtc, item.EndsAtUtc, item.TimeZoneId, item.LocationName,
        });
        item.Name = name;
        item.StartsAtUtc = request.StartsAtUtc;
        item.EndsAtUtc = request.EndsAtUtc;
        item.TimeZoneId = Normalize(request.TimeZoneId);
        item.LocationName = Normalize(request.LocationName);
        item.Revision++;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddActivity(db, tenant.TenantId!, principal, "Event", item.Id, before,
            JsonSerializer.Serialize(new { item.Name, item.StartsAtUtc, item.EndsAtUtc, item.TimeZoneId, item.LocationName }),
            item.UpdatedAtUtc.Value);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        return Results.Ok(new EventCorrectionResponse(item.Id, item.JobId, item.Name, item.StartsAtUtc,
            item.EndsAtUtc, item.TimeZoneId, item.LocationName, item.UpdatedAtUtc, item.Revision));
    }

    private static async Task<IResult> ListActivityAsync(
        string? entityType, string? entityId, ClaimsPrincipal principal, ITenantContext tenant,
        HqDbContext db, CancellationToken ct)
    {
        if (tenant.TenantId is null) return SelectCompany();
        if (!await CanAccessAsync(principal, tenant.TenantId, db, ct)) return Results.Forbid();
        if (entityType is not null && entityType is not ("Organization" or "Job" or "Event"))
            return Invalid("entityType", "Choose Organization, Job, or Event.");
        var query = db.ActivityRecords.AsQueryable();
        if (entityType is not null) query = query.Where(x => x.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(x => x.EntityId == entityId);
        var result = await (from record in query
            join user in db.Users on record.ActorUserId equals user.Id
            orderby record.OccurredAtUtc descending
            select new ActivityResponse(record.Id, record.EntityType, record.EntityId, record.Action,
                user.Email ?? string.Empty, record.BeforeJson, record.AfterJson, record.OccurredAtUtc))
            .Take(100).ToListAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult?> CheckWriteAsync(
        HttpContext context, IAntiforgery antiforgery, ClaimsPrincipal principal,
        ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        if (tenant.TenantId is null) return SelectCompany();
        return await CanManageAsync(principal, tenant.TenantId, db, ct) ? null : Results.Forbid();
    }

    private static Task<bool> CanManageAsync(ClaimsPrincipal principal, string tenantId, HqDbContext db, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult(false) : db.CompanyMemberships.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.UserId == userId &&
                (x.Role == "Owner" || x.Role == "Admin"), ct);
    }

    private static Task<bool> CanAccessAsync(ClaimsPrincipal principal, string tenantId, HqDbContext db, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult(false) : db.CompanyMemberships.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
    }

    private static void AddActivity(HqDbContext db, string tenantId, ClaimsPrincipal principal,
        string entityType, string entityId, string before, string after, DateTimeOffset occurredAt)
    {
        db.ActivityRecords.Add(new ActivityRecord
        {
            Id = Ulid.NewUlid().ToString().ToLowerInvariant(), TenantId = tenantId,
            ActorUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!, EntityType = entityType,
            EntityId = entityId, Action = "Updated", BeforeJson = before, AfterJson = after,
            OccurredAtUtc = occurredAt,
        });
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Stale() => Results.Conflict(new { message = "This record changed since you opened it. Cancel your edit and reopen it to use the latest version." });
    private static IResult SelectCompany() => Results.Conflict(new { message = "Choose a company first." });
    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public sealed record OrganizationCorrection(string? Name, string? OrganizationType, long Revision);
    public sealed record JobCorrection(string? Name, string? InternalReference, long Revision);
    public sealed record EventCorrection(string? Name, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc,
        string? TimeZoneId, string? LocationName, long Revision);
    public sealed record OrganizationCorrectionResponse(string Id, string Name, string? OrganizationType,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Revision);
    public sealed record JobCorrectionResponse(string Id, string ClientOrganizationId, string Name, string Status,
        string? InternalReference, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Revision);
    public sealed record EventCorrectionResponse(string Id, string JobId, string Name, DateTimeOffset? StartsAtUtc,
        DateTimeOffset? EndsAtUtc, string? TimeZoneId, string? LocationName, DateTimeOffset? UpdatedAtUtc, long Revision);
    public sealed record ActivityResponse(string Id, string EntityType, string EntityId, string Action,
        string ActorEmail, string BeforeJson, string AfterJson, DateTimeOffset OccurredAtUtc);
}
