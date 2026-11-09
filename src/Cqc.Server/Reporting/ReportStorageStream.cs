namespace Cqc.Server.Reporting;

/// <summary>Counts bytes before publication and enforces the same reservation during disk writes.</summary>
internal sealed class ReportStorageStream(Stream destination, long maximumBytes) : Stream
{
    private long position;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => position;
    public override long Position { get => position; set => throw new NotSupportedException(); }
    public override void Flush() => destination.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => destination.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count)
    {
        Reserve(count);
        destination.Write(buffer, offset, count);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reserve(buffer.Length);
        await destination.WriteAsync(buffer, cancellationToken);
    }
    private void Reserve(int count)
    {
        if (count > maximumBytes - position) throw new InvalidOperationException("Report exceeds temporary storage capacity; reduce analysis scope.");
        position += count;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
