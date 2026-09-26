using Citra.Application.Common.Storage;
using Citra.Domain.Documents;
using FluentValidation;

namespace Citra.Application.Features.Documents.Upload;

public sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    /// <summary>All file problems are reported on "file", the name of the upload field.</summary>
    public const string FileField = "File";

    /// <summary>Also used by the API when the web server itself stops an oversized request.</summary>
    public static string TooLargeMessage(IUploadLimits limits) =>
        $"The file is larger than the {limits.MaxUploadBytes / (1024 * 1024)} MB limit.";

    public UploadDocumentCommandValidator(IUploadLimits limits)
    {
        RuleFor(x => x.BotId).NotEmpty();

        RuleFor(x => x.FileName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Choose a file to upload.")
            .MaximumLength(Document.FileNameMaxLength).WithMessage("The file name is too long.")
            .Must(name => SupportedFileTypes.FromFileName(name) is not null)
            .WithMessage($"This file type is not supported. Upload {SupportedFileTypes.AllowedList}.")
            .OverridePropertyName(FileField);

        // Size rules only once there is a file at all (no file = one message, not "empty" as well).
        RuleFor(x => x.Length)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(limits.MaxUploadBytes).WithMessage(TooLargeMessage(limits))
            .OverridePropertyName(FileField)
            .When(x => !string.IsNullOrWhiteSpace(x.FileName));

        RuleFor(x => x.Content).NotNull().OverridePropertyName(FileField);
    }
}
