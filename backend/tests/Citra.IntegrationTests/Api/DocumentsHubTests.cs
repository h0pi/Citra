using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Citra.Api.Hubs;
using Citra.Application.Features.Documents;
using Citra.Domain.Documents;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Api;

/// <summary>
/// The documents hub of the real API (in memory). Only signed-in users connect; each receives its own
/// company's document updates and no other company's; the JSON matches the API's (camelCase, enums as text); and the
/// browser way of connecting (WebSockets, token in the address) works — while a token in the address is refused
/// for normal API calls.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentsHubTests(DatabaseFixture database) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_connection_without_a_token_is_refused()
    {
        await using var connection = Connect(accessToken: null);

        var error = await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct));

        error.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_connection_with_a_forged_token_is_refused()
    {
        var user = await _api.SignUpAsync(Ct);
        var parts = user.AccessToken.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}"; // same claims, wrong signature

        await using var connection = Connect(forged);

        (await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_user_receives_their_companys_updates_in_the_apis_json()
    {
        var user = await _api.SignUpAsync(Ct);
        await using var connection = Connect(user.AccessToken);
        var received = Listen(connection);
        await connection.StartAsync(Ct);

        var document = ReadyDocument(user.TenantId);
        var json = await SendUntilReceivedAsync(user.TenantId, document, received);

        json.GetProperty("id").GetGuid().ShouldBe(document.Id);
        json.GetProperty("botId").GetGuid().ShouldBe(document.BotId, "the page picks the updates of the bot it shows");
        json.GetProperty("status").GetString().ShouldBe("Ready", "enums as text, like the API");
        json.GetProperty("sourceType").GetString().ShouldBe("Pdf");
        json.GetProperty("fileName").GetString().ShouldBe("Handbook.pdf");
        json.GetProperty("chunkCount").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task Another_company_receives_nothing()
    {
        var mine = await _api.SignUpAsync(Ct);
        var other = await _api.SignUpAsync(Ct);
        await using var myConnection = Connect(mine.AccessToken);
        await using var otherConnection = Connect(other.AccessToken);
        var myUpdates = Listen(myConnection);
        var otherUpdates = Listen(otherConnection);
        await myConnection.StartAsync(Ct);
        await otherConnection.StartAsync(Ct);

        await SendUntilReceivedAsync(mine.TenantId, ReadyDocument(mine.TenantId), myUpdates);
        await Task.Delay(500, Ct); // time for a wrongly addressed message to arrive

        otherUpdates.Task.IsCompleted.ShouldBeFalse("another company's documents are never sent to it");
    }

    [Fact]
    public async Task A_browser_connects_over_websockets_with_the_token_in_the_address()
    {
        var user = await _api.SignUpAsync(Ct);
        var server = _api.Server;
        var url = new Uri(server.BaseAddress, $"{DocumentsHub.Path}?access_token={Uri.EscapeDataString(user.AccessToken)}");

        // As a browser does: WebSockets straight away, no Authorization header (browsers cannot set one there).
        await using var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken);
            })
            .Build();
        var received = Listen(connection);
        await connection.StartAsync(Ct);

        var json = await SendUntilReceivedAsync(user.TenantId, ReadyDocument(user.TenantId), received);

        json.GetProperty("status").GetString().ShouldBe("Ready");
    }

    [Fact]
    public async Task A_token_in_the_address_is_refused_for_normal_api_calls()
    {
        var user = await _api.SignUpAsync(Ct);
        using var client = _api.CreateClient();

        var inAddress = await client.GetAsync($"/api/auth/me?access_token={Uri.EscapeDataString(user.AccessToken)}", Ct);
        inAddress.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "tokens in URLs leak (history, logs, proxies)");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "the same token in the header works");
    }

    // ---------- helpers ----------

    /// <summary>A hub connection through the in-memory server (long polling), with the token in the header.</summary>
    private HubConnection Connect(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, DocumentsHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();

    /// <summary>Completes with the raw JSON of the first DocumentChanged message.</summary>
    private static TaskCompletionSource<JsonElement> Listen(HubConnection connection)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(nameof(IDocumentsClient.DocumentChanged), json => received.TrySetResult(json));
        return received;
    }

    /// <summary>
    /// Sends to the company's group until the message arrives: the server adds a connection to its group just
    /// after the connection is established, so the very first send can come a moment too early.
    /// </summary>
    private async Task<JsonElement> SendUntilReceivedAsync(Guid tenantId, DocumentDto document, TaskCompletionSource<JsonElement> received)
    {
        var hub = _api.Services.GetRequiredService<IHubContext<DocumentsHub, IDocumentsClient>>();
        var waited = Stopwatch.StartNew();
        while (!received.Task.IsCompleted)
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the update never arrived");
            await hub.Clients.Group(DocumentsHub.TenantGroup(tenantId)).DocumentChanged(document);
            await Task.WhenAny(received.Task, Task.Delay(200, Ct));
        }

        return await received.Task;
    }

    private static DocumentDto ReadyDocument(Guid tenantId)
    {
        var document = Document.FromUpload(
            tenantId, Guid.NewGuid(), "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf, "p/original.pdf", new string('a', 64));
        document.StartExtracting();
        document.StartChunking();
        document.StartEmbedding();
        document.MarkReady(5, 12, DateTimeOffset.UtcNow);
        return DocumentDto.From(document);
    }
}
