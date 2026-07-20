namespace SharpProof.Symbolic.Smt;

internal sealed class SmtAnalysisBudget(TimeSpan methodBudget) {
    private readonly long _resourceLimit = SmtResourceBudget.GetMethodRlimitBudget(methodBudget);
    private readonly double _wallClockSafetyLimitTicks = methodBudget.TotalSeconds *
                                     Stopwatch.Frequency *
                                     SmtResourceBudget.WallClockSafetyFactor;
    private long _consumedQueryTicks;
    private long _consumedResourceCount;

    public bool IsExceeded =>
        Interlocked.Read(ref _consumedResourceCount) > _resourceLimit ||
        Interlocked.Read(ref _consumedQueryTicks) > _wallClockSafetyLimitTicks;

    public void RecordConsumedResources(long consumedResourceCount) => Interlocked.Add(ref _consumedResourceCount, consumedResourceCount);

    public void RecordQueryDuration(long elapsedTicks) => Interlocked.Add(ref _consumedQueryTicks, elapsedTicks);
}
