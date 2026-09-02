namespace SharpProof.Worker.Protocol;

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly string _limitMessage;
    private long _remaining;

    internal BoundedReadStream(
        Stream inner,
        long maximumBytes,
        string limitMessage)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maximumBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        _remaining = maximumBytes;
        _limitMessage = limitMessage ??
            throw new ArgumentNullException(nameof(limitMessage));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (count == 0)
        {
            return 0;
        }
        if (_remaining == 0)
        {
            return ProbeForOverflow();
        }

        var read = _inner.Read(
            buffer,
            offset,
            (int)Math.Min(count, _remaining));
        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (count == 0)
        {
            return Task.FromResult(0);
        }
        return _remaining == 0
            ? ProbeForOverflowAsync(cancellationToken)
            : ReadWithinLimitAsync(
                buffer,
                offset,
                (int)Math.Min(count, _remaining),
                cancellationToken);
    }

    public override int ReadByte()
    {
        if (_remaining == 0)
        {
            return ProbeForOverflow();
        }

        var value = _inner.ReadByte();
        if (value >= 0)
        {
            _remaining--;
        }
        return value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task<int> ReadWithinLimitAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken)
            .ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    private int ProbeForOverflow()
    {
        return CompleteOverflowProbe(_inner.ReadByte() >= 0);
    }

    private async Task<int> ProbeForOverflowAsync(
        CancellationToken cancellationToken)
    {
        var probe = new byte[1];
        if (await _inner.ReadAsync(
                probe,
                0,
                1,
                cancellationToken)
            .ConfigureAwait(false) != 0)
        {
            return CompleteOverflowProbe(hasMoreData: true);
        }
        return CompleteOverflowProbe(hasMoreData: false);
    }

    private int CompleteOverflowProbe(bool hasMoreData)
    {
        if (hasMoreData)
        {
            throw new InvalidDataException(_limitMessage);
        }
        return 0;
    }

    private static void ValidateBufferArguments(
        byte[] buffer,
        int offset,
        int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException(
                "The offset and count exceed the buffer length.");
        }
    }
}
