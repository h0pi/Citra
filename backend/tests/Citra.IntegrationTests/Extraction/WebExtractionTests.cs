using System.Net;
using System.Net.Sockets;
using System.Text;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Web;
using Citra.Domain.Documents;
using Citra.Infrastructure.Extraction;

namespace Citra.IntegrationTests.Extraction;

/// <summary>
/// Web pages and SSRF protection at download time. A fake DNS maps test names to addresses;
/// connections always go to the local test server, whatever address was chosen, so we can see exactly which
/// addresses the fetcher agreed to use.
/// </summary>
public sealed class WebExtractionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, IPAddress[]> FakeDns = new()
    {
        ["site.test"] = [IPAddress.Loopback],
        ["other.test"] = [IPAddress.Loopback],
        ["evil.test"] = [IPAddress.Parse("10.0.0.5")],                     // internal network
        ["metadata.test"] = [IPAddress.Parse("169.254.169.254")],          // cloud credentials
        ["mixed.test"] = [IPAddress.Parse("192.168.1.1"), IPAddress.Loopback],
    };

    // ---------- Reading pages ----------

    [Fact]
    public async Task Keeps_the_main_content_and_the_page_title()
    {
        const string html = """
            <html><head><title>Returns | Acme Help</title><script>track()</script></head>
            <body>
              <header><a href="/">Acme</a> <nav><a>Home</a><a>Shop</a></nav></header>
              <div id="cookie-banner">We use cookies. <button>Accept</button></div>
              <main>
                <article>
                  <header><h1>Returns policy</h1><p>Updated March 2026</p></header>
                  <p>Items can be returned within <b>30 days</b>.</p>
                  <h2>How to return</h2>
                  <ol><li>Open your order</li><li>Click "Return"</li></ol>
                  <table><tr><th>Item</th><th>Days</th></tr><tr><td>Shoes</td><td>30</td></tr></table>
                </article>
              </main>
              <aside>Related: gift cards</aside>
              <footer>© Acme · Privacy</footer>
            </body></html>
            """;

        var result = await ExtractAsync(new() { ["/returns"] = TestResponse.Html(html) }, "http://site.test/returns");
        var text = result.Pages.ShouldHaveSingleItem().Text;

        result.Title.ShouldBe("Returns | Acme Help");
        text.ShouldContain("# Returns policy");
        text.ShouldContain("Updated March 2026", customMessage: "an article's own header stays");
        text.ShouldContain("Items can be returned within 30 days.");
        text.ShouldContain("## How to return");
        text.ShouldContain("- Open your order");
        text.ShouldContain("Item | Days\nShoes | 30");
        foreach (var noise in new[] { "track()", "Home", "Shop", "cookies", "Accept", "gift cards", "Privacy" })
        {
            text.ShouldNotContain(noise);
        }
    }

    [Fact]
    public async Task Uses_the_meta_charset_of_older_pages()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var body = Encoding.GetEncoding(1250).GetBytes(
            "<html><head><meta charset=\"windows-1250\"></head><body><p>Radno vrijeme: ponedjeljak do petka, čćžšđ.</p></body></html>");

        var result = await ExtractAsync(
            new() { ["/"] = new TestResponse(200, "text/html", body) }, "http://site.test/");

        result.Pages[0].Text.ShouldContain("čćžšđ");
    }

    [Fact]
    public async Task A_page_built_only_by_javascript_is_rejected_with_advice()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractAsync(
            new() { ["/app"] = TestResponse.Html("<html><body><div id=root></div><script>render()</script></body></html>") },
            "http://site.test/app"));

        error.Message.ShouldContain("JavaScript");
    }

    // ---------- Redirects and errors ----------

    [Fact]
    public async Task Follows_redirects_to_allowed_addresses()
    {
        var result = await ExtractAsync(
            new()
            {
                ["/old"] = TestResponse.Redirect("http://other.test/new"),
                ["/new"] = TestResponse.Html("<p>The page moved here and is long enough to count as text.</p>"),
            },
            "http://site.test/old");

        result.Pages[0].Text.ShouldContain("The page moved here");
    }

    [Fact]
    public async Task Stops_after_too_many_redirects()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractAsync(
            new() { ["/loop"] = TestResponse.Redirect("/loop") }, "http://site.test/loop"));

        error.Message.ShouldContain("redirects too many times");
    }

    [Theory]
    [InlineData(404, "was not found")]
    [InlineData(403, "requires a login")]
    [InlineData(429, "limiting requests")]
    [InlineData(500, "returned an error (500)")]
    public async Task Explains_http_errors(int status, string expected)
    {
        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractAsync(
            new() { ["/page"] = new TestResponse(status, "text/html", "<p>error</p>"u8.ToArray()) },
            "http://site.test/page"));

        error.Message.ShouldContain(expected);
    }

    [Fact]
    public async Task Refuses_links_to_files_that_are_not_web_pages()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractAsync(
            new() { ["/manual.pdf"] = new TestResponse(200, "application/pdf", "%PDF-1.7"u8.ToArray()) },
            "http://site.test/manual.pdf"));

        error.Message.ShouldContain("not a web page");
    }

    [Fact]
    public async Task Refuses_pages_over_5_MB()
    {
        var huge = new byte[WebPageFetcher.MaxPageBytes + 1];
        Array.Fill(huge, (byte)'a');

        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractAsync(
            new() { ["/big"] = new TestResponse(200, "text/html", huge) }, "http://site.test/big"));

        error.Message.ShouldContain("too large");
    }

    [Fact]
    public async Task Reports_an_unknown_website()
    {
        var error = await Should.ThrowAsync<TextExtractionException>(
            () => ExtractAsync([], "http://does-not-exist.test/"));

        error.Message.ShouldContain("could not be found");
    }

    // ---------- SSRF at download time ----------

    [Theory]
    [InlineData("http://evil.test/")]
    [InlineData("http://metadata.test/latest/meta-data")]
    public async Task Never_connects_to_internal_addresses_behind_a_public_looking_name(string url)
    {
        await using var server = new TestWebServer(new() { ["/"] = TestResponse.Html("<p>internal secrets</p>") });

        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractFromAsync(server, url));

        error.Message.ShouldContain("private or local network");
        server.RequestedHosts.ShouldBeEmpty("the fetcher must refuse before connecting");
    }

    [Fact]
    public async Task With_the_real_rule_even_the_test_server_on_localhost_is_refused()
    {
        await using var server = new TestWebServer(new() { ["/"] = TestResponse.Html("<p>secret</p>") });
        using var fetcher = new WebPageFetcher(
            FakeResolve, UrlSafety.IsPublicAddress, (_, ct) => ConnectToAsync(server.Port, ct));

        var error = await Should.ThrowAsync<TextExtractionException>(
            () => fetcher.FetchAsync(new Uri("http://site.test/"), Ct));

        error.Message.ShouldContain("private or local network");
        server.RequestedHosts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_redirect_to_an_internal_name_is_refused()
    {
        await using var server = new TestWebServer(new()
        {
            ["/go"] = TestResponse.Redirect("http://evil.test/admin"),
            ["/admin"] = TestResponse.Html("<p>admin panel</p>"),
        });

        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractFromAsync(server, "http://site.test/go"));

        error.Message.ShouldContain("private or local network");
        server.RequestedHosts.ShouldBe(["site.test"], "only the first, allowed request may reach a server");
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://site.test:8080/")]
    public async Task A_redirect_to_an_internal_address_is_refused_before_connecting(string target)
    {
        await using var server = new TestWebServer(new() { ["/go"] = TestResponse.Redirect(target) });

        var error = await Should.ThrowAsync<TextExtractionException>(() => ExtractFromAsync(server, "http://site.test/go"));

        error.Message.ShouldContain("redirects to a blocked address");
        server.RequestedHosts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Of_several_addresses_only_the_allowed_ones_are_used()
    {
        await using var server = new TestWebServer(new() { ["/"] = TestResponse.Html("<p>Public content that is long enough.</p>") });
        var connectedTo = new List<IPAddress>();
        using var fetcher = new WebPageFetcher(
            FakeResolve,
            TestRule,
            (endPoint, ct) =>
            {
                connectedTo.Add(endPoint.Address);
                return ConnectToAsync(server.Port, ct);
            });

        await fetcher.FetchAsync(new Uri("http://mixed.test/"), Ct);

        connectedTo.ShouldBe([IPAddress.Loopback], "192.168.1.1 must never be tried");
    }

    // ---------- helpers ----------

    /// <summary>Test rule: like production, but the local test server (127.0.0.1) counts as public.</summary>
    private static bool TestRule(IPAddress address) =>
        IPAddress.IsLoopback(address) || UrlSafety.IsPublicAddress(address);

    private static Task<IPAddress[]> FakeResolve(string host, CancellationToken cancellationToken) =>
        FakeDns.TryGetValue(host, out var addresses)
            ? Task.FromResult(addresses)
            : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound));

    private static async ValueTask<Stream> ConnectToAsync(int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task<ExtractedDocument> ExtractAsync(Dictionary<string, TestResponse> routes, string url)
    {
        await using var server = new TestWebServer(routes);
        return await ExtractFromAsync(server, url);
    }

    private static async Task<ExtractedDocument> ExtractFromAsync(TestWebServer server, string url)
    {
        using var fetcher = new WebPageFetcher(FakeResolve, TestRule, (_, ct) => ConnectToAsync(server.Port, ct));
        var document = Document.FromUrl(Guid.NewGuid(), Guid.NewGuid(), new Uri(url));
        return await new UrlTextExtractor(fetcher).ExtractAsync(document, Ct);
    }
}
