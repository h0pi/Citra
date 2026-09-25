using Citra.Api.ErrorHandling;
using Citra.Api.HealthChecks;
using Citra.Api.OpenApi;
using Citra.Application;
using Citra.Infrastructure;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

// Bootstrap logger: catches errors that happen before configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, config) => config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    // Add services to the container.

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddValidatedOptions<JwtOptions>(builder.Configuration, JwtOptions.SectionName);

    // Errors are returned as RFC 7807 ProblemDetails JSON, always with a traceId
    // so a user-reported error can be matched to the server log.
    builder.Services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // CORS for the Angular admin app. Origins come from config ("Cors:AllowedOrigins"):
    // localhost:4200 in Development, the real frontend domain in production.
    // (The public website widget gets its own per-bot policy later.)
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()                        // required by SignalR
        .WithExposedHeaders("Content-Disposition") // lets the browser read download file names
        .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
    });

    var app = builder.Build();

    // Development only: bring the local database schema up to date on startup.
    // (Only the API does this — the Worker never migrates, so the two can't race.)
    if (app.Environment.IsDevelopment())
    {
        await app.Services.ApplyDatabaseMigrationsAsync();
    }

    // One compact log line per HTTP request (method, path, status, duration).
    // Registered first (outermost) so it records the final status code set by the exception handler.
    app.UseSerilogRequestLogging();

    app.UseExceptionHandler();
    // Empty error responses (e.g. 404 for unknown routes) also get a ProblemDetails body.
    app.UseStatusCodePages();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        // Raw spec: /openapi/v1.json · Interactive API reference: /scalar
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("Citra API")
            .AddPreferredSecuritySchemes(BearerSecuritySchemeTransformer.SchemeName));
    }

    app.UseHttpsRedirection();

    app.UseCors(FrontendCorsPolicy);

    app.UseAuthorization();

    app.MapControllers();

    // /health       → runs all checks (database, …): is the app ready to serve traffic?
    // /health/live  → runs no checks: is the process alive? (used by Docker / uptime monitors)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    });

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Citra.Api terminated unexpectedly");
    Environment.ExitCode = 1; // non-zero so Docker / hosting knows the app failed
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    private const string FrontendCorsPolicy = "Frontend";
}
