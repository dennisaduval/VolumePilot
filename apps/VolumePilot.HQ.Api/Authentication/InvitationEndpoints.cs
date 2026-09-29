using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VolumePilot.HQ.Api.Persistence;

namespace VolumePilot.HQ.Api.Authentication;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var staff = endpoints.MapGroup("/api/staff").RequireAuthorization();
        staff.MapGet("", ListStaffAsync);
        staff.MapGet("/invitations", ListInvitationsAsync);
        staff.MapPost("/invitations", InviteAsync).RequireRateLimiting("authentication");
        staff.MapPost("/invitations/{id}/revoke", RevokeAsync);

        endpoints.MapPost("/api/auth/accept-invitation", AcceptAsync)
            .AllowAnonymous().RequireRateLimiting("authentication");
        return endpoints;
    }

    public static IEndpointRouteBuilder MapDevelopmentInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dev/invitations/{id}", GetDevelopmentInvitationAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ListStaffAsync(
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        var companyId = tenant.TenantId;
        if (companyId is null) return SelectCompany();
        if (!await CanManageAsync(principal, companyId, db, ct)) return Results.Forbid();

        var members = await (from membership in db.CompanyMemberships
            join user in db.Users on membership.UserId equals user.Id
            orderby user.Email
            select new StaffResponse(membership.Id, user.Email ?? string.Empty, membership.Role, membership.CreatedAtUtc))
            .ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> ListInvitationsAsync(
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        var companyId = tenant.TenantId;
        if (companyId is null) return SelectCompany();
        if (!await CanManageAsync(principal, companyId, db, ct)) return Results.Forbid();

        return Results.Ok(await db.StaffInvitations.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new InvitationResponse(x.Id, x.Email, x.Role, x.CreatedAtUtc,
                x.ExpiresAtUtc, x.AcceptedAtUtc, x.RevokedAtUtc)).ToListAsync(ct));
    }

    private static async Task<IResult> InviteAsync(
        InviteRequest request, HttpContext context, IAntiforgery antiforgery,
        ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db,
        UserManager<HqUser> users, IInvitationDelivery delivery, CancellationToken ct)
    {
        if (!await antiforgery.IsRequestValidAsync(context)) return InvalidCsrf();
        var companyId = tenant.TenantId;
        if (companyId is null) return SelectCompany();
        if (!await CanManageAsync(principal, companyId, db, ct)) return Results.Forbid();
        if (!delivery.IsAvailable)
            return Results.Problem("Invitation email delivery is not configured.", statusCode: 503);

        var email = request.Email?.Trim();
        if (email is null || email.Length > 254 || !new EmailAddressAttribute().IsValid(email) ||
            request.Role is not ("Admin" or "Staff"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Enter a valid email and choose Admin or Staff."],
            });

        var normalized = users.NormalizeEmail(email);
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null && await db.CompanyMemberships
            .AnyAsync(x => x.UserId == existing.Id, ct))
            return Results.Conflict(new { message = "This person already belongs to the company." });

        var now = DateTimeOffset.UtcNow;
        var previous = await db.StaffInvitations
            .Where(x => x.NormalizedEmail == normalized && x.AcceptedAtUtc == null && x.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var item in previous) item.RevokedAtUtc = now;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invitation = new StaffInvitation
        {
            Id = NewId(), TenantId = companyId, Email = email, NormalizedEmail = normalized,
            Role = request.Role, TokenHash = HashToken(token),
            InvitedByUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7),
        };
        db.StaffInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var company = await db.CompanyAccounts.SingleAsync(ct);
        await delivery.SendAsync(company.Name, email, invitation.Id, token, ct);
        return Results.Created($"/api/staff/invitations/{invitation.Id}",
            new InvitationResponse(invitation.Id, email, invitation.Role, now, invitation.ExpiresAtUtc, null, null));
    }

    private static async Task<IResult> RevokeAsync(
        string id, HttpContext context, IAntiforgery antiforgery, ClaimsPrincipal principal,
        ITenantContext tenant, HqDbContext db, CancellationToken ct)
    {
        if (!await antiforgery.IsRequestValidAsync(context)) return InvalidCsrf();
        var companyId = tenant.TenantId;
        if (companyId is null) return SelectCompany();
        if (!await CanManageAsync(principal, companyId, db, ct)) return Results.Forbid();

        var changed = await db.StaffInvitations
            .Where(x => x.Id == id && x.AcceptedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow), ct);
        return changed == 1 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> AcceptAsync(
        AcceptInvitationRequest request, HttpContext context, IAntiforgery antiforgery,
        ClaimsPrincipal principal, TenantContext tenant, HqDbContext db,
        UserManager<HqUser> users, SignInManager<HqUser> signIn, CancellationToken ct)
    {
        if (!await antiforgery.IsRequestValidAsync(context)) return InvalidCsrf();
        if (string.IsNullOrWhiteSpace(request.InvitationId) || string.IsNullOrWhiteSpace(request.Token))
            return InvalidInvitation();

        var invitation = await db.StaffInvitations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.InvitationId, ct);
        if (invitation is null || invitation.AcceptedAtUtc is not null || invitation.RevokedAtUtc is not null ||
            invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(invitation.TokenHash), Convert.FromHexString(HashToken(request.Token))))
            return InvalidInvitation();

        var user = await users.FindByEmailAsync(invitation.Email);
        if (user is not null && principal.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id)
            return Results.Conflict(new { message = "Sign in with the invited email to accept this invitation." });
        if (user is null && string.IsNullOrWhiteSpace(request.Password))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Set a password to create your account."],
            });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        tenant.SetBootstrapTenant(invitation.TenantId);
        if (user is null)
        {
            user = new HqUser { Email = invitation.Email, UserName = invitation.Email, EmailConfirmed = true };
            var result = await users.CreateAsync(user, request.Password!);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
            }
        }

        if (await db.CompanyMemberships.AnyAsync(x => x.UserId == user.Id, ct))
        {
            await transaction.RollbackAsync(ct);
            return Results.Conflict(new { message = "This person already belongs to the company." });
        }

        var acceptedAt = DateTimeOffset.UtcNow;
        var claimed = await db.StaffInvitations
            .Where(x => x.Id == invitation.Id && x.TokenHash == invitation.TokenHash &&
                x.AcceptedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > acceptedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AcceptedAtUtc, acceptedAt), ct);
        if (claimed != 1)
        {
            await transaction.RollbackAsync(ct);
            return InvalidInvitation();
        }

        db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = NewId(), TenantId = invitation.TenantId, UserId = user.Id,
            Role = invitation.Role, CreatedAtUtc = acceptedAt,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            additionalClaims: [new Claim(HqClaimTypes.ActiveTenantId, invitation.TenantId)]);
        return Results.Ok(new { companyId = invitation.TenantId, role = invitation.Role });
    }

    private static async Task<IResult> GetDevelopmentInvitationAsync(
        string id, ClaimsPrincipal principal, ITenantContext tenant, HqDbContext db,
        DevelopmentInvitationDelivery delivery, CancellationToken ct)
    {
        var companyId = tenant.TenantId;
        if (companyId is null) return SelectCompany();
        if (!await CanManageAsync(principal, companyId, db, ct)) return Results.Forbid();
        if (!await db.StaffInvitations.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
        var message = delivery.Get(id);
        return message is null ? Results.NotFound() : Results.Ok(message);
    }

    private static Task<bool> CanManageAsync(
        ClaimsPrincipal principal, string tenantId, HqDbContext db, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult(false) : db.CompanyMemberships
            .AnyAsync(x => x.UserId == userId && (x.Role == "Owner" || x.Role == "Admin"), ct);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewId() => Ulid.NewUlid().ToString().ToLowerInvariant();
    private static IResult SelectCompany() => Results.Conflict(new { message = "Choose a company first." });
    private static IResult InvalidCsrf() => Results.BadRequest(new { message = "A valid anti-forgery token is required." });
    private static IResult InvalidInvitation() => Results.BadRequest(new { message = "The invitation is invalid or expired." });

    public sealed record InviteRequest(string? Email, string? Role);
    public sealed record AcceptInvitationRequest(string? InvitationId, string? Token, string? Password);
    public sealed record StaffResponse(string MembershipId, string Email, string Role, DateTimeOffset JoinedAtUtc);
    public sealed record InvitationResponse(string Id, string Email, string Role, DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc, DateTimeOffset? AcceptedAtUtc, DateTimeOffset? RevokedAtUtc);
}
