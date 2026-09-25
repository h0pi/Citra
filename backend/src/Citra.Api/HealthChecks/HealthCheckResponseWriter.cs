using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Citra.Api.HealthChecks;

/// <summary>
/// Writes health check results as JSON, e.g.
/// { "status": "Healthy", "totalDurationMs": 12, "checks": [{ "name": "database", ... }] }.
/// Exception details are never included — only the check's description.
/// </summary>
internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds),
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
