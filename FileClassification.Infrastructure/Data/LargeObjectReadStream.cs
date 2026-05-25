using Npgsql;

namespace FileClassification.Infrastructure.Data;

#pragma warning disable CS0618 // NpgsqlLargeObjectStream is marked obsolete but still ships and works.

internal sealed class LargeObjectReadStream(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    NpgsqlLargeObjectStream inner) : Stream
{
    private bool _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        inner.ReadAsync(buffer, offset, count, ct);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        inner.ReadAsync(buffer, ct);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed) { base.Dispose(disposing); return; }
        _disposed = true;
        if (disposing)
        {
            inner.Dispose();
            transaction.Dispose(); // read-only, no commit
            connection.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await inner.DisposeAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
