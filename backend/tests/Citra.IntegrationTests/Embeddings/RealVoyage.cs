using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>
/// Opt-in access to the real Voyage API for tests: only when CITRA_REAL_API_TESTS=1 is set and a key is stored in
/// the API's user-secrets (citra-backend-dev). Normal runs never call Voyage (free accounts allow only 3 requests
/// per minute), and the key is never printed.
/// </summary>
internal static class RealVoyage
{
    public const string SkipReason =
        "Real Voyage API test skipped: set CITRA_REAL_API_TESTS=1 and store Voyage:ApiKey in user-secrets (citra-backend-dev) to run it.";

    /// <summary>The key, or null when real API tests are off or no key is stored.</summary>
    public static string? ApiKey()
    {
        if (Environment.GetEnvironmentVariable("CITRA_REAL_API_TESTS") != "1")
        {
            return null;
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", "citra-backend-dev", "secrets.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var key = JsonNode.Parse(File.ReadAllText(path))?["Voyage:ApiKey"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>The app's own Infrastructure services (settings from Citra.Api/appsettings.json), with the real key.</summary>
    public static ServiceProvider Services(string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ApiProjectDirectory(), "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
                ["Voyage:ApiKey"] = apiKey,
                ["Storage:LocalRootPath"] = Path.GetTempPath(),
            })
            .Build();
        var services = new ServiceCollection().AddLogging();
        Citra.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);
        return services.BuildServiceProvider();
    }

    private static string ApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Citra.Api")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "Citra.Api");
    }
}
