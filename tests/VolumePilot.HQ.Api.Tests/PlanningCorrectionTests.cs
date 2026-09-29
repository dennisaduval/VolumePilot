using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VolumePilot.HQ.Api.Persistence;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class PlanningCorrectionTests
{
    [Fact]
    public async Task OwnerCanCorrectPlanningRecordsWithRevisionAndActivity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var client = factory.CreateClient();
        var csrf = await TokenAsync(client, ct);
        using (var bootstrap = await SendAsync(client, HttpMethod.Post, "/api/dev/bootstrap",
            new { companyName = "Test Photography", email = "owner@example.test", password = "VolumePilot!123" }, csrf, ct))
            Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
        csrf = await TokenAsync(client, ct);

        using var org = await SendAsync(client, HttpMethod.Post, "/api/organizations",
            new { name = "Old League", organizationType = "League" }, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, org.StatusCode);
        using var orgJson = JsonDocument.Parse(await org.Content.ReadAsStringAsync(ct));
        var id = orgJson.RootElement.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.Equal(1, orgJson.RootElement.GetProperty("revision").GetInt64());

        using var corrected = await SendAsync(client, HttpMethod.Put, $"/api/organizations/{id}",
            new { name = "Corrected League", organizationType = "School", revision = 1 }, csrf, ct);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        using var correctedJson = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync(ct));
        Assert.Equal(2, correctedJson.RootElement.GetProperty("revision").GetInt64());

        using var stale = await SendAsync(client, HttpMethod.Put, $"/api/organizations/{id}",
            new { name = "Overwrite", organizationType = "Club", revision = 1 }, csrf, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var activity = await client.GetAsync($"/api/activity?entityType=Organization&entityId={id}", ct);
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        using var activityJson = JsonDocument.Parse(await activity.Content.ReadAsStringAsync(ct));
        Assert.Single(activityJson.RootElement.EnumerateArray());
        Assert.Equal("owner@example.test", activityJson.RootElement[0].GetProperty("actorEmail").GetString());

        using var list = await client.GetAsync("/api/organizations", ct);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct));
        Assert.Equal("Corrected League", listJson.RootElement[0].GetProperty("name").GetString());

        var jobId = await CreateIdAsync(client, "/api/jobs", new { clientOrganizationId = id, name = "Old Job" }, csrf, ct);
        var eventId = await CreateIdAsync(client, "/api/events", new { jobId, name = "Old Event" }, csrf, ct);
        foreach (var (path, body) in new (string, object)[]
        {
            ($"/api/jobs/{jobId}", new { name = "New Job", internalReference = "SPRING", revision = 1 }),
            ($"/api/events/{eventId}", new { name = "New Event", startsAtUtc = "2027-01-12T14:00:00Z", endsAtUtc = "2027-01-12T16:00:00Z", timeZoneId = "America/Chicago", locationName = "Main Gym", revision = 1 }),
        })
        {
            using var updated = await SendAsync(client, HttpMethod.Put, path, body, csrf, ct);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            using var updateJson = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(ct));
            Assert.Equal(2L, updateJson.RootElement.GetProperty("revision").GetInt64());
            using var rejected = await SendAsync(client, HttpMethod.Put, path, body, csrf, ct);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        using var invalidDate = await SendAsync(client, HttpMethod.Put, $"/api/events/{eventId}",
            new { name = "Invalid", startsAtUtc = "2027-01-12T16:00:00Z", endsAtUtc = "2027-01-12T14:00:00Z", revision = 2 }, csrf, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);
        using var allActivity = await client.GetAsync("/api/activity", ct);
        using var allHistory = JsonDocument.Parse(await allActivity.Content.ReadAsStringAsync(ct));
        Assert.Equal(3, allHistory.RootElement.GetArrayLength());
        foreach (var entry in allHistory.RootElement.EnumerateArray())
            Assert.NotEqual(entry.GetProperty("beforeJson").GetString(), entry.GetProperty("afterJson").GetString());
    }

    [Fact]
    public async Task CorrectionsRequireCsrfAndManageRoleAndRemovedMembersCannotReadHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var owner = factory.CreateClient();
        var csrf = await BootstrapAsync(owner, ct);
        var orgId = await CreateIdAsync(owner, "/api/organizations", new { name = "School" }, csrf, ct);
        var jobId = await CreateIdAsync(owner, "/api/jobs", new { clientOrganizationId = orgId, name = "Job" }, csrf, ct);
        var eventId = await CreateIdAsync(owner, "/api/events", new { jobId, name = "Event" }, csrf, ct);
        var paths = new[] { $"/api/organizations/{orgId}", $"/api/jobs/{jobId}", $"/api/events/{eventId}" };
        var body = new { name = "Changed", revision = 1 };
        using var anonymous = factory.CreateClient();
        foreach (var path in paths)
        {
            using var missingToken = await SendAsync(owner, HttpMethod.Put, path, body, null, ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
            using var signedOut = await SendAsync(anonymous, HttpMethod.Put, path, body, null, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HqDbContext>();
        await db.CompanyMemberships.IgnoreQueryFilters().ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.Role, "Staff"), ct);
        foreach (var path in paths)
        {
            using var forbidden = await SendAsync(owner, HttpMethod.Put, path, body, csrf, ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var visible = await owner.GetAsync("/api/activity", ct);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        using var history = JsonDocument.Parse(await visible.Content.ReadAsStringAsync(ct));
        Assert.Empty(history.RootElement.EnumerateArray());
        await db.CompanyMemberships.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
        using var removedRead = await owner.GetAsync("/api/activity", ct);
        Assert.Equal(HttpStatusCode.Forbidden, removedRead.StatusCode);
        using var removedWrite = await SendAsync(owner, HttpMethod.Put, paths[0], body, csrf, ct);
        Assert.Equal(HttpStatusCode.Forbidden, removedWrite.StatusCode);
    }

    [Fact]
    public async Task OtherCompanyRecordsAndHistoryAreNotVisibleOrWritable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var owner = factory.CreateClient();
        var csrf = await BootstrapAsync(owner, ct);
        var tenantId = Ulid.NewUlid().ToString();
        var orgId = Ulid.NewUlid().ToString();
        var jobId = Ulid.NewUlid().ToString();
        var eventId = Ulid.NewUlid().ToString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<HqDbContext>>();
            await using var db = new HqDbContext(options, new FixedTenantContext(tenantId));
            var userId = (await db.Users.SingleAsync(ct)).Id;
            db.CompanyAccounts.Add(new CompanyAccount { Id = tenantId, Name = "Other Company" });
            db.ClientOrganizations.Add(new ClientOrganization { Id = orgId, Name = "Other School" });
            db.Jobs.Add(new Job { Id = jobId, ClientOrganizationId = orgId, Name = "Other Job" });
            db.Events.Add(new Event { Id = eventId, JobId = jobId, Name = "Other Event" });
            db.ActivityRecords.Add(new ActivityRecord
            {
                Id = Ulid.NewUlid().ToString(), ActorUserId = userId, EntityType = "Organization",
                EntityId = orgId, Action = "Updated", BeforeJson = "{}", AfterJson = "{}", OccurredAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        foreach (var path in new[] { $"/api/organizations/{orgId}", $"/api/jobs/{jobId}", $"/api/events/{eventId}" })
        {
            using var correction = await SendAsync(owner, HttpMethod.Put, path, new { name = "Attempt", revision = 1 }, csrf, ct);
            Assert.Equal(HttpStatusCode.NotFound, correction.StatusCode);
        }
        using var activity = await owner.GetAsync($"/api/activity?entityType=Organization&entityId={orgId}", ct);
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        using var json = JsonDocument.Parse(await activity.Content.ReadAsStringAsync(ct));
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    private static async Task<string> BootstrapAsync(HttpClient client, CancellationToken ct)
    {
        var csrf = await TokenAsync(client, ct);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/dev/bootstrap",
            new { companyName = "Test Photography", email = "owner@example.test", password = "VolumePilot!123" }, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await TokenAsync(client, ct);
    }

    private static async Task<string> CreateIdAsync(HttpClient client, string path, object body, string csrf, CancellationToken ct)
    {
        using var response = await SendAsync(client, HttpMethod.Post, path, body, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> TokenAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync("/api/auth/csrf", ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object data, string? csrf, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(data) };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request, ct);
    }

    private sealed record FixedTenantContext(string? TenantId) : ITenantContext;
}
