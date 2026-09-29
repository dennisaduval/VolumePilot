using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VolumePilot.HQ.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ApiHealthReturnsServiceStatus()
    {
        using var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiHealthResponse>();
        Assert.NotNull(result);
        Assert.Equal("VolumePilot HQ API", result.Service);
        Assert.Equal("ok", result.Status);
    }

    private sealed record ApiHealthResponse(string Service, string Status);
}
