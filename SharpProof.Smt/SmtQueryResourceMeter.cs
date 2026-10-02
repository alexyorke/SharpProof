namespace SharpProof.Smt;

internal sealed class SmtQueryResourceMeter(
    uint limit,
    CancellationToken cancellationToken)
{
    private readonly long _limit = limit;
    private long _consumed;

    internal long Consumed => _consumed;

    internal void PollCancellation()
    {
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void Consume()
    {
        Consume(1);
    }

    internal void Consume(long amount)
    {
        PollCancellation();
        if (amount < 0 || amount > _limit - _consumed)
        {
            throw new SmtResourceLimitException();
        }

        _consumed += amount;
    }

    internal void ConsumeNative(long consumed)
    {
        _consumed = checked(_consumed + consumed);
        // Native work has already completed, even if cancellation arrived
        // before we could publish its charge.
        PollCancellation();
        if (_consumed > _limit)
        {
            throw new SmtResourceLimitException();
        }
    }

    internal uint GetRemainingBudget()
    {
        PollCancellation();
        var remaining = _limit - _consumed;
        if (remaining <= 0)
        {
            throw new SmtResourceLimitException();
        }

        return checked((uint)remaining);
    }
}

internal static class SmtNativeCheck
{
    internal static Status Run(Solver solver, Expr[] assumptions, SmtQueryResourceMeter meter)
    {
        meter.PollCancellation();
        var before = SmtNativeUtilities.ReadResourceCount(solver);
        try
        {
            return solver.Check(assumptions);
        }
        finally
        {
            var after = SmtNativeUtilities.ReadResourceCount(solver);
            if (after.HasValue)
            {
                meter.ConsumeNative(SmtNativeUtilities.ComputeResourceDelta(before.GetValueOrDefault(), after.Value));
            }
            meter.PollCancellation();
        }
    }
}


internal sealed class SmtResourceLimitException : Exception;
internal sealed class UnsupportedIrEncodingException : Exception;
