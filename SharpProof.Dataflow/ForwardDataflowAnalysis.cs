namespace SharpProof.Dataflow;

public sealed class ForwardDataflowAnalysisOptions(
    int widenAfter = 2,
    int maxIterations = 10_000)
{
    public int WidenAfter
    {
        get;
    } = ArgumentNullGuard.RequireNonnegative(widenAfter, nameof(widenAfter));
    public int MaxIterations
    {
        get;
    } = ArgumentNullGuard.RequirePositive(maxIterations, nameof(maxIterations));
}

public sealed class DataflowAnalysisResult<T>
{
    internal DataflowAnalysisResult(
        ImmutableArray<T> inputStates,
        ImmutableArray<T> outputStates,
        int iterations)
    {
        InputStates = inputStates;
        OutputStates = outputStates;
        Iterations = iterations;
    }

    public ImmutableArray<T> InputStates
    {
        get;
    }
    public ImmutableArray<T> OutputStates
    {
        get;
    }
    public int Iterations
    {
        get;
    }

    public T GetInputState(int blockId)
    {
        return InputStates[blockId];
    }

    public T GetOutputState(int blockId)
    {
        return OutputStates[blockId];
    }
}

/// <summary>
/// Raised when the solver reaches its iteration limit without reaching a fixed
/// point. This is a resource bound rather than a defect, so callers that must
/// degrade gracefully can catch it specifically instead of every
/// <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class DataflowConvergenceException : InvalidOperationException
{
    public DataflowConvergenceException()
        : this("The dataflow analysis did not converge within its iteration limit.")
    {
    }

    public DataflowConvergenceException(string message)
        : base(message)
    {
    }

    public DataflowConvergenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Deterministic, round-based forward worklist solver.
/// </summary>
public static class ForwardDataflowAnalysis
{
    public static DataflowAnalysisResult<T> Analyze<T>(
        DataflowGraph<T> graph,
        IAbstractDomain<T> domain,
        T initialState,
        ForwardDataflowAnalysisOptions? options = null)
    {
        return AnalyzeCore(graph, domain, initialState,
            options ?? new ForwardDataflowAnalysisOptions(),
            produceResult: true)!;
    }

    internal static void AnalyzeWithoutResult<T>(
        DataflowGraph<T> graph,
        IAbstractDomain<T> domain,
        T initialState,
        ForwardDataflowAnalysisOptions options)
    {
        _ = AnalyzeCore(graph, domain, initialState, options,
            produceResult: false);
    }

    private static DataflowAnalysisResult<T>? AnalyzeCore<T>(
        DataflowGraph<T> graph,
        IAbstractDomain<T> domain,
        T initialState,
        ForwardDataflowAnalysisOptions options,
        bool produceResult)
    {
        ArgumentNullGuard.NotNull(graph, nameof(graph));
        ArgumentNullGuard.NotNull(domain, nameof(domain));
        ArgumentNullGuard.NotNull(options, nameof(options));

        var blockCount = graph.Blocks.Length;
        var bottom = domain.Bottom;
        var inputs = Enumerable.Repeat(bottom, blockCount).ToArray();
        var outputs = Enumerable.Repeat(bottom, blockCount).ToArray();
        var updateCounts = new int[blockCount];
        inputs[graph.EntryBlockId] = initialState;

        var pending = FindReachableBlocks(graph);
        var changedOutputs = new List<int>();
        var affected = new SortedSet<int>();
        var iterations = 0;
        while (pending.Count != 0)
        {
            if (iterations >= options.MaxIterations)
            {
                throw new DataflowConvergenceException();
            }

            iterations++;

            var batch = pending.ToImmutableArray();
            pending.Clear();

            changedOutputs.Clear();
            foreach (var blockId in batch)
            {
                var transferred = graph.GetBlock(blockId).Transfer(inputs[blockId]);
                if (!domain.LessThanOrEqual(outputs[blockId], transferred))
                {
                    throw new InvalidOperationException(
                        $"Block {blockId} transfer must be monotone as its input grows.");
                }

                if (domain.LessThanOrEqual(transferred, outputs[blockId]))
                {
                    continue;
                }

                var monotoneOutput = domain is CanonicalAbstractDomain<T> canonicalDomain &&
                    canonicalDomain.IsCanonicalTransfer(transferred)
                        ? transferred
                        : domain.Join(outputs[blockId], transferred);
                if (!domain.AreEquivalent(outputs[blockId], monotoneOutput))
                {
                    outputs[blockId] = monotoneOutput;
                    changedOutputs.Add(blockId);
                }
            }
            if (changedOutputs.Count == 0)
            {
                continue;
            }

            affected.Clear();
            foreach (var blockId in changedOutputs)
            {
                foreach (var successor in graph.GetSuccessors(blockId))
                {
                    affected.Add(successor);
                }
            }

            foreach (var blockId in affected)
            {
                var incoming = blockId == graph.EntryBlockId ? initialState : bottom;
                foreach (var predecessor in graph.GetPredecessors(blockId))
                {
                    incoming = domain.Join(incoming, outputs[predecessor]);
                }

                var candidate = domain.Join(inputs[blockId], incoming);
                if (domain.AreEquivalent(inputs[blockId], candidate))
                {
                    continue;
                }

                var shouldWiden = graph.IsCyclicBlock(blockId) &&
                    updateCounts[blockId] >= options.WidenAfter;
                var updated = shouldWiden
                    ? domain.Widen(inputs[blockId], candidate)
                    : candidate;
                if (!domain.LessThanOrEqual(inputs[blockId], updated) ||
                    !domain.LessThanOrEqual(candidate, updated))
                {
                    throw new InvalidOperationException("Domain widening must be an upper bound.");
                }

                updateCounts[blockId]++;
                if (!shouldWiden || !domain.AreEquivalent(inputs[blockId], updated))
                {
                    inputs[blockId] = updated;
                    pending.Add(blockId);
                }
            }
        }

        return produceResult
            ? new DataflowAnalysisResult<T>([.. inputs], [.. outputs], iterations)
            : null;
    }

    private static SortedSet<int> FindReachableBlocks<T>(
        DataflowGraph<T> graph)
    {
        var reachable = new SortedSet<int> { graph.EntryBlockId };
        var pending = new Stack<int>();
        pending.Push(graph.EntryBlockId);
        while (pending.Count != 0)
        {
            var blockId = pending.Pop();
            foreach (var successor in graph.GetSuccessors(blockId))
            {
                if (reachable.Add(successor))
                {
                    pending.Push(successor);
                }
            }
        }

        return reachable;
    }
}
