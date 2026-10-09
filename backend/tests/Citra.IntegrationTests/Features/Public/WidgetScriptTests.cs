using System.Net;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Public;

/// <summary>C-224: websites load the widget script from the API, and browsers cache it.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class WidgetScriptTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task The_api_serves_the_widget_script_with_caching_headers()
    {
        await using var api = new ApiFactory(fixture);
        var client = api.CreateClient();

        using var response = await client.GetAsync("/widget.js", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=3600");
        response.Headers.ETag.ShouldNotBeNull();
    }
}
