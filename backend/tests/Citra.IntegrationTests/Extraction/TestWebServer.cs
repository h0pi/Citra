using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Citra.IntegrationTests.Extraction;

/// <summary>A canned HTTP response.</summary>
internal sealed record TestResponse(int Status, string ContentType, byte[] Body, string? Location = null)
{
    public static TestResponse Html(string html) => new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public static TestResponse Redirect(string location) => new(302, "text/plain", [], location);
}

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 for fetcher tests: it answers by path from a fixed table and
/// records every request's Host header. Deliberately tiny: one request per connection, then close.
/// </summary>
internal sealed class TestWebServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Dictionary<string, TestResponse> _routes;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public TestWebServer(Dictionary<string, TestResponse> routes)
    {
        _routes = routes;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> RequestedHosts { get; } = [];

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var request = await ReadHeadersAsync(stream);
            var requestLine = request.Split("\r\n")[0].Split(' ');
            var path = requestLine.Length > 1 ? requestLine[1] : "/";
            var host = request.Split("\r\n")
                .FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))?[5..].Trim() ?? "";
            lock (RequestedHosts)
            {
                RequestedHosts.Add(host);
            }

            var response = _routes.GetValueOrDefault(path) ?? new TestResponse(404, "text/plain", "missing"u8.ToArray());
            var head = new StringBuilder()
                .Append($"HTTP/1.1 {response.Status} X\r\n")
                .Append($"Content-Type: {response.ContentType}\r\n")
                .Append($"Content-Length: {response.Body.Length}\r\n")
                .Append(response.Location is null ? "" : $"Location: {response.Location}\r\n")
                .Append("Connection: close\r\n\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
            await stream.WriteAsync(response.Body);
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one) == 1)
        {
            buffer.Add(one[0]);
            if (buffer.Count >= 4 && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }
}
