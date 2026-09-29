using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using VolumePilot.HQ.Api.Persistence;

namespace VolumePilot.HQ.Api.Work;

public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var organizations = endpoints.MapGroup("/api/organizations").RequireAuthorization();
        organizations.MapGet("", ListOrganizationsAsync);
        organizations.MapPost("", CreateOrganizationAsync);

        var jobs = endpoints.MapGroup("/api/jobs").RequireAuthorization();
        jobs.MapGet("", ListJobsAsync);
        jobs.MapPost("", CreateJobAsync);

        var events = endpoints.MapGroup("/api/events").RequireAuthorization();
        events.MapGet("", ListEventsAsync);
        events.MapPost("", CreateEventAsync);

        return endpoints;
    }

    private static async Task<IResult> ListOrganizationsAsync(
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before viewing its organizations." });
        }

        if (!await CanAccessTenantAsync(principal, tenantContext.TenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var result = await db.ClientOrganizations
            .OrderBy(organization => organization.Name)
            .Select(organization => new OrganizationResponse(
                organization.Id,
                organization.Name,
                organization.OrganizationType,
                organization.CreatedAtUtc,
                organization.UpdatedAtUtc,
                organization.Revision))
            .ToListAsync(cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateOrganizationAsync(
        OrganizationRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before adding an organization." });
        }

        if (!await CanManageWorkAsync(principal, tenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || request.OrganizationType?.Length > 80)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Enter an organization name of 1 to 200 characters."],
            });
        }

        var organization = new ClientOrganization
        {
            Id = NewId(),
            Name = name,
            OrganizationType = string.IsNullOrWhiteSpace(request.OrganizationType)
                ? null
                : request.OrganizationType.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.ClientOrganizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/organizations/{organization.Id}", new OrganizationResponse(
            organization.Id,
            organization.Name,
            organization.OrganizationType,
            organization.CreatedAtUtc,
            organization.UpdatedAtUtc,
            organization.Revision));
    }

    private static async Task<IResult> ListJobsAsync(
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before viewing Jobs." });
        }

        if (!await CanAccessTenantAsync(principal, tenantContext.TenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var result = await db.Jobs
            .OrderByDescending(job => job.CreatedAtUtc)
            .Select(job => new JobResponse(
                job.Id,
                job.ClientOrganizationId,
                job.Name,
                job.Status,
                job.InternalReference,
                job.CreatedAtUtc,
                job.UpdatedAtUtc,
                job.Revision))
            .ToListAsync(cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateJobAsync(
        JobRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before creating a Job." });
        }

        if (!await CanManageWorkAsync(principal, tenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || request.InternalReference?.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Enter a Job name of 1 to 200 characters."],
            });
        }

        var organizationExists = await db.ClientOrganizations
            .AnyAsync(organization => organization.Id == request.ClientOrganizationId, cancellationToken);
        if (!organizationExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientOrganizationId"] = ["Choose an organization in the active company."],
            });
        }

        var job = new Job
        {
            Id = NewId(),
            ClientOrganizationId = request.ClientOrganizationId,
            Name = name,
            InternalReference = string.IsNullOrWhiteSpace(request.InternalReference)
                ? null
                : request.InternalReference.Trim(),
            Status = "Draft",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/jobs/{job.Id}", new JobResponse(
            job.Id,
            job.ClientOrganizationId,
            job.Name,
            job.Status,
            job.InternalReference,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.Revision));
    }

    private static async Task<IResult> ListEventsAsync(
        string? jobId,
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before viewing Events." });
        }

        if (!await CanAccessTenantAsync(principal, tenantContext.TenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var query = db.Events.AsQueryable();
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            query = query.Where(item => item.JobId == jobId);
        }

        var result = await query
            .OrderBy(item => item.StartsAtUtc)
            .Select(item => new EventResponse(
                item.Id,
                item.JobId,
                item.Name,
                item.StartsAtUtc,
                item.EndsAtUtc,
                item.TimeZoneId,
                item.LocationName,
                item.UpdatedAtUtc,
                item.Revision))
            .ToListAsync(cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateEventAsync(
        EventRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        ClaimsPrincipal principal,
        ITenantContext tenantContext,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
        {
            return Results.Conflict(new { message = "Choose a company before creating an Event." });
        }

        if (!await CanManageWorkAsync(principal, tenantId, db, cancellationToken))
        {
            return Results.Forbid();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            request.TimeZoneId?.Length > 100 || request.LocationName?.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Enter an Event name of 1 to 200 characters."],
            });
        }

        if (request.StartsAtUtc.HasValue && request.EndsAtUtc.HasValue &&
            request.EndsAtUtc.Value < request.StartsAtUtc.Value)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["endsAtUtc"] = ["The Event end time must be after its start time."],
            });
        }

        var jobExists = await db.Jobs.AnyAsync(job => job.Id == request.JobId, cancellationToken);
        if (!jobExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["jobId"] = ["Choose a Job in the active company."],
            });
        }

        var captureEvent = new VolumePilot.HQ.Api.Persistence.Event
        {
            Id = NewId(),
            JobId = request.JobId,
            Name = name,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim(),
            LocationName = string.IsNullOrWhiteSpace(request.LocationName) ? null : request.LocationName.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Events.Add(captureEvent);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/events/{captureEvent.Id}", new EventResponse(
            captureEvent.Id,
            captureEvent.JobId,
            captureEvent.Name,
            captureEvent.StartsAtUtc,
            captureEvent.EndsAtUtc,
            captureEvent.TimeZoneId,
            captureEvent.LocationName,
            captureEvent.UpdatedAtUtc,
            captureEvent.Revision));
    }

    private static Task<bool> CanManageWorkAsync(
        ClaimsPrincipal principal,
        string tenantId,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Task.FromResult(false);
        }

        return db.CompanyMemberships
            .IgnoreQueryFilters()
            .AnyAsync(membership => membership.UserId == userId &&
                                    membership.TenantId == tenantId &&
                                    (membership.Role == "Owner" || membership.Role == "Admin"),
                cancellationToken);
    }

    private static Task<bool> CanAccessTenantAsync(
        ClaimsPrincipal principal,
        string tenantId,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Task.FromResult(false);
        }

        return db.CompanyMemberships
            .IgnoreQueryFilters()
            .AnyAsync(membership => membership.UserId == userId && membership.TenantId == tenantId,
                cancellationToken);
    }

    private static string NewId() => Ulid.NewUlid().ToString().ToLowerInvariant();

    public sealed record OrganizationRequest(string? Name, string? OrganizationType);
    public sealed record OrganizationResponse(string Id, string Name, string? OrganizationType, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc, long Revision);
    public sealed record JobRequest(string ClientOrganizationId, string? Name, string? InternalReference);
    public sealed record JobResponse(string Id, string ClientOrganizationId, string Name, string Status, string? InternalReference,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Revision);
    public sealed record EventRequest(
        string JobId,
        string? Name,
        DateTimeOffset? StartsAtUtc,
        DateTimeOffset? EndsAtUtc,
        string? TimeZoneId,
        string? LocationName);
    public sealed record EventResponse(
        string Id,
        string JobId,
        string Name,
        DateTimeOffset? StartsAtUtc,
        DateTimeOffset? EndsAtUtc,
        string? TimeZoneId,
        string? LocationName,
        DateTimeOffset? UpdatedAtUtc,
        long Revision);
}
