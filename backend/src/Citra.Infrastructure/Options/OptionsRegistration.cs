using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.Infrastructure.Options;

public static class OptionsRegistration
{
    /// <summary>
    /// Binds a config section to <typeparamref name="TOptions"/> and validates its data annotations
    /// when the app starts, so bad or missing settings fail fast with a clear message
    /// instead of breaking later at runtime.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
