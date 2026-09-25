using Citra.Application;
using Citra.Infrastructure;
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
