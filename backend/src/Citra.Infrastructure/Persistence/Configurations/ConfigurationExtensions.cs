using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Longest enum value name we store; enums are saved as readable text ("Ready", "Failed").</summary>
    public const int EnumMaxLength = 32;

    /// <summary>Stores an enum as its name instead of a number, so the database stays readable and safe to reorder.</summary>
    public static PropertyBuilder<TEnum> IsEnumText<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(EnumMaxLength);

    /// <summary>Money columns: up to 999,999.999999 USD with 6 decimals (per-message costs are fractions of a cent).</summary>
    public static PropertyBuilder<decimal> IsMoney(this PropertyBuilder<decimal> property) =>
        property.HasPrecision(12, 6);
}
