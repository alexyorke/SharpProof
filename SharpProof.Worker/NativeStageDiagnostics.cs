namespace SharpProof.Worker;

// Opt-in diagnostics selected by the exact callable ID in SHARPPROOF_NATIVE_STAGE_DIAGNOSTICS.
// These observations never enter worker results, budgets, or claim publication.
internal sealed class NativeStageDiagnostics
{
    private const string SelectorVariable = "SHARPPROOF_NATIVE_STAGE_DIAGNOSTICS";
    private const string SelectedCallable = "M:Algorithms.Sorting.LSDRadixSorter.LSDRadixSort(System.Char[])";
    private static readonly string? Selector = Environment.GetEnvironmentVariable(SelectorVariable);
    private static readonly object WriteLock = new();

    private readonly string _location;
    private readonly string _callableId;
    private readonly List<string> _events = [];
    private readonly object _eventsLock = new();
    private const int MaximumRecords = 2048;
    private readonly Stack<long> _scopes = new();
    private readonly long _boundaryStarted;
    private readonly CancellationToken _methodCancellation;
    private readonly CancellationToken _projectCancellation;
    private readonly CancellationToken _callerCancellation;
    private long _nextScope;
    private int _dropped;

    private NativeStageDiagnostics(string location, string callableId, long boundaryStarted,
        CancellationToken methodCancellation, CancellationToken projectCancellation, CancellationToken callerCancellation)
    {
        _location = location;
        _callableId = callableId;
        _boundaryStarted = boundaryStarted;
        _methodCancellation = methodCancellation;
        _projectCancellation = projectCancellation;
        _callerCancellation = callerCancellation;
    }

    internal static NativeStageDiagnostics? For(WorkerCallableManifestEntry entry, long boundaryStarted,
        CancellationToken methodCancellation, CancellationToken projectCancellation, CancellationToken callerCancellation)
    {
        if (!string.Equals(Selector, SelectedCallable, StringComparison.Ordinal))
        { return null; }
        var location = Path.GetFileName(entry.Location.Path) + ":" +
            entry.Location.Line.ToString(CultureInfo.InvariantCulture);
        return string.Equals(Selector, entry.CallableId, StringComparison.Ordinal)
            ? new(location, entry.CallableId, boundaryStarted, methodCancellation, projectCancellation, callerCancellation)
            : null;
    }

    internal StageScope Measure(string stage, Func<long>? readConsumedResourceCount = null,
        bool flushOnDispose = false)
    {
        var started = Stopwatch.GetTimestamp();
        var resourceStart = TryRead(readConsumedResourceCount, out var value) ? value : (long?)null;
        long scope;
        long parent;
        lock (_eventsLock)
        {
            scope = ++_nextScope;
            parent = _scopes.TryPeek(out var current) ? current : 0;
            _scopes.Push(scope);
            Append(Prefix(stage) + " event=begin scope=" + scope.ToString(CultureInfo.InvariantCulture) +
                " parent=" + parent.ToString(CultureInfo.InvariantCulture) + Observation(started) +
                (resourceStart is { } start ? " resourcesStart=" + start.ToString(CultureInfo.InvariantCulture) : ""));
        }
        return new StageScope(this, stage, scope, parent, started, resourceStart,
            resourceStart.HasValue ? readConsumedResourceCount : null, flushOnDispose);
    }

    internal void Record(string stage, string result)
    {
        var line = Prefix(stage) + " event=record" + Observation(Stopwatch.GetTimestamp()) + " " + result;
        lock (_eventsLock)
        { Append(line); }
    }

    internal static string Describe(PassiveCallableCheckResult result)
    {
        var outcome = result.Outcome switch
        {
            ProvenOutcome => "Proven",
            RefutedOutcome => "Refuted",
            UnknownOutcome => "Unknown",
            null => "None",
            _ => "Other"
        };
        return "outcome=" + outcome +
            " reason=" + result.Reason +
            " queryCompleted=" + (result.QueryCompleted ? "true" : "false");
    }

    private void Write(string stage, long scope, long parent, long started, long? resourceStart,
        Func<long>? readConsumedResourceCount, string? result)
    {
        var resourceEnd = TryRead(readConsumedResourceCount, out var value) ? value : (long?)null;
        var ended = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds;
        var line = Prefix(stage) + " event=end scope=" + scope.ToString(CultureInfo.InvariantCulture) +
            " parent=" + parent.ToString(CultureInfo.InvariantCulture) + Observation(ended) +
            " elapsedMs=" + elapsed.ToString("F3", CultureInfo.InvariantCulture);
        if (resourceStart is { } start && resourceEnd is { } end)
        {
            line += " resourcesStart=" + start.ToString(CultureInfo.InvariantCulture) +
                " resourcesEnd=" + end.ToString(CultureInfo.InvariantCulture) +
                " resourcesDelta=" + (end - start).ToString(CultureInfo.InvariantCulture);
        }
        else if (resourceStart.HasValue || readConsumedResourceCount != null)
        {
            line += " resources=unavailable";
        }
        if (result != null)
        { line += " " + result; }
        lock (_eventsLock)
        {
            if (_scopes.TryPeek(out var current) && current == scope)
            { _scopes.Pop(); }
            Append(line);
        }
    }

    private string Observation(long timestamp) =>
        " methodElapsedMs=" + Stopwatch.GetElapsedTime(_boundaryStarted, timestamp).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
        " methodCanceled=" + (_methodCancellation.IsCancellationRequested ? "true" : "false") +
        " projectCanceled=" + (_projectCancellation.IsCancellationRequested ? "true" : "false") +
        " callerCanceled=" + (_callerCancellation.IsCancellationRequested ? "true" : "false");

    // Called only under _eventsLock; dropping diagnostic rows never drops proof work.
    private void Append(string line)
    {
        if (_events.Count < MaximumRecords)
        { _events.Add(line); }
        else
        { _dropped++; }
    }

    private string Prefix(string stage)
    {
        return "[SharpProof.NativeStage] location=" + _location +
            " callable=" + _callableId +
            " stage=" + stage;
    }

    private void Flush()
    {
        lock (_eventsLock)
        {
            lock (WriteLock)
            {
                foreach (var line in _events)
                {
                    try
                    { Console.Error.WriteLine(line); }
                    catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                    { }
                }
                try
                {
                    Console.Error.WriteLine(Prefix("flush") + " event=flush retained=" + _events.Count.ToString(CultureInfo.InvariantCulture) +
                        " dropped=" + _dropped.ToString(CultureInfo.InvariantCulture) + Observation(Stopwatch.GetTimestamp()));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                { }
                _events.Clear();
            }
        }
    }

    private static bool TryRead(Func<long>? reader, out long value)
    {
        value = 0;
        if (reader == null)
        { return false; }
        try
        {
            value = reader();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { return false; }
    }

    internal sealed class StageScope(NativeStageDiagnostics owner, string stage, long scope, long parent, long started,
        long? resourceStart, Func<long>? readConsumedResourceCount, bool flushOnDispose) : IDisposable
    {
        private int _disposed;
        private string? _result;

        internal void SetResult(string result)
        { _result = result; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Write(stage, scope, parent, started, resourceStart, readConsumedResourceCount, _result);
                if (flushOnDispose)
                { owner.Flush(); }
            }
        }
    }
}
