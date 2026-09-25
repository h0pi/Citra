using System.Globalization;
using System.Text;

namespace Citra.Domain.Common;

/// <summary>Turns names into URL-friendly slugs, e.g. "Acme Support Bot!" → "acme-support-bot".</summary>
public static class Slug
{
    public const int MaxLength = 80;

    public static string From(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Split accented letters into base letter + accent mark, then drop the marks (č → c, é → e).
        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length > 0 ? slug : "item";
    }
}
