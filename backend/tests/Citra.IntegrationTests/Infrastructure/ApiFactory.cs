using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Citra.IntegrationTests.Infrastructure;

/// <summary>
/// The real API (its own Program.cs, middleware, auth and hubs) hosted in memory, on the tests' database.
/// Settings that normally come from user-secrets are given here; a random signing key per run.
/// </summary>
public sealed class ApiFactory(DatabaseFixture database) : WebApplicationFactory<Program>
{
    private readonly string _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", _signingKey);
        builder.UseSetting("Storage:LocalRootPath", database.StorageRoot);
    }

    /// <summary>Signs up a new company with one owner, as the register page does; returns their access token.</summary>
    public async Task<SignedUpUser> SignUpAsync(CancellationToken cancellationToken)
    {
        var unique = Guid.NewGuid().ToString("N");
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = $"Company {unique[..8]}",
            displayName = "Owner",
            email = $"owner-{unique}@example.test",
            password = $"Pw-{unique}1",
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var token = body.GetProperty("accessToken").GetString()!;
        return new SignedUpUser(token, TenantIdOf(token));
    }

    /// <summary>The tenant_id claim of an access token (its middle part is base64url JSON).</summary>
    private static Guid TenantIdOf(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(claims.RootElement.GetProperty("tenant_id").GetString()!);
    }
}

public sealed record SignedUpUser(string AccessToken, Guid TenantId);
