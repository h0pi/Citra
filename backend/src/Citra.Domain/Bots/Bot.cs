using Citra.Domain.Common;

namespace Citra.Domain.Bots;

/// <summary>
/// An AI assistant with its own documents, instructions and website widget.
/// A tenant can have several bots (e.g. "Customer support" and "Internal HR").
/// </summary>
public sealed class Bot : Entity, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int SystemPromptMaxLength = 4000;
    public const int WelcomeMessageMaxLength = 500;
    public const int MaxStarterQuestions = 6;
    public const int MaxAllowedDomains = 20;

    public const string DefaultWelcomeMessage = "Hi! Ask me anything about our documentation.";
    public const string DefaultWidgetColor = "#6366F1";

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Unique within the tenant; used in the public widget URL.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Extra instructions for this bot (tone, focus). Appended to Citra's built-in RAG rules.</summary>
    public string? SystemPrompt { get; private set; }

    public string WelcomeMessage { get; private set; } = DefaultWelcomeMessage;

    /// <summary>Optional per-bot model override (e.g. "claude-haiku-4-5"); null = app default.</summary>
    public string? Model { get; private set; }

    /// <summary>0 = focused and consistent, 1 = more creative. Low values suit document Q&amp;A.</summary>
    public double Temperature { get; private set; } = 0.2;

    /// <summary>Whether the public website widget may use this bot.</summary>
    public bool IsPublic { get; private set; }

    /// <summary>Hex color for the widget bubble and header, e.g. "#6366F1".</summary>
    public string WidgetColor { get; private set; } = DefaultWidgetColor;

    /// <summary>Websites allowed to embed the widget (e.g. "acme.com"). Empty = none.</summary>
    public List<string> AllowedDomains { get; private set; } = [];

    /// <summary>Suggested first questions shown in chat and widget.</summary>
    public List<string> StarterQuestions { get; private set; } = [];

    /// <summary>Protects API costs: max widget messages per day (null = unlimited).</summary>
    public int? DailyMessageLimit { get; private set; } = 500;

    private Bot()
    {
        // For EF Core.
    }

    public static Bot Create(Guid tenantId, string name, string? description = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Bot
        {
            TenantId = tenantId,
            Name = name.Trim(),
            Slug = Common.Slug.From(name),
            Description = NullIfEmpty(description),
        };
    }

    public void UpdateDetails(string name, string? description, string? systemPrompt, string welcomeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(welcomeMessage);

        Name = name.Trim();
        Description = NullIfEmpty(description);
        SystemPrompt = NullIfEmpty(systemPrompt);
        WelcomeMessage = welcomeMessage.Trim();
    }

    /// <summary>Slug is set separately so the application layer can make it unique within the tenant first.</summary>
    public void ChangeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        Slug = Common.Slug.From(slug);
    }

    public void ConfigureModel(string? model, double temperature)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(temperature);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(temperature, 1.0);

        Model = NullIfEmpty(model);
        Temperature = temperature;
    }

    public void ConfigureWidget(
        bool isPublic,
        string widgetColor,
        IEnumerable<string> allowedDomains,
        IEnumerable<string> starterQuestions,
        int? dailyMessageLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetColor);
        if (dailyMessageLimit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyMessageLimit), "Limit cannot be negative.");
        }

        IsPublic = isPublic;
        WidgetColor = widgetColor.Trim();
        AllowedDomains = CleanList(allowedDomains, MaxAllowedDomains, toLower: true);
        StarterQuestions = CleanList(starterQuestions, MaxStarterQuestions, toLower: false);
        DailyMessageLimit = dailyMessageLimit;
    }

    private static List<string> CleanList(IEnumerable<string> values, int max, bool toLower) =>
        values
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Select(v => toLower ? v.ToLowerInvariant() : v)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
