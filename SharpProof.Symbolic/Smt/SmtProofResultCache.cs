namespace SharpProof.Symbolic.Smt;
internal sealed class SmtProofResultCache {
    private const int LocalEntryLimit = 2048;
    private const int SharedEntryLimit = 4096;
    private static readonly BoundedConcurrentCache<string, AnalysisProofResult> s_sharedResults =
        new(SharedEntryLimit, StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<AnalysisProofResult>> s_sharedFlights =
        new(StringComparer.Ordinal);
    private readonly BoundedConcurrentCache<string, AnalysisProofResult> _localResults =
        new(LocalEntryLimit, StringComparer.Ordinal);
    public int LocalEntryCount => _localResults.Count;
    public long LocalHitCount => _localResults.HitCount;
    public long LocalMissCount => _localResults.MissCount;
    public long LocalEvictionCount => _localResults.EvictionCount;
    public static int SharedEntryCount => s_sharedResults.Count;
    public static long SharedHitCount => s_sharedResults.HitCount;
    public static long SharedMissCount => s_sharedResults.MissCount;
    public static long SharedEvictionCount => s_sharedResults.EvictionCount;
    public bool TryGetLocal(string queryKey, out AnalysisProofResult result) =>
        _localResults.TryGetValue(queryKey, out result);
    public bool TryGetShared(SmtAnalysisOptions options, string queryKey, out AnalysisProofResult result) {
        if (options.UseSharedResultCache)
            return s_sharedResults.TryGetValue(CreateSharedKey(options, queryKey), out result);
        result = default!;
        return false;
    }
    public void AddLocalIfCacheable(string queryKey, AnalysisProofResult result) {
        if (!IsTransientFailure(result)) _localResults.TryAdd(queryKey, result);
    }
    public void AddLocal(string queryKey, AnalysisProofResult result) => _localResults.TryAdd(queryKey, result);
    public void AddSharedIfCacheable(SmtAnalysisOptions options, string queryKey, AnalysisProofResult result) {
        if (!options.UseSharedResultCache || !IsShareable(result)) return;
        s_sharedResults.TryAdd(CreateSharedKey(options, queryKey), result);
    }
    public SharedFlightLease AcquireSharedFlight(SmtAnalysisOptions options, string queryKey, Func<AnalysisProofResult> classify) {
        var sharedKey = CreateSharedKey(options, queryKey);
        var candidate = new Lazy<AnalysisProofResult>(classify, LazyThreadSafetyMode.ExecutionAndPublication);
        var flight = s_sharedFlights.GetOrAdd(sharedKey, candidate);
        return new SharedFlightLease(sharedKey, flight, ReferenceEquals(flight, candidate));
    }
    public void ReleaseSharedFlight(SharedFlightLease lease) {
        if (lease.OwnsFlight) s_sharedFlights.TryRemove(lease.Key, out _);
    }
    public static bool IsShareable(AnalysisProofResult result) =>
        result.Outcome is AnalysisProofOutcome.Proven or AnalysisProofOutcome.Disproven;
    public static bool IsTransientFailure(AnalysisProofResult result)
        => string.Equals(result.Reason, "smt_transient_failure", StringComparison.Ordinal) ||
               string.Equals(result.PathCheck.Witness?.Reason, "z3_transient_failure", StringComparison.Ordinal) ||
               string.Equals(result.HazardCheck.Witness?.Reason, "z3_transient_failure", StringComparison.Ordinal);
    private static string CreateSharedKey(SmtAnalysisOptions options, string queryKey) => options.Mode +
               "|timeout_ms=" +
               (long)options.QueryTimeout.TotalMilliseconds +
               "|max_path=" +
               options.MaxPathConditions +
               "|max_expr=" +
               options.MaxExpressionNodes +
               "|" +
               queryKey;
    internal sealed record SharedFlightLease(string Key, Lazy<AnalysisProofResult> Result, bool OwnsFlight);
}
