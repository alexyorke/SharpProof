namespace SharpProof.Smt;

internal static class SmtNativeUtilities
{
    internal static BackendFailureReason ClassifyUnknown(string? reason)
    {
        if (reason?.IndexOf("incomplete", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BackendFailureReason.Incomplete;
        }

        if (reason?.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return BackendFailureReason.Timeout;
        }

        if (reason?.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("rlimit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            reason?.IndexOf("max. memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(reason, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            return BackendFailureReason.ResourceLimit;
        }

        return BackendFailureReason.InfrastructureFailure;
    }

    internal static long ComputeResourceDelta(uint before, uint after)
    {
        // StatisticsEntry exposes Z3's native rlimit counter as a uint. The
        // context counter can wrap while a backend remains alive, so subtract
        // in the counter's unsigned domain instead of allowing a negative
        // delta to undercharge the query.
        return after >= before
            ? after - before
            : (long)uint.MaxValue + 1 - before + after;
    }

    internal static uint? ReadResourceCount(Solver solver)
    {
        // Statistics is a caller-owned Z3 object holding a native reference, the
        // same as a model. A solver session outlives hundreds of
        // queries per lane, so leaving it to the finalizer accumulates.
        using var statistics = solver.Statistics;
        foreach (var entry in statistics.Entries)
        {
            if (!string.Equals(
                    entry.Key,
                    "rlimit count",
                    StringComparison.Ordinal) ||
                !entry.IsUInt)
            {
                continue;
            }

            return entry.UIntValue;
        }

        return null;
    }

    internal static void AddOwnedParameter(
        Params parameters,
        Symbol name,
        uint value)
    {
        using (name)
        {
            parameters.Add(name, value);
        }
    }

    internal static IrValue? CreateBooleanValue(IrFactory factory, Expr expression)
    {
        return expression is not BoolExpr ? null : expression.BoolValue switch
        {
            Z3_lbool.Z3_L_TRUE => factory.CreateBooleanValue(true),
            Z3_lbool.Z3_L_FALSE => factory.CreateBooleanValue(false),
            _ => null
        };
    }
}
