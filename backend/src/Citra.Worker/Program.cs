using Citra.Application;
using Citra.Infrastructure;
using Citra.Worker.Processing;
using Serilog;

// Bootstrap logger: catches errors that happen before configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, config) => config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Document processing: polls the job table and runs the pipeline for every due job
    // (its settings, section "Processing", are registered and validated by AddInfrastructure).
    builder.Services.AddHostedService<DocumentProcessingService>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Citra.Worker terminated unexpectedly");
    Environment.ExitCode = 1; // non-zero so Docker / hosting knows the app failed
}
finally
{
    Log.CloseAndFlush();
}
