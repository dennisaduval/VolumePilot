using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VolumePilot.HQ.Api.Persistence;

namespace VolumePilot.HQ.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapHqAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        }).AllowAnonymous();

        endpoints.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication");

        endpoints.MapGet("/api/auth/me", GetCurrentSessionAsync)
            .RequireAuthorization();

        endpoints.MapPost("/api/auth/active-company", SelectActiveCompanyAsync)
            .RequireAuthorization()
            .RequireRateLimiting("authentication");

        endpoints.MapPost("/api/auth/logout", LogoutAsync)
            .RequireAuthorization();

        return endpoints;
    }

    public static IEndpointRouteBuilder MapDevelopmentBootstrapEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dev/bootstrap-status", async (HqDbContext db, CancellationToken cancellationToken) =>
        {
            var hasCompany = await db.CompanyAccounts.IgnoreQueryFilters()
                .AnyAsync(cancellationToken);
            return Results.Ok(new { requiresBootstrap = !hasCompany });
        }).AllowAnonymous();

        endpoints.MapPost("/api/dev/bootstrap", BootstrapOwnerAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication");

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<HqUser> userManager,
        SignInManager<HqUser> signInManager,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        if (!IsValidEmail(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        var memberships = await db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(membership => membership.UserId == user.Id)
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return Results.Unauthorized();
        }

        var companyIds = memberships.Select(membership => membership.TenantId).ToArray();
        var companies = await db.CompanyAccounts
            .IgnoreQueryFilters()
            .Where(company => companyIds.Contains(company.Id))
            .OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name))
            .ToListAsync(cancellationToken);

        IEnumerable<Claim> claims = memberships.Count == 1
            ? new[] { new Claim(HqClaimTypes.ActiveTenantId, memberships[0].TenantId) }
            : Array.Empty<Claim>();
        await signInManager.SignInWithClaimsAsync(user, request.RememberMe, claims);

        var selectedTenant = memberships.Count == 1 ? memberships[0].TenantId : null;
        return Results.Ok(new UserSession(user.Id, user.Email ?? string.Empty, selectedTenant, companies));
    }

    private static async Task<IResult> GetCurrentSessionAsync(
        ClaimsPrincipal principal,
        UserManager<HqUser> userManager,
        HqDbContext db,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var memberships = await db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(membership => membership.UserId == userId)
            .ToListAsync(cancellationToken);
        var companyIds = memberships.Select(membership => membership.TenantId).ToArray();
        var companies = await db.CompanyAccounts
            .IgnoreQueryFilters()
            .Where(company => companyIds.Contains(company.Id))
            .OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name))
            .ToListAsync(cancellationToken);

        var activeTenantId = tenantContext.TenantId;
        if (activeTenantId is not null && memberships.All(item => item.TenantId != activeTenantId))
        {
            activeTenantId = null;
        }

        return Results.Ok(new UserSession(user.Id, user.Email ?? string.Empty, activeTenantId, companies));
    }

    private static async Task<IResult> SelectActiveCompanyAsync(
        CompanySelectionRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        ClaimsPrincipal principal,
        UserManager<HqUser> userManager,
        SignInManager<HqUser> signInManager,
        HqDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var membership = await db.CompanyMemberships
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.TenantId == request.CompanyId,
                cancellationToken);
        if (membership is null)
        {
            return Results.Forbid();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            additionalClaims: [new Claim(HqClaimTypes.ActiveTenantId, membership.TenantId)]);

        return Results.Ok(new { companyId = membership.TenantId });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<HqUser> signInManager)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> BootstrapOwnerAsync(
        BootstrapOwnerRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<HqUser> userManager,
        SignInManager<HqUser> signInManager,
        HqDbContext db,
        TenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest(new { message = "A valid anti-forgery token is required." });
        }

        var companyName = request.CompanyName?.Trim();
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(companyName) || companyName.Length is < 2 or > 200 ||
            !IsValidEmail(email) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Enter a company name, valid email, and password with at least 12 characters."],
            });
        }

        if (await db.CompanyAccounts.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return Results.Conflict(new { message = "The initial company has already been created." });
        }

        var companyId = Ulid.NewUlid().ToString().ToLowerInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new HqUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
        };
        var createUserResult = await userManager.CreateAsync(user, request.Password);
        if (!createUserResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.ValidationProblem(createUserResult.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        tenantContext.SetBootstrapTenant(companyId);
        db.CompanyAccounts.Add(new CompanyAccount
        {
            Id = companyId,
            Name = companyName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Ulid.NewUlid().ToString().ToLowerInvariant(),
            TenantId = companyId,
            UserId = user.Id,
            Role = "Owner",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await signInManager.SignInWithClaimsAsync(
            user,
            isPersistent: false,
            additionalClaims: [new Claim(HqClaimTypes.ActiveTenantId, companyId)]);

        return Results.Created("/api/auth/me", new UserSession(
            user.Id,
            user.Email ?? email,
            companyId,
            [new CompanyOption(companyId, companyName)]));
    }

    private static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && new EmailAddressAttribute().IsValid(email.Trim());

    public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
    public sealed record CompanySelectionRequest(string CompanyId);
    public sealed record BootstrapOwnerRequest(string? CompanyName, string? Email, string? Password);
    public sealed record CompanyOption(string Id, string Name);
    public sealed record UserSession(string UserId, string Email, string? ActiveCompanyId, IReadOnlyList<CompanyOption> Companies);
}
