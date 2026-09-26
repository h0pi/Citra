namespace Citra.Application.Common.Storage;

/// <summary>Upload rules that come from configuration (Storage:MaxUploadSizeMb).</summary>
public interface IUploadLimits
{
    long MaxUploadBytes { get; }
}
