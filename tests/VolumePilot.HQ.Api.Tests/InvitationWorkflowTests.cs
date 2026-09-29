using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VolumePilot.HQ.Api.Persistence;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class InvitationWorkflowTests
{
    [Fact]
    public async Task OwnerInvitesStaffWhoCanJoinButCannotInviteOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var owner = factory.CreateClient();
        var csrf = await CsrfAsync(owner, ct);
        using var bootstrap = await PostAsync(owner, "/api/dev/bootstrap",
            new { companyName = "Test Photography", email = "owner@example.test", password = "VolumePilot!123" }, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
        csrf = await CsrfAsync(owner, ct);

        using var invalid = await PostAsync(owner, "/api/staff/invitations",
            new { email = "staff@example.test", role = "Owner" }, csrf, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var invited = await PostAsync(owner, "/api/staff/invitations",
            new { email = "staff@example.test", role = "Staff" }, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var invitation = await invited.Content.ReadFromJsonAsync<InvitationResponse>(ct);
        Assert.NotNull(invitation);

        using var inbox = await owner.GetAsync($"/api/dev/invitations/{invitation.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        var message = await inbox.Content.ReadFromJsonAsync<DeliveredInvitation>(ct);
        Assert.NotNull(message);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HqDbContext>();
            var saved = await db.StaffInvitations.IgnoreQueryFilters().SingleAsync(ct);
            Assert.NotEqual(message.Token, saved.TokenHash);
        }

        using var joiner = factory.CreateClient();
        var joinCsrf = await CsrfAsync(joiner, ct);
        using var wrong = await PostAsync(joiner, "/api/auth/accept-invitation",
            new { invitationId = invitation.Id, token = "wrong", password = "VolumePilot!123" }, joinCsrf, ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        using var joined = await PostAsync(joiner, "/api/auth/accept-invitation",
            new { invitationId = invitation.Id, token = message.Token, password = "VolumePilot!123" }, joinCsrf, ct);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);

        joinCsrf = await CsrfAsync(joiner, ct);
        using var forbidden = await PostAsync(joiner, "/api/staff/invitations",
            new { email = "another@example.test", role = "Admin" }, joinCsrf, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var replay = await PostAsync(owner, "/api/auth/accept-invitation",
            new { invitationId = invitation.Id, token = message.Token }, csrf, ct);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task RevokedInvitationCannotBeAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new HqApiFactory();
        using var owner = factory.CreateClient();
        var csrf = await CsrfAsync(owner, ct);
        using var bootstrap = await PostAsync(owner, "/api/dev/bootstrap",
            new { companyName = "Test Photography", email = "owner@example.test", password = "VolumePilot!123" }, csrf, ct);
        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
        csrf = await CsrfAsync(owner, ct);

        using var invited = await PostAsync(owner, "/api/staff/invitations",
            new { email = "admin@example.test", role = "Admin" }, csrf, ct);
        var invitation = await invited.Content.ReadFromJsonAsync<InvitationResponse>(ct);
        Assert.NotNull(invitation);
        using var inbox = await owner.GetAsync($"/api/dev/invitations/{invitation.Id}", ct);
        var message = await inbox.Content.ReadFromJsonAsync<DeliveredInvitation>(ct);
        Assert.NotNull(message);

        using var revoked = await PostAsync(owner, $"/api/staff/invitations/{invitation.Id}/revoke", new { }, csrf, ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var joiner = factory.CreateClient();
        using var rejected = await PostAsync(joiner, "/api/auth/accept-invitation",
            new { invitationId = invitation.Id, token = message.Token, password = "VolumePilot!123" },
            await CsrfAsync(joiner, ct), ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private static async Task<string> CsrfAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync("/api/auth/csrf", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsrfResponse>(ct))!.RequestToken!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string csrf, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request, ct);
    }

    private sealed record CsrfResponse(string? RequestToken);
    private sealed record InvitationResponse(string Id);
    private sealed record DeliveredInvitation(string InvitationId, string CompanyName, string Email, string Token);
}
