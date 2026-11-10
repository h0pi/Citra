using System.ComponentModel.DataAnnotations;
using Citra.Application.Features.Auth.Demo;

namespace Citra.Infrastructure.Options;

/// <summary>The public demo ("Try demo" on the login page). Section: "Demo". No slug = no demo.</summary>
public sealed class DemoOptions : IDemoSettings
{
    public const string SectionName = "Demo";

    /// <summary>Slug of the demo company, e.g. "northwind-outdoor" (created by demo-data/seed.py).</summary>
    public string? TenantSlug { get; init; }

    /// <summary>When the Worker cleans up the demo every night (hour in UTC).</summary>
    [Range(0, 23)]
    public int ResetHourUtc { get; init; } = 3;
}
