using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VolumePilot.HQ.Api.Persistence;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class AuthenticatedWorkflowTests
{
    [Fact]
    public async Task OwnerCanBootstrapAndCreateAnOrganizationJobAndEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var client = factory.CreateClient();

        using var unauthenticatedResponse = await client.GetAsync("/api/organizations", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);

        var csrf = await GetCsrfTokenAsync(client, cancellationToken);
        using var noTokenRequest = new HttpRequestMessage(HttpMethod.Post, "/api/dev/bootstrap")
        {
            Content = JsonContent.Create(new
            {
                companyName = "Test Photography",
                email = "owner@example.test",
                password = "VolumePilot!123",
            }),
        };
        using var noTokenResponse = await client.SendAsync(noTokenRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, noTokenResponse.StatusCode);

        using var bootstrapRequest = new HttpRequestMessage(HttpMethod.Post, "/api/dev/bootstrap")
        {
            Content = JsonContent.Create(new
            {
                companyName = "Test Photography",
                email = "owner@example.test",
                password = "VolumePilot!123",
            }),
        };
        bootstrapRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var bootstrapResponse = await client.SendAsync(bootstrapRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, bootstrapResponse.StatusCode);

        using var sessionResponse = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<UserSession>(cancellationToken);
        Assert.NotNull(session);
        Assert.NotNull(session.ActiveCompanyId);

        using var organizationRequest = new HttpRequestMessage(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = "Lake Charles Youth League", organizationType = "League" }),
        };
        organizationRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var organizationResponse = await client.SendAsync(organizationRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, organizationResponse.StatusCode);
        using var organizationJson = JsonDocument.Parse(
            await organizationResponse.Content.ReadAsStringAsync(cancellationToken));
        var organizationId = organizationJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(organizationId));

        using var jobRequest = new HttpRequestMessage(HttpMethod.Post, "/api/jobs")
        {
            Content = JsonContent.Create(new
            {
                clientOrganizationId = organizationId,
                name = "Fall Photo Day",
                internalReference = "FALL-2026",
            }),
        };
        jobRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var jobResponse = await client.SendAsync(jobRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, jobResponse.StatusCode);
        using var jobJson = JsonDocument.Parse(await jobResponse.Content.ReadAsStringAsync(cancellationToken));
        var jobId = jobJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        using var eventRequest = new HttpRequestMessage(HttpMethod.Post, "/api/events")
        {
            Content = JsonContent.Create(new
            {
                jobId,
                name = "Photo Day",
                startsAtUtc = "2026-10-12T14:00:00Z",
                endsAtUtc = "2026-10-12T20:00:00Z",
                timeZoneId = "America/Chicago",
                locationName = "Main Gym",
            }),
        };
        eventRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var eventResponse = await client.SendAsync(eventRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);

        using var eventsResponse = await client.GetAsync($"/api/events?jobId={jobId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        var events = await eventsResponse.Content.ReadFromJsonAsync<EventResponse[]>(cancellationToken);
        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("Photo Day", events[0].Name);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HqDbContext>();
            await db.CompanyMemberships
                .IgnoreQueryFilters()
                .Where(membership => membership.TenantId == session.ActiveCompanyId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        using var revokedMemberResponse = await client.GetAsync("/api/organizations", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, revokedMemberResponse.StatusCode);
    }

    [Fact]
    public async Task PublicRegistrationEndpointIsNotMapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/auth/csrf", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CsrfResponse>(cancellationToken);
        Assert.NotNull(result?.RequestToken);
        return result.RequestToken;
    }

    private sealed record CsrfResponse(string? RequestToken);
    private sealed record UserSession(string UserId, string Email, string? ActiveCompanyId);
    private sealed record EventResponse(string Id, string JobId, string Name);
}
