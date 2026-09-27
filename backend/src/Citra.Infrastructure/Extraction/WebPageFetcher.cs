using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Web;

namespace Citra.Infrastructure.Extraction;

/// <summary>A downloaded web page: its bytes, media type (e.g. "text/html"), charset and final address.</summary>
internal sealed record FetchedPage(byte[] Content, string MediaType, string? Charset, Uri FinalUrl);

/// <summary>
/// Downloads public web pages safely (SSRF protection, part 2 of 2; part 1 is UrlSafety.Check when the link is
/// added). Every connection, including after redirects, resolves the host itself and connects only to public
/// addresses, so a harmless-looking name that points at 127.0.0.1, 10.x or the cloud metadata service
/// (169.254.169.254) is refused. Resolving and connecting in one step also defeats DNS rebinding, where a name
/// resolves to a public address when checked and to an internal one when used.
/// </summary>
internal sealed class WebPageFetcher : IDisposable
{
    internal const int MaxRedirects = 5;
    internal const long MaxPageBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly string[] AcceptedMediaTypes = ["text/html", "application/xhtml+xml", "text/plain"];

    private readonly HttpClient _client;

    public WebPageFetcher()
        : this(Dns.GetHostAddressesAsync, UrlSafety.IsPublicAddress, ConnectAsync)
    {
    }

    /// <summary>For tests: fake DNS, a relaxed address rule for a local test server, and a custom connect.</summary>
    internal WebPageFetcher(
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isAllowed,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // each redirect target is checked below before following it
            UseProxy = false,          // a proxy would connect for us and bypass the address check
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = IPAddress.TryParse(host, out var literal)
                    ? [literal]
                    : await resolve(host, cancellationToken);

                var allowed = addresses.Where(isAllowed).ToList();
                if (allowed.Count == 0)
                {
                    throw new BlockedAddressException(host);
                }

                Exception? lastError = null;
                foreach (var address in allowed)
                {
                    try
                    {
                        return await connect(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    }
                    catch (SocketException ex)
                    {
                        lastError = ex;
                    }
                }

                throw lastError!;
            },
        };

        _client = new HttpClient(handler) { Timeout = Timeout };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CitraBot/1.0 (document import; +https://github.com/h0pi/Citra)");
        _client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.1");
    }

    public async Task<FetchedPage> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var redirect = 0; ; redirect++)
        {
            if (UrlSafety.Check(current) is { } problem)
            {
                throw new TextExtractionException(redirect == 0 ? problem : $"The page redirects to a blocked address. {problem}");
            }

            using var response = await SendAsync(current, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect >= MaxRedirects || response.Headers.Location is null)
                {
                    throw new TextExtractionException("The page redirects too many times.");
                }

                current = new Uri(current, response.Headers.Location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TextExtractionException(StatusMessage(response.StatusCode));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "text/html";
            if (!AcceptedMediaTypes.Contains(mediaType))
            {
                throw new TextExtractionException(
                    $"This link is a file ({mediaType}), not a web page. Download it and upload the file instead.");
            }

            if (response.Content.Headers.ContentLength > MaxPageBytes)
            {
                throw new TextExtractionException("This page is too large to import (over 5 MB).");
            }

            var content = await ReadLimitedAsync(response.Content, cancellationToken);
            return new FetchedPage(content, mediaType, response.Content.Headers.ContentType?.CharSet, current);
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            throw new TextExtractionException("This address points to a private or local network, which is not allowed.", ex);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.NameResolutionError
            || ex.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
        {
            throw new TextExtractionException("This website could not be found. Check the address.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TextExtractionException("This website could not be reached. Try again later.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TextExtractionException("The website did not respond in time.", ex);
        }
    }

    /// <summary>Reads at most <see cref="MaxPageBytes"/> (after decompression, so a "zip bomb" cannot blow up).</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxPageBytes)
            {
                throw new TextExtractionException("This page is too large to import (over 5 MB).");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string StatusMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Gone => "The page was not found (it may have been moved or deleted).",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "The page requires a login or blocks automated access, so it cannot be imported.",
        HttpStatusCode.TooManyRequests => "The website is limiting requests. Try again later.",
        _ => $"The website returned an error ({(int)status}). Try again later.",
    };

    private static async ValueTask<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Raised inside the connection step; unwrapped into a user message by <see cref="SendAsync"/>.</summary>
    private sealed class BlockedAddressException(string host)
        : Exception($"'{host}' resolves only to private or local addresses.");
}
