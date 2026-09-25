using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

public enum StorageProvider
{
    Local,
}

/// <summary>Where uploaded documents are stored. Section: "Storage".</summary>
public sealed class StorageOptions : IValidatableObject
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; init; } = StorageProvider.Local;

    /// <summary>Folder for uploaded files when <see cref="Provider"/> is Local. Relative paths resolve from the app's content root.</summary>
    public string LocalRootPath { get; init; } = string.Empty;

    [Range(1, 500)]
    public int MaxUploadSizeMb { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == StorageProvider.Local && string.IsNullOrWhiteSpace(LocalRootPath))
        {
            yield return new ValidationResult(
                "LocalRootPath is required when Provider is Local.",
                [nameof(LocalRootPath)]);
        }
    }
}
