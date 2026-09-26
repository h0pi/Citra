using Citra.Application.Common.Storage;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Storage;

internal sealed class ConfiguredUploadLimits(IOptions<StorageOptions> options) : IUploadLimits
{
    public long MaxUploadBytes { get; } = options.Value.MaxUploadSizeMb * 1024L * 1024L;
}
