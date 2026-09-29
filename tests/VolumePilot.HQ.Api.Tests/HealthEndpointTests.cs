using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<HqApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(HqApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ApiHealthReturnsServiceStatus()
    {
        using var response = await _client.GetAsync(
            "/api/health",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiHealthResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("VolumePilot HQ API", result.Service);
        Assert.Equal("ok", result.Status);
    }

    private sealed record ApiHealthResponse(string Service, string Status);
}
