using System.Diagnostics;

namespace SharpProof.Symbolic.Smt;

internal sealed class SmtAnalysisBudget
{
    private readonly long _resourceLimit;
    private readonly double _wallClockSafetyLimitTicks;
    private long _consumedQueryTicks;
    private long _consumedResourceCount;

    public SmtAnalysisBudget(TimeSpan methodBudget)
    {
        _resourceLimit = SmtResourceBudget.GetMethodRlimitBudget(methodBudget);
        _wallClockSafetyLimitTicks = methodBudget.TotalSeconds *
                                     Stopwatch.Frequency *
                                     SmtResourceBudget.WallClockSafetyFactor;
    }

    public bool IsExceeded =>
        Interlocked.Read(ref _consumedResourceCount) > _resourceLimit ||
        Interlocked.Read(ref _consumedQueryTicks) > _wallClockSafetyLimitTicks;

    public void RecordConsumedResources(long consumedResourceCount)
    {
        Interlocked.Add(ref _consumedResourceCount, consumedResourceCount);
    }

    public void RecordQueryDuration(long elapsedTicks)
    {
        Interlocked.Add(ref _consumedQueryTicks, elapsedTicks);
    }
}
