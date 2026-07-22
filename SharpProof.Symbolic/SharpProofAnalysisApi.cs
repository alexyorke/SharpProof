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

public sealed record SharpProofAnalysisOptions(SharpProofAnalysisBudget? AnalysisBudget = null);

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

    internal static SharpProofAnalysisBudget FromNamedValues(SharpProofAnalysisBudget defaults, Func<string, int, int> getValue) {
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

public sealed record SharpProofUnknownReason(string Code, string Category, string Message, bool IsRetryable, bool IsConfigurationRelated);

public sealed record SharpProofTruncationReason(string Code, int Limit, int Observed, string Provenance, int? SourceSpanStart);

public sealed record SharpProofProofFact(string Condition, string Status, string Reason, string SymbolicCondition, string? Counterexample);

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
    private readonly SharpProofAnalysisBudget _analysisLimits;
    private readonly SymbolicConditionProofEngine _conditionProofEngine;
    private readonly SymbolicInvariantService _invariantService;
    private readonly SmtAnalysisService _ownedSmtAnalysis;
    private readonly SymbolicRuntimeHazardQueryService _runtimeHazardService;
    private readonly SymbolicSourceInput _source;
    private bool _disposed;

    private SharpProofAnalysisSession(
        SymbolicSourceInput source,
        SharpProofAnalysisBudget analysisLimits,
        SmtAnalysisService ownedSmtAnalysis) {
        _source = source;
        _analysisLimits = analysisLimits;
        _ownedSmtAnalysis = ownedSmtAnalysis;
        _invariantService = new SymbolicInvariantService();
        _conditionProofEngine = new SymbolicConditionProofEngine(_invariantService);
        _runtimeHazardService = new SymbolicRuntimeHazardQueryService(_invariantService);
    }
    public static SharpProofAnalysisSession FromText(string sourceText, string? filePath = null,
        SharpProofAnalysisOptions? options = null) {
        options ??= new SharpProofAnalysisOptions();
        var source = CompileSource(sourceText, filePath ?? "SharpProof.Symbolic.Query.cs");
        var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return new SharpProofAnalysisSession(source, ResolveAnalysisLimits(options), smt);
    }
    public static SharpProofAnalysisSession FromFile(string filePath, SharpProofAnalysisOptions? options = null) {
        options ??= new SharpProofAnalysisOptions();
        var fullPath = Path.GetFullPath(filePath);
        var source = CompileSource(File.ReadAllText(fullPath), fullPath);
        var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return new SharpProofAnalysisSession(source, ResolveAnalysisLimits(options), smt);
    }
    public SharpProofAnalysisResult Analyze(SharpProofAnalysisRequest request, CancellationToken cancellationToken = default) {
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
    private SharpProofAnalysisResult Execute(SharpProofAnalysisRequest request, CancellationToken cancellationToken) {
        try {
            MethodEffects? effects = null;
            var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
            var proofFacts = ImmutableArray.CreateBuilder<SharpProofProofFact>();
            var hazards = ImmutableArray.CreateBuilder<SharpProofHazard>();
            var truncations = ImmutableArray.CreateBuilder<SharpProofTruncationReason>();
            string? complexity = null;
            if ((request.Facets & SharpProofAnalysisFacet.Effects) != 0) {
                effects = AnalyzeMethodEffects(request.Target, cancellationToken);
                unknowns.AddRange(effects.UnknownReasons);
            }
            if ((request.Facets & SharpProofAnalysisFacet.ProofFacts) != 0)
                AnalyzeProofFacts(request, proofFacts, unknowns, truncations, cancellationToken);

            if ((request.Facets & SharpProofAnalysisFacet.RuntimeHazards) != 0)
                AnalyzeHazards(request.Target, hazards, unknowns, truncations, cancellationToken);

            if ((request.Facets & SharpProofAnalysisFacet.Complexity) != 0)
                complexity = AnalyzeComplexity(request.Target, unknowns, cancellationToken);

            return new SharpProofAnalysisResult(
                request.Target,
                unknowns.Count == 0 && truncations.Count == 0
                    ? SharpProofQueryStatus.Succeeded
                    : SharpProofQueryStatus.Unknown,
                effects,
                proofFacts.ToImmutable(),
                hazards.ToImmutable(),
                complexity,
                [.. unknowns.Distinct()],
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
                [],
                [],
                null,
                [],
                [],
                error);
        }
    }
    private MethodEffects AnalyzeMethodEffects(SharpProofTarget target, CancellationToken cancellationToken) {
        if (target.Kind is SharpProofTargetKind.Span or SharpProofTargetKind.AllLines)
            return AnalyzeSyntaxTree(_source.SyntaxTree, _source.Compilation, target, cancellationToken);

        return SymbolicMethodLikeQueryDispatcher.Execute(
            _source,
            target,
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
            [.. exceptions.Distinct()],
            [.. sites.Distinct()],
            [.. unknowns.Distinct()]);
    }
    private void AnalyzeProofFacts(
        SharpProofAnalysisRequest request,
        ImmutableArray<SharpProofProofFact>.Builder facts,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        ImmutableArray<SharpProofTruncationReason>.Builder truncations,
        CancellationToken cancellationToken) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        if (!string.IsNullOrWhiteSpace(request.Condition)) {
            if (request.Target.Kind != SharpProofTargetKind.Point)
                throw new ArgumentException("Condition proof requests require a point target.", nameof(request));
            var proof = _conditionProofEngine.ProveAtSyntaxTree(
                _source.SyntaxTree,
                _source.Compilation,
                request.Target.Line!.Value,
                request.Target.Column ?? 1,
                request.Condition!,
                _ownedSmtAnalysis,
                cancellationToken);
            facts.Add(Project(proof));
            if (proof.TruthValue == SymbolicTruthValue.Unknown)
                unknowns.Add(Convert(SymbolicUnknownReasonTaxonomy.ForProof(SymbolicUnknownReason.Unknown, proof.Reason)));
            AddTruncations(truncations, proof.AnalysisTruncation);
            return;
        }
        var points = QueryProgramPoints(request.Target, cancellationToken);
        var hasUnknown = points.Any(static point => point.Reachability == SymbolicReachability.Unknown);
        facts.Add(new SharpProofProofFact(
            "invariant",
            hasUnknown ? "Unknown" : "Proven",
            "merged_symbolic_invariant",
            SymbolicMergedPathFactMerger.MergeInvariantText(points),
            null));
        foreach (var point in points)
            if (point.Reachability == SymbolicReachability.Unknown)
                unknowns.Add(Convert(SymbolicUnknownReasonTaxonomy.ForProof(SymbolicUnknownReason.Unknown, point.ReachabilityReason)));
        AddTruncations(truncations, SymbolicAnalysisTruncationInfo.Combine(
            points.Select(static point => point.AnalysisTruncation)));
    }
    private void AnalyzeHazards(
        SharpProofTarget target,
        ImmutableArray<SharpProofHazard>.Builder hazards,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        ImmutableArray<SharpProofTruncationReason>.Builder truncations,
        CancellationToken cancellationToken) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        var result = _runtimeHazardService.QuerySyntaxTreeRuntimeHazards(
            _source.SyntaxTree,
            _source.Compilation,
            target,
            _ownedSmtAnalysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(true));
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
            .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or SymbolicRuntimeHazardStatus.Unsupported)
            .Select(static hazard => Convert(hazard.UnknownReasonInfo)));
        AddTruncations(truncations, result.AnalysisTruncation);
    }
    private string AnalyzeComplexity(
        SharpProofTarget target,
        ImmutableArray<SharpProofUnknownReason>.Builder unknowns,
        CancellationToken cancellationToken) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        var result = SymbolicMethodLikeQueryDispatcher.Execute(
            _source,
            target,
            "Complexity queries support point, position, or line targets only.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true),
            static (resolved, compilation, token) => {
                if (resolved.BodyNode == null)
                    throw new ArgumentException("The requested method-like declaration does not have a body.");
                if (resolved.MethodSymbol == null)
                    throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");
                return new SymbolicComplexityAnalysisSession(compilation, token).Analyze(resolved);
            },
            cancellationToken);
        unknowns.AddRange(result.UnknownReasons.Select(static reason => Convert(SymbolicUnknownReasonTaxonomy.ForComplexity(reason))));
        return result.Complexity.Text;
    }
    private IReadOnlyList<SymbolicProgramPointAnalysis> QueryProgramPoints(
        SharpProofTarget target,
        CancellationToken cancellationToken) {
        var syntaxTree = _source.SyntaxTree;
        var compilation = _source.Compilation;
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var points = new List<SymbolicProgramPointAnalysis>();

        void Add(SyntaxNode node) => points.Add(node is ForStatementSyntax forStatement
            ? _invariantService.AnalyzeForInitialEntry(forStatement, semanticModel, _ownedSmtAnalysis, cancellationToken)
            : _invariantService.AnalyzeAt(node, semanticModel, _ownedSmtAnalysis, cancellationToken));

        switch (target.Kind) {
            case SharpProofTargetKind.Point:
                var pointPosition = SymbolicSourceLocation.GetPosition(
                    syntaxTree, target.Line!.Value, target.Column ?? 1, cancellationToken);
                var lineNodes = SymbolicSourceTargetSelector.FindOnLine(syntaxTree, target.Line.Value, cancellationToken);
                if (lineNodes.Count == 0) throw new ArgumentException("No program points found on --line.", nameof(target));
                Add(SymbolicSourceTargetSelector.SelectNearest(lineNodes, pointPosition));
                break;
            case SharpProofTargetKind.Position:
                var position = target.Position!.Value;
                var text = syntaxTree.GetText(cancellationToken);
                if (position < 0 || position > text.Length)
                    throw new ArgumentOutOfRangeException(nameof(target), "--position must be within the source text span.");
                Add(SymbolicSourceTargetSelector.FindAtPosition(root, position));
                break;
            case SharpProofTargetKind.Line:
                foreach (var node in SymbolicSourceTargetSelector.FindOnLine(syntaxTree, target.Line!.Value, cancellationToken)) Add(node);
                break;
            case SharpProofTargetKind.Span:
                var span = SymbolicSourceLocation.GetSourceSpan(
                    syntaxTree, target.SpanStart!.Value, target.SpanEnd!.Value, cancellationToken);
                foreach (var node in SymbolicSourceTargetSelector.FindInSpan(syntaxTree, span, cancellationToken)) Add(node);
                break;
            case SharpProofTargetKind.AllLines:
                var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
                for (var line = 1; line <= lineCount; line++)
                    foreach (var node in SymbolicSourceTargetSelector.FindOnLine(syntaxTree, line, cancellationToken)) Add(node);
                break;
            default:
                throw new NotSupportedException("Target kind is not supported for syntax tree queries.");
        }
        return points;
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
    private static void AddTruncations(ImmutableArray<SharpProofTruncationReason>.Builder target,
        SymbolicAnalysisTruncationInfo truncation) =>
        target.AddRange(truncation.Events.Select(static item => new SharpProofTruncationReason(
            item.Code,
            item.Limit,
            item.Observed,
            item.Provenance,
            item.SourceSpanStart)));

    private static SharpProofAnalysisBudget ResolveAnalysisLimits(SharpProofAnalysisOptions options) =>
        (options.AnalysisBudget ?? SharpProofAnalysisBudget.Default).Validate();

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
