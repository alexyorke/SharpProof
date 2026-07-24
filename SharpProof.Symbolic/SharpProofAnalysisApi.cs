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
    private readonly ImmutableArray<Diagnostic> _sourceCompilationErrors;
    private readonly ImmutableArray<Diagnostic> _sourceSyntaxErrors;
    private bool _disposed;
    private SharpProofAnalysisSession(
        SymbolicSourceInput source,
        SharpProofAnalysisBudget analysisLimits,
        SmtAnalysisService ownedSmtAnalysis) {
        _source = source;
        _sourceSyntaxErrors = [.. source.SyntaxTree.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error)];
        _sourceCompilationErrors = [.. source.Compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error)];
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
            var target = ResolveRequest(request, cancellationToken);
            if (TryCreateSourceError(out var sourceError))
                return Failed(request.Target, sourceError);
            using var smtBudgetScope = _ownedSmtAnalysis.BeginMethodBudgetScope();
            var result = new AnalysisResultAccumulator(request.Target);
            if ((request.Facets & SharpProofAnalysisFacet.Effects) != 0)
                result.Add(AnalyzeMethodEffects(target, cancellationToken));
            if ((request.Facets & SharpProofAnalysisFacet.ProofFacts) != 0)
                AnalyzeProofFacts(request, target, result, cancellationToken);
            if ((request.Facets & SharpProofAnalysisFacet.RuntimeHazards) != 0)
                AnalyzeHazards(target, result, cancellationToken);
            if ((request.Facets & SharpProofAnalysisFacet.Complexity) != 0)
                AnalyzeComplexity(target, result);
            return result.Build();
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception)) {
            var error = SymbolicErrorClassifier.FromException(exception);
            return Failed(request.Target, error);
        }
    }
    private static SharpProofAnalysisResult Failed(SharpProofTarget target, SharpProofError error) => new(
        target,
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
    private sealed class AnalysisResultAccumulator(SharpProofTarget target) {
        private readonly List<SharpProofProofFact> _facts = [];
        private readonly List<SharpProofHazard> _hazards = [];
        private readonly List<SharpProofUnknownReason> _unknowns = [];
        private readonly List<SharpProofTruncationReason> _truncations = [];
        private MethodEffects? _effects;
        private string? _complexity;
        internal void Add(MethodEffects effects) {
            _effects = effects;
            _unknowns.AddRange(effects.UnknownReasons);
        }
        internal void Add(SymbolicConditionProofResult proof) {
            _facts.Add(Project(proof));
            if (proof.TruthValue == SymbolicTruthValue.Unknown)
                _unknowns.Add(Convert(SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown, proof.Reason)));
            Add(proof.AnalysisTruncation);
        }
        internal void Add(IReadOnlyList<SymbolicProgramPointAnalysis> points) {
            var hasUnknown = points.Any(static point => point.Reachability == SymbolicReachability.Unknown);
            _facts.Add(new SharpProofProofFact(
                "invariant",
                hasUnknown ? "Unknown" : "Proven",
                "merged_symbolic_invariant",
                SymbolicMergedPathFactMerger.MergeInvariantText(points),
                null));
            _unknowns.AddRange(points
                .Where(static point => point.Reachability == SymbolicReachability.Unknown)
                .Select(static point => Convert(SymbolicUnknownReasonTaxonomy.ForProof(
                    SymbolicUnknownReason.Unknown, point.ReachabilityReason))));
            Add(SymbolicAnalysisTruncationInfo.Combine(points.Select(static point => point.AnalysisTruncation)));
        }
        internal void Add(SymbolicRuntimeHazardQueryResult result) {
            _hazards.AddRange(result.Hazards.Select(static hazard => new SharpProofHazard(
                hazard.Kind.ToString(), hazard.Status.ToString(), hazard.StatusReason, hazard.ExceptionType,
                hazard.OperationText, hazard.SpanStart, hazard.SpanEnd,
                FormatCounterexample(hazard.TriggerWitness))));
            _unknowns.AddRange(result.Hazards
                .Where(static hazard => hazard.Status is SymbolicRuntimeHazardStatus.Unknown or
                    SymbolicRuntimeHazardStatus.Unsupported)
                .Select(static hazard => Convert(hazard.UnknownReasonInfo)));
            Add(result.AnalysisTruncation);
        }
        internal void Add(SymbolicComplexityResult result) {
            _complexity = result.Complexity.Text;
            _unknowns.AddRange(result.UnknownReasons.Select(static reason =>
                Convert(SymbolicUnknownReasonTaxonomy.ForComplexity(reason))));
        }
        private void Add(SymbolicAnalysisTruncationInfo truncation) =>
            _truncations.AddRange(truncation.Events.Select(static item => new SharpProofTruncationReason(
                item.Code, item.Limit, item.Observed, item.Provenance, item.SourceSpanStart)));
        internal SharpProofAnalysisResult Build() => new(
            target,
            _unknowns.Count == 0 && _truncations.Count == 0
                ? SharpProofQueryStatus.Succeeded
                : SharpProofQueryStatus.Unknown,
            _effects,
            [.. _facts],
            [.. _hazards],
            _complexity,
            [.. _unknowns.Distinct()],
            [.. _truncations],
            null);
    }
    private ResolvedQueryTarget ResolveRequest(
        SharpProofAnalysisRequest request,
        CancellationToken cancellationToken) {
        var target = request.Target ?? throw new ArgumentException("A query target is required.", nameof(request));
        var resolved = ResolvedQueryTarget.Create(_source, target, cancellationToken, nameof(request));
        if (request.Facets == SharpProofAnalysisFacet.None ||
            (request.Facets & ~SharpProofAnalysisFacet.All) != 0)
            throw new ArgumentException("At least one defined analysis facet is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Condition)) return resolved;
        if ((request.Facets & SharpProofAnalysisFacet.ProofFacts) == 0)
            throw new ArgumentException("A proof condition requires the proof-facts facet.", nameof(request));
        if (target.Kind != SharpProofTargetKind.Point)
            throw new ArgumentException("Condition proof requests require a point target.", nameof(request));
        var parsedCondition = SyntaxFactory.ParseExpression(request.Condition!);
        var parseError = parsedCondition.GetDiagnostics().FirstOrDefault(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseError != null)
            throw new FormatException("The proof condition is not valid C#: " + parseError.GetMessage());
        return resolved;
    }
    private bool TryCreateSourceError(out SharpProofError error) {
        var errors = !_sourceSyntaxErrors.IsDefaultOrEmpty
            ? _sourceSyntaxErrors
            : _sourceCompilationErrors;
        if (errors.IsDefaultOrEmpty) {
            error = null!;
            return false;
        }
        var syntaxFailure = !_sourceSyntaxErrors.IsDefaultOrEmpty;
        var first = errors[0];
        var details = ImmutableDictionary<string, string>.Empty
            .Add("diagnosticCount", errors.Length.ToString(CultureInfo.InvariantCulture))
            .Add("diagnostics", string.Join(Environment.NewLine, errors.Take(20).Select(static diagnostic =>
                diagnostic.ToString())));
        error = new SharpProofError(
            syntaxFailure ? SymbolicErrorCodes.ParseFailed : SymbolicErrorCodes.CompilationFailed,
            syntaxFailure ? SharpProofErrorCategory.Parse : SharpProofErrorCategory.Input,
            (syntaxFailure ? "Source parsing failed: " : "Source compilation failed: ") + first.GetMessage(),
            SymbolicErrorExitCodes.InvalidData,
            false,
            details);
        return true;
    }
    private MethodEffects AnalyzeMethodEffects(ResolvedQueryTarget target, CancellationToken cancellationToken) {
        if (target.Target.Kind is SharpProofTargetKind.Span or SharpProofTargetKind.AllLines)
            return AnalyzeSyntaxTree(target, cancellationToken);
        return target.Execute(
            "Method-effect analysis supports point, position, line, span, or all-lines targets.",
            (resolved, compilation, token) => {
                if (resolved.MethodSymbol == null)
                    throw new ArgumentException("Could not resolve the target method.");
                return new MethodEffectAnalysisSession(compilation, token, smtAnalysis: _ownedSmtAnalysis).Analyze(
                    resolved.MethodSymbol,
                    resolved.Declaration,
                    resolved.SemanticModel);
            });
    }
    private MethodEffects AnalyzeSyntaxTree(ResolvedQueryTarget target, CancellationToken cancellationToken) {
        var declarations = target.Root.DescendantNodesAndSelf()
            .Where(static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true));
        if (target.Target.Kind == SharpProofTargetKind.Span) {
            var span = target.Span!.Value;
            declarations = declarations.Where(declaration => span.IsEmpty
                ? declaration.FullSpan.Contains(span.Start)
                : declaration.FullSpan.OverlapsWith(span));
        }
        var session = new MethodEffectAnalysisSession(
            target.Source.Compilation, cancellationToken, smtAnalysis: _ownedSmtAnalysis);
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var combinedEffects = SharpProofEffect.None;
        var capabilities = SharpProofCapability.None;
        var exceptions = ImmutableArray.CreateBuilder<MethodExceptionFact>();
        var sites = ImmutableArray.CreateBuilder<MethodEffectSite>();
        var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        foreach (var declaration in declarations) {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ResolvedMethodLikeTarget.Create(declaration, target.SemanticModel, cancellationToken);
            if (resolved.BodyNode == null || resolved.MethodSymbol == null ||
                !seen.Add(resolved.MethodSymbol.OriginalDefinition))
                continue;
            var method = session.Analyze(resolved.MethodSymbol, resolved.Declaration, target.SemanticModel);
            combinedEffects |= method.Effects;
            capabilities |= method.Capabilities;
            exceptions.AddRange(method.ExceptionFacts);
            sites.AddRange(method.Sites);
            unknowns.AddRange(method.UnknownReasons);
        }
        if (seen.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(target), "No analyzable method bodies were found in the requested target.");
        return new MethodEffects(
            combinedEffects,
            capabilities,
            [.. exceptions.Distinct()],
            [.. sites.Distinct()],
            [.. unknowns.Distinct()]);
    }
    private void AnalyzeProofFacts(
        SharpProofAnalysisRequest request,
        ResolvedQueryTarget target,
        AnalysisResultAccumulator result,
        CancellationToken cancellationToken) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        if (!string.IsNullOrWhiteSpace(request.Condition)) {
            if (!target.HasProgramPointOnTargetLine)
                throw new ArgumentOutOfRangeException(nameof(request), "No program point exists at the requested point.");
            var proof = _conditionProofEngine.ProveAtSyntaxTree(
                target.Source.SyntaxTree,
                target.Source.Compilation,
                request.Target.Line!.Value,
                request.Target.Column ?? 1,
                request.Condition!,
                _ownedSmtAnalysis,
                cancellationToken);
            result.Add(proof);
            return;
        }
        result.Add(QueryProgramPoints(target, cancellationToken));
    }
    private void AnalyzeHazards(
        ResolvedQueryTarget target,
        AnalysisResultAccumulator accumulator,
        CancellationToken cancellationToken) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        accumulator.Add(_runtimeHazardService.QuerySyntaxTreeRuntimeHazards(
            target,
            _ownedSmtAnalysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(true)));
    }
    private void AnalyzeComplexity(
        ResolvedQueryTarget target,
        AnalysisResultAccumulator accumulator) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(_analysisLimits);
        accumulator.Add(target.Execute(
            "Complexity queries support point, position, or line targets only.",
            static (resolved, compilation, token) => {
                if (resolved.BodyNode == null)
                    throw new ArgumentException("The requested method-like declaration does not have a body.");
                if (resolved.MethodSymbol == null)
                    throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");
                return new SymbolicComplexityAnalysisSession(compilation, token).Analyze(resolved);
            }));
    }
    private IReadOnlyList<SymbolicProgramPointAnalysis> QueryProgramPoints(
        ResolvedQueryTarget target,
        CancellationToken cancellationToken) {
        if (target.ProgramPointNodes.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(target), "No program points were found in the requested target.");
        return target.ProgramPointNodes.Select(node => node is ForStatementSyntax forStatement
                ? _invariantService.AnalyzeForInitialEntry(
                    forStatement, target.SemanticModel, _ownedSmtAnalysis, cancellationToken)
                : _invariantService.AnalyzeAt(
                    node, target.SemanticModel, _ownedSmtAnalysis, cancellationToken))
            .ToArray();
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
