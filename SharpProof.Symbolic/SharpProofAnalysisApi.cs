using SharpProof.Attributes;

namespace SharpProof.Symbolic;

public enum SharpProofQueryStatus {
    Succeeded,
    Unknown,
    Failed,
    Canceled
}

[Flags]
public enum SharpProofAnalysisFacet {
    None = 0,
    Effects = 1,
    ProofFacts = 2,
    RuntimeHazards = 4,
    Complexity = 8,
    All = Effects | ProofFacts | RuntimeHazards | Complexity
}

public enum SharpProofTargetKind {
    Point,
    Position,
    Line,
    Span,
    AllLines
}

public sealed record SharpProofTarget(
    SharpProofTargetKind Kind,
    int? Line = null,
    int? Column = null,
    int? Position = null,
    int? SpanStart = null,
    int? SpanEnd = null);

public sealed record SharpProofAnalysisOptions(
    SharpProofAnalysisBudget? AnalysisBudget = null);

public sealed record SharpProofAnalysisBudget(
    int MaxMergedIfElseFacts = 16,
    int MaxMergedSwitchFacts = 32,
    int MaxMergedTryFacts = 16,
    int MaxTryCompletionBranches = 8,
    int MaxFiniteForeachElementFacts = 8,
    int MaxScopedBlockCompletionStatements = 32,
    int MaxStructuralNullStateDepth = 4,
    int MaxMergedPathConditions = 32,
    int MaxMergeableFactsPerTargetPerState = 4,
    int MaxFactChoiceCombinationsPerTarget = 64,
    int MaxGuardFactsPerTargetPerState = 6) {
    public static SharpProofAnalysisBudget Default { get; } = new();

    private static readonly (string Name, Func<SharpProofAnalysisBudget, int> Read)[] NamedLimits =
    [
        ("merged-if-else-facts", static value => value.MaxMergedIfElseFacts),
        ("merged-switch-facts", static value => value.MaxMergedSwitchFacts),
        ("merged-try-facts", static value => value.MaxMergedTryFacts),
        ("try-completion-branches", static value => value.MaxTryCompletionBranches),
        ("finite-foreach-element-facts", static value => value.MaxFiniteForeachElementFacts),
        ("scoped-block-completion-statements", static value => value.MaxScopedBlockCompletionStatements),
        ("structural-null-state-depth", static value => value.MaxStructuralNullStateDepth),
        ("merged-path-conditions", static value => value.MaxMergedPathConditions),
        ("mergeable-facts-per-target-per-state", static value => value.MaxMergeableFactsPerTargetPerState),
        ("fact-choice-combinations-per-target", static value => value.MaxFactChoiceCombinationsPerTarget),
        ("guard-facts-per-target-per-state", static value => value.MaxGuardFactsPerTargetPerState)
    ];

    internal static SharpProofAnalysisBudget FromNamedValues(
        SharpProofAnalysisBudget defaults,
        Func<string, int, int> getValue) {
        var values = NamedLimits.Select(limit => getValue(limit.Name, limit.Read(defaults))).ToArray();
        return new SharpProofAnalysisBudget(
            values[0], values[1], values[2], values[3], values[4], values[5],
            values[6], values[7], values[8], values[9], values[10]);
    }

    internal SharpProofAnalysisBudget Validate() {
        var invalid = NamedLimits.FirstOrDefault(limit => limit.Read(this) <= 0);
        if (invalid.Name != null)
            throw new ArgumentOutOfRangeException(invalid.Name, "Analysis limits must be positive.");
        return this;
    }
}

public sealed record SharpProofAnalysisRequest(
    SharpProofTarget Target,
    SharpProofAnalysisFacet Facets = SharpProofAnalysisFacet.All,
    string? Condition = null);

public sealed record SharpProofUnknownReason(
    string Code,
    string Category,
    string Message,
    bool IsRetryable,
    bool IsConfigurationRelated);

public sealed record SharpProofTruncationReason(
    string Code,
    int Limit,
    int Observed,
    string Provenance,
    int? SourceSpanStart);

public sealed record SharpProofProofFact(
    string Condition,
    string Status,
    string Reason,
    string SymbolicCondition,
    string? Counterexample);

public sealed record SharpProofHazard(
    string Kind,
    string Status,
    string Reason,
    string ExceptionType,
    string Operation,
    int? SpanStart,
    int? SpanEnd,
    string? Counterexample);

public enum SharpProofErrorCategory {
    Usage,
    Input,
    Unsupported,
    Parse,
    Project,
    Solver,
    Timeout,
    Cancellation,
    Internal
}

public sealed record SharpProofError(
    string Code,
    SharpProofErrorCategory Category,
    string Message,
    int RecommendedExitCode,
    bool IsRetryable,
    ImmutableDictionary<string, string> Details);

public sealed record SharpProofAnalysisResult(
    SharpProofTarget Target,
    SharpProofQueryStatus Status,
    MethodEffects? MethodEffects,
    ImmutableArray<SharpProofProofFact> ProofFacts,
    ImmutableArray<SharpProofHazard> Hazards,
    string? Complexity,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons,
    ImmutableArray<SharpProofTruncationReason> Truncations,
    SharpProofError? Error);

public sealed class SharpProofAnalysisSession : IDisposable {
    private readonly ConcurrentDictionary<SharpProofAnalysisRequest, Lazy<SharpProofAnalysisResult>> _results = new();
    private readonly SymbolicQueryExecutor _executor = new();
    private readonly SmtAnalysisService _ownedSmtAnalysis;
    private readonly SymbolicSourceInput _source;
    private readonly SymbolicQueryOptions _options;
    private bool _disposed;

    private SharpProofAnalysisSession(
        SymbolicSourceInput source,
        SymbolicQueryOptions options,
        SmtAnalysisService ownedSmtAnalysis) {
        _source = source;
        _options = options;
        _ownedSmtAnalysis = ownedSmtAnalysis;
    }

    public static SharpProofAnalysisSession FromText(
        string sourceText,
        string? filePath = null,
        SharpProofAnalysisOptions? options = null) {
        options ??= new SharpProofAnalysisOptions();
        var source = CompileSource(sourceText, filePath ?? "SharpProof.Symbolic.Query.cs");
        var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return new SharpProofAnalysisSession(
            source,
            CreateQueryOptions(options, smt),
            smt);
    }

    public static SharpProofAnalysisSession FromFile(
        string filePath,
        SharpProofAnalysisOptions? options = null) {
        options ??= new SharpProofAnalysisOptions();
        var fullPath = Path.GetFullPath(filePath);
        var source = CompileSource(File.ReadAllText(fullPath), fullPath);
        var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return new SharpProofAnalysisSession(
            source,
            CreateQueryOptions(options, smt),
            smt);
    }

    public SharpProofAnalysisResult Analyze(
        SharpProofAnalysisRequest request,
        CancellationToken cancellationToken = default) {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (_disposed) throw new ObjectDisposedException(nameof(SharpProofAnalysisSession));
        if (cancellationToken.CanBeCanceled) return Execute(request, cancellationToken);

        var lazy = _results.GetOrAdd(request, value => new Lazy<SharpProofAnalysisResult>(
            () => Execute(value, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try {
            return lazy.Value;
        }
        catch {
            if (_results.TryGetValue(request, out var current) && ReferenceEquals(current, lazy))
                _results.TryRemove(request, out _);
            throw;
        }
    }

    public void Dispose() {
        _disposed = true;
        _results.Clear();
        _ownedSmtAnalysis.Dispose();
    }

    private SharpProofAnalysisResult Execute(
        SharpProofAnalysisRequest request,
        CancellationToken cancellationToken) {
        try {
            MethodEffects? effects = null;
            var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
            var proofFacts = ImmutableArray.CreateBuilder<SharpProofProofFact>();
            var hazards = ImmutableArray.CreateBuilder<SharpProofHazard>();
            var truncations = ImmutableArray.CreateBuilder<SharpProofTruncationReason>();
            string? complexity = null;
            var context = new SymbolicQueryContext(_source, request.Target, _options);

            if ((request.Facets & SharpProofAnalysisFacet.Effects) != 0) {
                effects = AnalyzeMethodEffects(context, cancellationToken);
                unknowns.AddRange(effects.UnknownReasons);
            }

            if ((request.Facets & SharpProofAnalysisFacet.ProofFacts) != 0)
                AnalyzeProofFacts(request, context, proofFacts, unknowns, truncations, cancellationToken);

            if ((request.Facets & SharpProofAnalysisFacet.RuntimeHazards) != 0)
                AnalyzeHazards(context, hazards, unknowns, truncations, cancellationToken);

            if ((request.Facets & SharpProofAnalysisFacet.Complexity) != 0)
                complexity = AnalyzeComplexity(context, unknowns, cancellationToken);

            return new SharpProofAnalysisResult(
                request.Target,
                unknowns.Count == 0 && truncations.Count == 0
                    ? SharpProofQueryStatus.Succeeded
                    : SharpProofQueryStatus.Unknown,
                effects,
                proofFacts.ToImmutable(),
                hazards.ToImmutable(),
                complexity,
                unknowns.Distinct().ToImmutableArray(),
                truncations.ToImmutable(),
                null);
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception)) {
            var error = SymbolicErrorClassifier.FromException(exception);
            return new SharpProofAnalysisResult(
                request.Target,
                error.Category == SharpProofErrorCategory.Cancellation
                    ? SharpProofQueryStatus.Canceled
                    : SharpProofQueryStatus.Failed,
                null,
                ImmutableArray<SharpProofProofFact>.Empty,
                ImmutableArray<SharpProofHazard>.Empty,
                null,
                ImmutableArray<SharpProofUnknownReason>.Empty,
                ImmutableArray<SharpProofTruncationReason>.Empty,
                error);
        }
    }

    private MethodEffects AnalyzeMethodEffects(
        SymbolicQueryContext context,
        CancellationToken cancellationToken) {
        if (context.Target.Kind is SharpProofTargetKind.Span or SharpProofTargetKind.AllLines)
            return AnalyzeMethodEffectRange(context, cancellationToken);

        return SymbolicMethodLikeQueryDispatcher.Execute(
            context,
            "Method-effect analysis supports point, position, line, span, or all-lines targets.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true),
            (resolved, compilation, token) => {
                if (resolved.MethodSymbol == null)
                    throw new ArgumentException("Could not resolve the target method.");
                return new MethodEffectAnalysisSession(compilation, token, smtAnalysis: _ownedSmtAnalysis).Analyze(
                    resolved.MethodSymbol,
                    resolved.Declaration,
                    resolved.SemanticModel);
            },
            cancellationToken);
    }

    private MethodEffects AnalyzeMethodEffectRange(
        SymbolicQueryContext context,
        CancellationToken cancellationToken) => AnalyzeSyntaxTree(
            context.Source.SyntaxTree,
            context.Source.Compilation,
            context.Target,
            cancellationToken);

    private MethodEffects AnalyzeSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SharpProofTarget target,
        CancellationToken cancellationToken) {
        var root = syntaxTree.GetRoot(cancellationToken);
        var declarations = root.DescendantNodesAndSelf()
            .Where(static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true));
        if (target.Kind == SharpProofTargetKind.Span) {
            var start = target.SpanStart ?? throw new ArgumentException("Span start is required.");
            var end = target.SpanEnd ?? throw new ArgumentException("Span end is required.");
            if (start < 0 || end < start || end > root.FullSpan.End)
                throw new ArgumentOutOfRangeException(nameof(target), "The target span must be within the source.");
            var span = TextSpan.FromBounds(start, end);
            declarations = declarations.Where(declaration => span.IsEmpty
                ? declaration.FullSpan.Contains(start)
                : declaration.FullSpan.OverlapsWith(span));
        }

        var model = compilation.GetSemanticModel(syntaxTree);
        var session = new MethodEffectAnalysisSession(compilation, cancellationToken, smtAnalysis: _ownedSmtAnalysis);
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var effects = ImmutableArray.CreateBuilder<MethodEffects>();
        foreach (var declaration in declarations) {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ResolvedMethodLikeTarget.Create(declaration, model, cancellationToken);
            if (resolved.BodyNode == null || resolved.MethodSymbol == null ||
                !seen.Add(resolved.MethodSymbol.OriginalDefinition))
                continue;
            effects.Add(session.Analyze(resolved.MethodSymbol, resolved.Declaration, model));
        }

        var combinedEffects = SharpProofEffect.None;
        var capabilities = SharpProofCapability.None;
        var exceptions = ImmutableArray.CreateBuilder<MethodExceptionFact>();
        var sites = ImmutableArray.CreateBuilder<MethodEffectSite>();
        var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        foreach (var method in effects) {
            combinedEffects |= method.Effects;
            capabilities |= method.Capabilities;
            exceptions.AddRange(method.ExceptionFacts);
            sites.AddRange(method.Sites);
            unknowns.AddRange(method.UnknownReasons);
        }
        return new MethodEffects(
            combinedEffects,
            capabilities,
            exceptions.Distinct().ToImmutableArray(),
            sites.Distinct().ToImmutableArray(),
            unknowns.Distinct().ToImmutableArray());
    }

    private void AnalyzeProofFacts(
        SharpProofAnalysisRequest request,
        SymbolicQueryContext context,
        ImmutableArray<SharpProofProofFact>.Builder facts,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        ImmutableArray<SharpProofTruncationReason>.Builder truncations,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(request.Condition)) {
            var proof = _executor.Prove(context, request.Condition!, cancellationToken);
            facts.Add(Project(proof));
            if (proof.TruthValue == SymbolicTruthValue.Unknown)
                unknowns.Add(Convert(SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    proof.Reason)));
            AddTruncations(truncations, proof.AnalysisTruncation);
            return;
        }

        var result = _executor.Query(context, cancellationToken);
        var hasUnknown = result.ProgramPoints.Any(
            static point => point.Reachability == SymbolicReachability.Unknown);
        facts.Add(new SharpProofProofFact(
            "invariant",
            hasUnknown ? "Unknown" : "Proven",
            "merged_symbolic_invariant",
            result.MergedInvariantText,
            null));
        foreach (var point in result.ProgramPoints)
            if (point.Reachability == SymbolicReachability.Unknown)
                unknowns.Add(Convert(SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown,
                    point.ReachabilityReason)));
        AddTruncations(truncations, result.AnalysisTruncation);
    }

    private void AnalyzeHazards(
        SymbolicQueryContext context,
        ImmutableArray<SharpProofHazard>.Builder hazards,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        ImmutableArray<SharpProofTruncationReason>.Builder truncations,
        CancellationToken cancellationToken) {
        var result = _executor.QueryRuntimeHazards(
            context,
            new SymbolicRuntimeHazardQueryOptions(true),
            cancellationToken);
        hazards.AddRange(result.Hazards.Select(static hazard => new SharpProofHazard(
            hazard.Kind.ToString(),
            hazard.Status.ToString(),
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.OperationText,
            hazard.SpanStart,
            hazard.SpanEnd,
            FormatCounterexample(hazard.TriggerWitness))));
        unknowns.AddRange(result.Hazards
            .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                SymbolicRuntimeHazardStatus.Unsupported)
            .Select(static hazard => Convert(hazard.UnknownReasonInfo)));
        AddTruncations(truncations, result.AnalysisTruncation);
    }

    private string AnalyzeComplexity(
        SymbolicQueryContext context,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        CancellationToken cancellationToken) {
        var result = _executor.QueryComplexity(context, cancellationToken);
        unknowns.AddRange(result.UnknownReasonDetails.Select(static reason => Convert(reason)));
        return result.Complexity.Text;
    }

    private static SharpProofUnknownReason Convert(SymbolicUnknownReasonInfo reason) => new(
        reason.Code,
        reason.Category.ToString(),
        reason.RawReason,
        reason.IsRetryable,
        reason.IsConfigurationRelated);

    private static SharpProofProofFact Project(SymbolicConditionProofResult proof) => new(
        proof.Condition,
        proof.TruthValue.ToString(),
        proof.Reason,
        proof.FormulaText,
        FormatCounterexample(proof.CounterexampleWitness));

    private static string? FormatCounterexample(SymbolicInputWitness witness) {
        if (!witness.IsAvailable || witness.Assignments.Count == 0) return null;
        return string.Join(", ", witness.Assignments
            .OrderBy(static assignment => assignment.SourceName, StringComparer.Ordinal)
            .Select(static assignment => assignment.SourceName + "=" + assignment.Value));
    }

    private static void AddTruncations(
        ImmutableArray<SharpProofTruncationReason>.Builder target,
        SymbolicAnalysisTruncationInfo truncation) =>
        target.AddRange(truncation.Events.Select(static item => new SharpProofTruncationReason(
            item.Code,
            item.Limit,
            item.Observed,
            item.Provenance,
            item.SourceSpanStart)));

    private static SymbolicQueryOptions CreateQueryOptions(
        SharpProofAnalysisOptions options,
        SmtAnalysisService smt) =>
        new(
            smtAnalysis: smt,
            analysisLimits: options.AnalysisBudget);

    private static SymbolicSourceInput CompileSource(string sourceText, string filePath) {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            SymbolicSourceCompilationKind.Query,
            references: null,
            CancellationToken.None);
        return SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation);
    }
}
