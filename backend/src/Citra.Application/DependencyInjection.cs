using Citra.Application.Common.Behaviors;
using Citra.Application.Common.Chunking;
using Citra.Application.Features.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Application layer: MediatR handlers and FluentValidation validators
    /// found in this assembly.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);
            // Order matters: behaviors run outer → inner. Logging wraps validation,
            // so failed validations are logged with their duration too.
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<AuthSessionIssuer>();

        // Stateless (each call keeps its own state); needs the ITokenCounter registered by Infrastructure.
        services.AddSingleton<TextChunker>();

        return services;
    }
}
