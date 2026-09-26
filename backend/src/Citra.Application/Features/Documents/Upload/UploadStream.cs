using System.Security.Cryptography;

namespace Citra.Application.Features.Documents.Upload;

/// <summary>
/// Wraps an upload while it is copied to storage: first replays the header bytes already read for the type
/// check, then the rest of the upload. On the way it computes the SHA-256 and stops with
/// <see cref="FileTooLargeException"/> as soon as the real size passes the limit (the declared size can lie).
/// </summary>
internal sealed class UploadStream(byte[] header, Stream rest, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private int _headerPosition;
    private long _total;

    /// <summary>Lower-case hex SHA-256 of everything read. Call after the stream was read to the end.</summary>
    public string Sha256Hex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        if (_headerPosition < header.Length)
        {
            read = Math.Min(buffer.Length, header.Length - _headerPosition);
            header.AsMemory(_headerPosition, read).CopyTo(buffer);
            _headerPosition += read;
        }
        else
        {
            read = await rest.ReadAsync(buffer, cancellationToken);
        }

        _total += read;
        if (_total > maxBytes)
        {
            throw new FileTooLargeException(maxBytes);
        }

        _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _total;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The upload turned out bigger than allowed while it was being read.</summary>
internal sealed class FileTooLargeException(long maxBytes)
    : Exception($"The file is larger than the {maxBytes / (1024 * 1024)} MB limit.");
