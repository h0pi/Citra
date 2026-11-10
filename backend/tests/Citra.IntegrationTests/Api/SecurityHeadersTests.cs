using System.Net;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Citra.IntegrationTests.Api;

/// <summary>C-268: security headers on every response — data, errors and the widget script.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecurityHeadersTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/widget.js", HttpStatusCode.OK)]
    [InlineData("/api/bots", HttpStatusCode.Unauthorized)]
    [InlineData("/api/no-such-endpoint", HttpStatusCode.NotFound)]
    public async Task Every_response_has_the_security_headers(string path, HttpStatusCode status)
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
    }

    [Fact]
    public async Task Https_responses_tell_browsers_to_stay_on_https()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://citra.example.test") });

        using var response = await client.GetAsync("/health/live", Ct);

        response.Headers.GetValues("Strict-Transport-Security").ShouldBe(["max-age=31536000"]);
    }
}
