using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Citra.Api;
using Citra.Application.Features.Auth.Register;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Citra.IntegrationTests.Api;

/// <summary>
/// Sign-up, login, refresh and logout through the real API (validation, error handling, JWT), as the
/// login and register pages use them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthEndpointTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_creates_the_company_and_its_owner_and_logs_them_in()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var email = UniqueEmail();

        using var response = await RegisterAsync(client, email, "Pass-1234", company: "  Acme Outdoor  ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await BodyAsync(response);
        session.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        session.GetProperty("refreshToken").GetString().ShouldNotBeNullOrEmpty();
        var user = session.GetProperty("user");
        (user.GetProperty("email").GetString(), user.GetProperty("role").GetString(), user.GetProperty("tenantName").GetString())
            .ShouldBe((email, "Owner", "Acme Outdoor"));

        // The access token works right away.
        using var me = await MeAsync(client, session.GetProperty("accessToken").GetString()!);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(me)).GetProperty("id").GetGuid().ShouldBe(user.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task An_email_can_register_only_once_whatever_its_case()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, email, "Pass-1234")).EnsureSuccessStatusCode();

        using var again = await RegisterAsync(client, "  " + email.ToUpperInvariant(), "Pass-5678");

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Errors(await BodyAsync(again), "email").ShouldContain("An account with this email already exists.");
    }

    [Theory]
    [InlineData("", "Owner", "owner@example.test", "Pass-1234", "companyName")]
    [InlineData("Acme", "", "owner@example.test", "Pass-1234", "displayName")]
    [InlineData("Acme", "Owner", "not-an-email", "Pass-1234", "email")]
    [InlineData("Acme", "Owner", "owner@example.test", "", "password")]
    [InlineData("Acme", "Owner", "owner@example.test", "short1", "password")]   // under 8 characters
    [InlineData("Acme", "Owner", "owner@example.test", "no-digits-here", "password")]
    public async Task Register_rejects_invalid_input_and_says_which_field(
        string company, string name, string email, string password, string field)
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { companyName = company, displayName = name, email, password }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Errors(await BodyAsync(response), field).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Login_works_with_the_email_in_any_case()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, email, "Pass-1234")).EnsureSuccessStatusCode();

        using var response = await LoginAsync(client, $"  {email.ToUpperInvariant()} ", "Pass-1234");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await BodyAsync(response);
        session.GetProperty("user").GetProperty("email").GetString().ShouldBe(email);
        (await MeAsync(client, session.GetProperty("accessToken").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_answer()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, email, "Pass-1234")).EnsureSuccessStatusCode();

        using var wrongPassword = await LoginAsync(client, email, "Pass-9999");
        using var unknownEmail = await LoginAsync(client, UniqueEmail(), "Pass-1234");

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Detail(await BodyAsync(wrongPassword)).ShouldBe("Invalid email or password.");
        Detail(await BodyAsync(unknownEmail)).ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, email, "Pass-1234")).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(client, email, "Pass-9999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var locked = await LoginAsync(client, email, "Pass-1234");

        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Detail(await BodyAsync(locked)).ShouldBe("Too many failed attempts. Please try again in 15 minutes.");
    }

    [Fact]
    public async Task Refresh_gives_a_new_pair_and_the_old_refresh_token_stops_working()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var first = await BodyAsync(await RegisterAsync(client, UniqueEmail(), "Pass-1234"));
        var oldToken = first.GetProperty("refreshToken").GetString()!;

        using var refreshed = await RefreshAsync(client, oldToken);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await BodyAsync(refreshed);
        second.GetProperty("refreshToken").GetString().ShouldNotBe(oldToken);
        second.GetProperty("user").GetProperty("id").GetGuid().ShouldBe(first.GetProperty("user").GetProperty("id").GetGuid());
        (await MeAsync(client, second.GetProperty("accessToken").GetString()!)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_an_old_refresh_token_ends_the_whole_session()
    {
        // A reused token means it was copied: the thief and the real user are both logged out.
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var oldToken = (await BodyAsync(await RegisterAsync(client, UniqueEmail(), "Pass-1234"))).GetProperty("refreshToken").GetString()!;
        var newToken = (await BodyAsync(await RefreshAsync(client, oldToken))).GetProperty("refreshToken").GetString()!;

        using var reused = await RefreshAsync(client, oldToken);
        using var afterReuse = await RefreshAsync(client, newToken);

        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Detail(await BodyAsync(reused)).ShouldBe("Your session has expired. Please log in again.");
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("not-a-real-token", HttpStatusCode.Unauthorized)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task An_unknown_or_missing_refresh_token_is_refused(string token, HttpStatusCode status)
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();

        using var response = await RefreshAsync(client, token);

        response.StatusCode.ShouldBe(status);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_can_be_repeated()
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();
        var token = (await BodyAsync(await RegisterAsync(client, UniqueEmail(), "Pass-1234"))).GetProperty("refreshToken").GetString()!;

        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = token }, Ct);
        using var again = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = token }, Ct);
        using var refresh = await RefreshAsync(client, token);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_sign_up_closed_nobody_can_register_but_accounts_still_log_in()
    {
        await using var open = new ApiFactory(fixture);
        var email = UniqueEmail();
        using (var client = open.CreateClient())
        {
            (await RegisterAsync(client, email, "Pass-1234")).EnsureSuccessStatusCode();
        }

        await using var closed = new ApiFactory(fixture).WithWebHostBuilder(b => b.UseSetting("Registration:Enabled", "false"));
        using var visitor = closed.CreateClient();

        using var register = await RegisterAsync(visitor, UniqueEmail(), "Pass-1234");
        using var login = await LoginAsync(visitor, email, "Pass-1234");

        register.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Detail(await BodyAsync(register)).ShouldBe(RegisterCommandHandler.ClosedMessage);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task One_address_may_try_logging_in_only_so_often_a_minute()
    {
        // Guessing passwords across many accounts is stopped per address, not only per account (lockout).
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();

        for (var i = 0; i < RateLimits.AuthRequestsPerIpPerMinute; i++)
        {
            (await LoginAsync(client, UniqueEmail(), "Pass-1234")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var refused = await LoginAsync(client, UniqueEmail(), "Pass-1234");
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not.a.jwt")]
    public async Task Me_needs_a_valid_access_token(string? token)
    {
        await using var api = new ApiFactory(fixture);
        using var client = api.CreateClient();

        using var response = await MeAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string UniqueEmail() => $"auth-{Guid.NewGuid():N}@example.test";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password, string company = "Acme") =>
        client.PostAsJsonAsync("/api/auth/register", new { companyName = company, displayName = "Owner", email, password }, Ct);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token }, Ct);

    private static Task<HttpResponseMessage> MeAsync(HttpClient client, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string? Detail(JsonElement problem) => problem.GetProperty("detail").GetString();

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").TryGetProperty(field, out var messages)
            ? [.. messages.EnumerateArray().Select(m => m.GetString()!)]
            : [];
}
