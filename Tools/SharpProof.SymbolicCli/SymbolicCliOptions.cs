internal sealed class SymbolicCliOptions
{
    public const string Usage = """
                                Usage: SharpProof.SymbolicCli [explain] (--file <path>|--stdin|--source-text <text>) [--source-file-name <path>] [--project <path>|--solution <path>] (--line <n> [--column <n>] [--line-invariants] | --position <n> | --span-start <n> --span-end <n> | --all-lines) [--json|--compact-json]

                                Options:
                                  --file <path>       C# source file to query.
                                  --stdin             Read C# source text from standard input.
                                  --source-text <text>
                                                      Query inline C# source text.
                                  --source-file-name <path>
                                                      Virtual source path reported for --stdin or --source-text.
                                  --source-map-uri <uri>
                                                      Original source URI for an inline source snippet.
                                  --source-map-original-line <n>
                                                      1-based original line corresponding to snippet line 1. Default: 1.
                                  --source-map-original-column <n>
                                                      1-based original column corresponding to snippet line 1, column 1. Default: 1.
                                  --request-json <json>
                                                      Run a strict schemaVersion 1 request envelope supplied inline.
                                  --request-json-stdin
                                                      Read a strict schemaVersion 1 request envelope from standard input.
                                  --error-json        Emit typed JSON error envelopes on stdout for failed text-mode requests.
                                  --sarif             With explain, emit a SARIF 2.1.0 report.
                                  --markdown          With explain, emit a bounded Markdown report.
                                  --report-max-diagnostics <n>
                                                      Maximum analyzer diagnostics in a machine-readable explain report. Default: 50.
                                  --report-max-hazards <n>
                                                      Maximum runtime hazards in a machine-readable explain report. Default: 50.
                                  --report-max-items <n>
                                                      Maximum facts, reasons, sites, drivers, callees, and workspace messages per explain section. Default: 50.
                                  --project <path>    Load the source through its MSBuild project, including references, parse/compilation options, analyzer config, and AdditionalFiles.
                                  --solution <path>   Load the source through an MSBuild solution. Use --project-name when more than one project compiles the file.
                                  --project-name <name>
                                                      Select a project by project name, assembly name, or project file name.
                                  --configuration <name>
                                                      Set the MSBuild Configuration property while loading a project or solution.
                                  --framework <tfm>   Set the MSBuild TargetFramework property while loading a project or solution.
                                  --msbuild-property <name=value>
                                                      Set an additional MSBuild property. Can be repeated.
                                  explain             Print a contract-oriented explanation for one line or position by composing invariant, hazard, capability, and complexity queries.
                                  --line <n>          1-based source line to query.
                                  --column <n>        1-based source column to query. With --line-invariants, selects the nearest program point on the line.
                                  --line-invariants   Query every statement/expression program point on the line, or the nearest point when --column is supplied.
                                  --span-start <n>    0-based inclusive source span start to query.
                                  --span-end <n>      0-based exclusive source span end to query.
                                  --span-start-line <n>
                                                      1-based span start line for line/column span queries.
                                  --span-start-column <n>
                                                      1-based span start column for line/column span queries.
                                  --span-end-line <n> 1-based span end line for line/column span queries.
                                  --span-end-column <n>
                                                      1-based span end column for line/column span queries.
                                  --all-lines         Query every line that contains statement/expression program points.
                                  --line-expressions  Include expression program points in --line-invariants, --span-start/--span-end, or --all-lines.
                                  --post-line-invariants
                                                      Include facts established by completed declaration/assignment statements on queried lines.
                                  --position <n>      0-based absolute source position to query.
                                  --reference <path>  Metadata reference path. Can be repeated.
                                  --language-version <version>
                                                      C# language version, such as 12, latest, or preview. Default: preview.
                                  --define <symbol>   Preprocessor symbol. Can be repeated.
                                  --nullable <mode>   Nullable context: disable, enable, warnings, or annotations. Default: disable.
                                  --allow-unsafe      Allow unsafe C# in the standalone compilation.
                                  --documentation-mode <mode>
                                                      Documentation mode: none, parse, or diagnose. Default: parse.
                                  --platform <value>  Compilation platform, such as AnyCpu, x64, x86, Arm, or Arm64. Default: AnyCpu.
                                  --optimization <value>
                                                      Optimization level: debug or release. Default: debug.
                                  --assembly-name <name>
                                                      Assembly identity name for the standalone compilation.
                                  --node-kind <kind>  Keep only matching Roslyn node kinds in --line-invariants or --all-lines output. Can be repeated.
                                  --program-point-kind <kind>
                                                      Keep only Statement, Expression, or Other program points. Can be repeated.
                                  --filter-line <n>   Keep only program points on this 1-based line in aggregate output. Can be repeated.
                                  --line-start <n>    Keep only program points at or after this 1-based line.
                                  --line-end <n>      Keep only program points at or before this 1-based line.
                                  --with-facts        Keep only program points that have at least one reported fact.
                                  --with-conditions   Keep only program points that have at least one path condition.
                                  --method <name>     Keep only program points inside a matching method/local function. Can be repeated.
                                  --method-contains <text>
                                                      Keep only program points inside a method/local function containing text. Can be repeated.
                                  --condition-target <target>
                                                      Keep only program points with a path condition for the target. Can be repeated.
                                  --invariant-target <target>
                                                      In compact, invariant, or text output, show per-target invariant summaries only for this target. Can be repeated.
                                  --condition <expr>  Keep only program points with an exact source-like path condition. Can be repeated.
                                  --condition-contains <text>
                                                      Keep only program points with a path condition containing text. Can be repeated.
                                  --reachability <r>  Keep only program points with reachability NotChecked, Unknown, Reachable, or Unreachable. Can be repeated.
                                  --with-proofs       Keep only program points with at least one implication proof result.
                                  --proof-outcome <v> Keep only program points with proof outcome Unknown, ProvenTrue, ProvenFalse, or Unreachable. Can be repeated.
                                  --proof-condition <expr>
                                                      Keep only program points with an exact implication condition. Can be repeated.
                                  --proof-condition-contains <text>
                                                      Keep only program points with an implication condition containing text. Can be repeated.
                                  --check-reachability
                                                      Use bounded SMT to classify whether the queried program point is reachable.
                                  --implies <expr>    Use bounded SMT to prove whether invariants at the queried point imply expr. Can be repeated.
                                  --runtime-hazards   Query proven runtime hazards instead of invariant program points.
                                  --complexity        Query the containing method-like body's conservative time complexity instead of invariants.
                                  --capabilities      Query the containing method-like body's proven capability categories instead of invariants.
                                  --fail-on-hazard    Exit with code 1 when final runtime hazard output contains hazards.
                                  --fail-on-unproven-implies
                                                      Exit with code 1 unless every requested --implies proof is ProvenTrue.
                                  --allowed-capability <name>
                                                      Capability allowed by the CI policy. Can be repeated.
                                  --fail-on-capability-violation
                                                      Exit with code 1 when --capabilities reports a capability outside the allowlist.
                                  --fail-on-capability-unknown
                                                      Exit with code 1 when --capabilities remains conservative or unknown.
                                  --fail-on-complexity-exceeded <bound>
                                                      Exit with code 1 when --complexity provably exceeds the supplied bound.
                                  --fail-on-complexity-unknown
                                                      Exit with code 1 when --complexity is unknown or incomparable to its bound.
                                  --max-conservative-unknowns <n>
                                                      Exit with code 1 when an invariant result exceeds this unknown-fact count.
                                  --hazard-kind <k>   Keep only DirectThrow, Rethrow, DivideByZero, NullDereference, NullableValueWithoutValue, IndexOutOfRange, ArgumentOutOfRange, CheckedIntegralOverflow, ArrayTypeMismatch, UnboxNull, InvalidCast, DynamicNullBinding, or NegativeArrayLength hazards. Can be repeated.
                                  --hazard-status <s> Keep only Proven, Unreachable, Unknown, or Unsupported runtime hazards. Can be repeated.
                                  --hazard-exception-type <type>
                                                      Keep only runtime hazards with this exception type. Can be repeated.
                                  --hazard-category <category>
                                                      Keep only runtime hazards with this category. Can be repeated.
                                  --include-unproven-hazards
                                                      Include unknown, unreachable, and unsupported hazard candidates in runtime hazard output.
                                  --smt-mode <mode>   SMT mode: disabled, bounded, or deep. Default: bounded.
                                  --smt-timeout-ms <n>
                                                      Per-query SMT timeout in milliseconds.
                                  --smt-method-budget-ms <n>
                                                      Total SMT budget for this CLI query in milliseconds.
                                  --smt-max-path-conditions <n>
                                                      Maximum path conditions before conservative fallback.
                                  --smt-max-expression-nodes <n>
                                                      Maximum formula nodes before conservative fallback.
                                  --smt-transient-retries <n>
                                                      Retry count after transient Z3 context failures. Default: 1.
                                  --smt-keep-context-on-transient-failure
                                                      Retry without first recycling the failed thread-local context.
                                  --smt-dispose-context-on-exit
                                                      Dispose the current thread's solver context when the CLI service exits.
                                  --analysis-limit <name>=<n>
                                                      Override a positive bounded-analysis limit. Repeat for multiple limits.
                                  --json              Emit JSON instead of text.
                                  --compact-json      Emit compact bounded JSON for invariants or runtime hazards.
                                  --max-lines <n>     Maximum lines included in --compact-json output. Default: 100.
                                  --max-points <n>    Maximum program points included in --compact-json output. Default: 250.
                                  --max-hazards <n>   Maximum runtime hazards included in --runtime-hazards --compact-json output. Default: 250.
                                  --max-facts <n>     Maximum raw SMT facts included in --compact-json output. Default: 50.
                                  --max-conditions <n>
                                                      Maximum condition strings included in --compact-json output. Default: 50.
                                  --max-proofs <n>    Maximum proof summaries/results included in --compact-json output. Default: 50.
                                  --summary-only      Shorthand for --compact-json with --max-lines 0, --max-points 0, and --max-hazards 0.
                                  --fail-on-compact-truncation
                                                      Exit with code 1 when a compact output limit truncates the result.
                                  --fail-on-compact-threshold <metric=max>
                                                      Exit with code 1 when a compact aggregate count exceeds max. Can be repeated.

                                Runtime hazard notes:
                                  --runtime-hazards accepts --line, --span-start/--span-end, or --all-lines.
                                  Runtime hazard output includes only Proven hazards by default.
                                  Add --include-unproven-hazards to inspect Unknown, Unreachable, or Unsupported candidates.
                                  Use --hazard-kind, --hazard-status, --hazard-exception-type, or --hazard-category to narrow hazards.

                                Analysis limit names:
                                  merged-if-else-facts, merged-switch-facts, merged-try-facts,
                                  try-completion-branches, finite-foreach-element-facts,
                                  scoped-block-completion-statements, structural-null-state-depth,
                                  merged-path-conditions, mergeable-facts-per-target-per-state,
                                  fact-choice-combinations-per-target, guard-facts-per-target-per-state.

                                Complexity notes:
                                  --complexity accepts --line, --line --column, or --position.
                                  Complexity queries resolve the containing method-like body and return a conservative Big-O result.

                                Capability notes:
                                  --capabilities accepts --line, --line --column, or --position.
                                  Capability queries resolve the containing method-like body and return proven capability categories plus unknown reasons.

                                Examples:
                                  SharpProof.SymbolicCli explain --file Example.cs --line 42 --implies "index >= 0"
                                  SharpProof.SymbolicCli explain --project Example.csproj --file src/Example.cs --line 42
                                  SharpProof.SymbolicCli explain --solution Example.sln --project-name Example --file src/Example.cs --line 42
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --line-invariants --compact-json --invariant-target index
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --runtime-hazards
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --complexity
                                  SharpProof.SymbolicCli --file Example.cs --line 42 --capabilities --compact-json
                                  SharpProof.SymbolicCli --file Example.cs --all-lines --runtime-hazards --hazard-kind NullDereference --compact-json
                                  SharpProof.SymbolicCli --file Example.cs --all-lines --runtime-hazards --include-unproven-hazards --hazard-status Unknown --compact-json
                                """;

    public string? FilePath { get; private set; }

    public bool ReadSourceFromStdin { get; private set; }

    public string? InlineSourceText { get; private set; }

    public string? SourceFileName { get; private set; }

    public string? SourceMapUri { get; private set; }

    public int SourceMapOriginalLine { get; private set; } = 1;

    public int SourceMapOriginalColumn { get; private set; } = 1;

    private bool SourceMapOriginalLineSpecified { get; set; }

    private bool SourceMapOriginalColumnSpecified { get; set; }

    public string? ProjectPath { get; private set; }

    public string? SolutionPath { get; private set; }

    public string? ProjectName { get; private set; }

    public Dictionary<string, string> MSBuildProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsProjectAware => ProjectPath != null || SolutionPath != null;

    public bool HasSource => FilePath != null || ReadSourceFromStdin || InlineSourceText != null;

    private bool HasInlineSource => ReadSourceFromStdin || InlineSourceText != null;

    private bool StandaloneCompilationOptionsSpecified { get; set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public bool HasColumn { get; private set; }

    public int? Position { get; private set; }

    public int? SpanStart { get; private set; }

    public int? SpanEnd { get; private set; }

    public int? SpanStartLine { get; private set; }

    public int? SpanStartColumn { get; private set; }

    public int? SpanEndLine { get; private set; }

    public int? SpanEndColumn { get; private set; }

    public bool LineInvariants { get; private set; }

    public bool AllLines { get; private set; }

    public bool LineExpressions { get; private set; }

    public bool PostLineInvariants { get; private set; }

    public List<string> ReferencePaths { get; } = new();

    public LanguageVersion LanguageVersion { get; private set; } = LanguageVersion.Preview;

    public List<string> PreprocessorSymbols { get; } = new();

    public NullableContextOptions NullableContext { get; private set; } = NullableContextOptions.Disable;

    public bool AllowUnsafe { get; private set; }

    public DocumentationMode DocumentationMode { get; private set; } = DocumentationMode.Parse;

    public Platform Platform { get; private set; } = Platform.AnyCpu;

    public OptimizationLevel OptimizationLevel { get; private set; } = OptimizationLevel.Debug;

    public string? AssemblyName { get; private set; }

    public List<string> NodeKinds { get; } = new();

    public List<string> ProgramPointKinds { get; } = new();

    public List<int> FilterLines { get; } = new();

    public int? FilterLineStart { get; private set; }

    public int? FilterLineEnd { get; private set; }

    public bool WithFacts { get; private set; }

    public bool WithConditions { get; private set; }

    public List<string> MethodNames { get; } = new();

    public List<string> MethodNameContains { get; } = new();

    public List<string> ConditionTargets { get; } = new();

    public List<string> InvariantTargets { get; } = new();

    public bool HasInvariantTargetFilter => InvariantTargets.Count != 0;

    public List<string> Conditions { get; } = new();

    public List<string> ConditionContains { get; } = new();

    public List<SymbolicReachability> ReachabilityFilters { get; } = new();

    public bool WithProofs { get; private set; }

    public List<SymbolicTruthValue> ProofOutcomes { get; } = new();

    public List<string> ProofConditions { get; } = new();

    public List<string> ProofConditionContains { get; } = new();

    public bool Json { get; private set; }

    public bool CompactJson { get; private set; }

    public bool CheckReachability { get; private set; }

    public List<string> ImpliedConditions { get; } = new();

    public bool RuntimeHazards { get; private set; }

    public bool Complexity { get; private set; }

    public bool Capabilities { get; private set; }

    public bool Explain { get; private set; }

    public bool FailOnHazard { get; private set; }

    public bool FailOnUnprovenImplies { get; private set; }

    public bool FailOnCapabilityViolation { get; private set; }

    public bool FailOnCapabilityUnknown { get; private set; }

    public List<SymbolicCapability> AllowedCapabilities { get; } = new();

    public SharpProof.Attributes.ComplexityKind? MaximumComplexity { get; private set; }

    public bool FailOnComplexityUnknown { get; private set; }

    public int? MaximumConservativeUnknowns { get; private set; }

    public bool FailOnCompactTruncation { get; private set; }

    public Dictionary<string, int> CompactThresholds { get; } = new(StringComparer.Ordinal);

    public bool IncludeUnprovenHazards { get; private set; }

    public List<SymbolicRuntimeHazardKind> HazardKinds { get; } = new();

    public List<SymbolicRuntimeHazardStatus> HazardStatuses { get; } = new();

    public List<string> HazardExceptionTypes { get; } = new();

    public List<string> HazardCategories { get; } = new();

    public bool ShowHelp { get; private set; }

    public bool ErrorJson { get; private set; }

    public bool Sarif { get; private set; }

    public bool Markdown { get; private set; }

    public int ReportMaxDiagnostics { get; private set; } = 50;

    public int ReportMaxHazards { get; private set; } = 50;

    public int ReportMaxItems { get; private set; } = 50;

    private bool ReportLimitSpecified { get; set; }

    public SmtAnalysisMode SmtMode { get; private set; } = SmtAnalysisOptions.Default.Mode;

    private bool SmtModeSpecified { get; set; }

    public int? SmtTimeoutMs { get; private set; }

    public int? SmtMethodBudgetMs { get; private set; }

    public int? SmtMaxPathConditions { get; private set; }

    public int? SmtMaxExpressionNodes { get; private set; }

    public int SmtTransientRetryCount { get; private set; } =
        SmtSolverLifecycleOptions.Default.MaxTransientRetries;

    private bool SmtTransientRetryCountSpecified { get; set; }

    public bool SmtRecycleContextOnTransientFailure { get; private set; } =
        SmtSolverLifecycleOptions.Default.RecycleContextOnTransientFailure;

    private bool SmtRecycleContextOnTransientFailureSpecified { get; set; }

    public bool SmtDisposeContextOnExit { get; private set; }

    private bool SmtDisposeContextOnExitSpecified { get; set; }

    private SmtAnalysisOptions? ProjectSmtOptions { get; set; }

    private SymbolicAnalysisLimits? ProjectAnalysisLimits { get; set; }

    private Dictionary<string, int> AnalysisLimitOverrides { get; } = new(StringComparer.Ordinal);

    public int CompactMaxLines { get; private set; } = 100;

    public int CompactMaxProgramPoints { get; private set; } = 250;

    public int CompactMaxHazards { get; private set; } = 250;

    public int CompactMaxFacts { get; private set; } = 50;

    public int CompactMaxConditions { get; private set; } = 50;

    public int CompactMaxProofs { get; private set; } = 50;

    public bool HasCompactOutputLimit { get; private set; }

    public bool HasCompactHazardOutputLimit { get; private set; }

    public bool CompactSummaryOnly { get; private set; }

    public bool RequiresSmt => Explain || CheckReachability || ImpliedConditions.Count != 0 || RuntimeHazards;

    public bool HasExitGates =>
        FailOnHazard ||
        FailOnUnprovenImplies ||
        FailOnCapabilityViolation ||
        FailOnCapabilityUnknown ||
        MaximumComplexity.HasValue ||
        FailOnComplexityUnknown ||
        MaximumConservativeUnknowns.HasValue ||
        FailOnCompactTruncation ||
        CompactThresholds.Count != 0;

    public bool IsSpanQuery => SpanStart.HasValue || SpanEnd.HasValue;

    public bool IsLineColumnSpanQuery =>
        SpanStartLine.HasValue ||
        SpanStartColumn.HasValue ||
        SpanEndLine.HasValue ||
        SpanEndColumn.HasValue;

    public bool IsAnySpanQuery => IsSpanQuery || IsLineColumnSpanQuery;

    public bool HasRuntimeHazardFilter =>
        HazardStatuses.Count != 0 ||
        HazardExceptionTypes.Count != 0 ||
        HazardCategories.Count != 0;

    public bool HasResultFilter =>
        NodeKinds.Count != 0 ||
        ProgramPointKinds.Count != 0 ||
        FilterLines.Count != 0 ||
        FilterLineStart.HasValue ||
        FilterLineEnd.HasValue ||
        WithFacts ||
        WithConditions ||
        MethodNames.Count != 0 ||
        MethodNameContains.Count != 0 ||
        ConditionTargets.Count != 0 ||
        Conditions.Count != 0 ||
        ConditionContains.Count != 0 ||
        ReachabilityFilters.Count != 0 ||
        WithProofs ||
        ProofOutcomes.Count != 0 ||
        ProofConditions.Count != 0 ||
        ProofConditionContains.Count != 0;

    private static void NormalizeStringList(List<string> values)
    {
        if (values.Count == 0) return;

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        values.Clear();
        values.AddRange(normalized);
    }

    private delegate void OptionHandler(SymbolicCliOptions options, OptionCursor cursor, string option);

    private sealed class OptionCursor(string[] arguments)
    {
        private int _index = -1;
        public string Current => arguments[_index];
        public bool MoveNext() => ++_index < arguments.Length;
        public string String(string option) => ReadString(arguments, ref _index, option);
        public int PositiveInt(string option) => ReadPositiveInt(arguments, ref _index, option);
        public int NonNegativeInt(string option) => ReadNonNegativeInt(arguments, ref _index, option);
        public LanguageVersion LanguageVersion(string option) => ReadLanguageVersion(arguments, ref _index, option);
        public NullableContextOptions Nullable(string option) => ReadNullableContext(arguments, ref _index, option);
        public DocumentationMode DocumentationMode(string option) => ReadDocumentationMode(arguments, ref _index, option);
        public Platform Platform(string option) => ReadPlatform(arguments, ref _index, option);
        public OptimizationLevel Optimization(string option) => ReadOptimizationLevel(arguments, ref _index, option);
        public string ProgramPointKind(string option) => ReadProgramPointKind(arguments, ref _index, option);
        public SymbolicReachability Reachability(string option) => ReadReachability(arguments, ref _index, option);
        public SymbolicTruthValue TruthValue(string option) => ReadTruthValue(arguments, ref _index, option);
        public SymbolicCapability Capability(string option) => ReadCapability(arguments, ref _index, option);
        public SharpProof.Attributes.ComplexityKind Complexity(string option) => ReadComplexityBound(arguments, ref _index, option);
        public SymbolicRuntimeHazardKind HazardKind(string option) => ReadHazardKind(arguments, ref _index, option);
        public SymbolicRuntimeHazardStatus HazardStatus(string option) => ReadHazardStatus(arguments, ref _index, option);
        public SmtAnalysisMode SmtMode(string option) => ReadSmtMode(arguments, ref _index, option);
    }

    private static readonly IReadOnlyDictionary<string, OptionHandler> OptionHandlers = CreateOptionHandlers();

    private static IReadOnlyDictionary<string, OptionHandler> CreateOptionHandlers()
    {
        var handlers = new Dictionary<string, OptionHandler>(StringComparer.Ordinal);
        void Add(OptionHandler handler, params string[] names)
        {
            foreach (var name in names) handlers.Add(name, handler);
        }

        Add(static (o, _, _) => o.Explain = true, "explain");
        Add(static (o, _, _) => o.ShowHelp = true, "--help", "-h");
        Add(static (o, _, _) => o.ErrorJson = true, SymbolicCliOutputPolicy.ErrorJson);
        Add(static (o, _, _) => o.Sarif = true, SymbolicCliOutputPolicy.Sarif);
        Add(static (o, _, _) => o.Markdown = true, SymbolicCliOutputPolicy.Markdown);
        Add(static (o, c, a) => { o.ReportMaxDiagnostics = c.NonNegativeInt(a); o.ReportLimitSpecified = true; }, "--report-max-diagnostics");
        Add(static (o, c, a) => { o.ReportMaxHazards = c.NonNegativeInt(a); o.ReportLimitSpecified = true; }, "--report-max-hazards");
        Add(static (o, c, a) => { o.ReportMaxItems = c.NonNegativeInt(a); o.ReportLimitSpecified = true; }, "--report-max-items");
        Add(static (o, c, a) => o.FilePath = c.String(a), "--file");
        Add(static (o, _, _) => o.ReadSourceFromStdin = true, "--stdin");
        Add(static (o, c, a) => o.InlineSourceText = c.String(a), "--source-text");
        Add(static (o, c, a) => o.SourceFileName = c.String(a), "--source-file-name");
        Add(static (o, c, a) => o.SourceMapUri = c.String(a), "--source-map-uri");
        Add(static (o, c, a) => { o.SourceMapOriginalLine = c.PositiveInt(a); o.SourceMapOriginalLineSpecified = true; }, "--source-map-original-line");
        Add(static (o, c, a) => { o.SourceMapOriginalColumn = c.PositiveInt(a); o.SourceMapOriginalColumnSpecified = true; }, "--source-map-original-column");
        Add(static (o, c, a) => o.ProjectPath = c.String(a), "--project");
        Add(static (o, c, a) => o.SolutionPath = c.String(a), "--solution");
        Add(static (o, c, a) => o.ProjectName = c.String(a), "--project-name");
        Add(static (o, c, a) => o.MSBuildProperties["Configuration"] = c.String(a), "--configuration");
        Add(static (o, c, a) => o.MSBuildProperties["TargetFramework"] = c.String(a), "--framework", "--target-framework");
        Add(static (o, c, a) => o.AddMSBuildProperty(c.String(a), a), "--msbuild-property");
        Add(static (o, c, a) => o.Line = c.PositiveInt(a), "--line");
        Add(static (o, c, a) => { o.Column = c.PositiveInt(a); o.HasColumn = true; }, "--column");
        Add(static (o, c, a) => o.Position = c.NonNegativeInt(a), "--position");
        Add(static (o, c, a) => o.SpanStart = c.NonNegativeInt(a), "--span-start");
        Add(static (o, c, a) => o.SpanEnd = c.NonNegativeInt(a), "--span-end");
        Add(static (o, c, a) => o.SpanStartLine = c.PositiveInt(a), "--span-start-line");
        Add(static (o, c, a) => o.SpanStartColumn = c.PositiveInt(a), "--span-start-column");
        Add(static (o, c, a) => o.SpanEndLine = c.PositiveInt(a), "--span-end-line");
        Add(static (o, c, a) => o.SpanEndColumn = c.PositiveInt(a), "--span-end-column");
        Add(static (o, _, _) => o.LineInvariants = true, "--line-invariants", "--all-line-points");
        Add(static (o, _, _) => o.AllLines = true, "--all-lines", "--file-invariants");
        Add(static (o, _, _) => o.LineExpressions = true, "--line-expressions", "--include-expressions");
        Add(static (o, _, _) => o.PostLineInvariants = true, "--post-line-invariants");
        Add(static (o, c, a) => { o.ReferencePaths.Add(c.String(a)); o.StandaloneCompilationOptionsSpecified = true; }, "--reference", "-r");
        Add(static (o, c, a) => { o.LanguageVersion = c.LanguageVersion(a); o.StandaloneCompilationOptionsSpecified = true; }, "--language-version", "--lang-version");
        Add(static (o, c, a) => { o.PreprocessorSymbols.Add(c.String(a)); o.StandaloneCompilationOptionsSpecified = true; }, "--define", "-d");
        Add(static (o, c, a) => { o.NullableContext = c.Nullable(a); o.StandaloneCompilationOptionsSpecified = true; }, "--nullable");
        Add(static (o, _, _) => { o.AllowUnsafe = true; o.StandaloneCompilationOptionsSpecified = true; }, "--allow-unsafe", "--unsafe");
        Add(static (o, c, a) => { o.DocumentationMode = c.DocumentationMode(a); o.StandaloneCompilationOptionsSpecified = true; }, "--documentation-mode");
        Add(static (o, c, a) => { o.Platform = c.Platform(a); o.StandaloneCompilationOptionsSpecified = true; }, "--platform");
        Add(static (o, c, a) => { o.OptimizationLevel = c.Optimization(a); o.StandaloneCompilationOptionsSpecified = true; }, "--optimization", "--optimize");
        Add(static (o, c, a) => { o.AssemblyName = c.String(a); o.StandaloneCompilationOptionsSpecified = true; }, "--assembly-name");
        Add(static (o, c, a) => o.NodeKinds.Add(c.String(a)), "--node-kind");
        Add(static (o, c, a) => o.ProgramPointKinds.Add(c.ProgramPointKind(a)), "--program-point-kind", "--point-kind");
        Add(static (o, c, a) => o.FilterLines.Add(c.PositiveInt(a)), "--filter-line");
        Add(static (o, c, a) => o.FilterLineStart = c.PositiveInt(a), "--line-start");
        Add(static (o, c, a) => o.FilterLineEnd = c.PositiveInt(a), "--line-end");
        Add(static (o, _, _) => o.WithFacts = true, "--with-facts");
        Add(static (o, _, _) => o.WithConditions = true, "--with-conditions");
        Add(static (o, c, a) => o.MethodNames.Add(c.String(a)), "--method");
        Add(static (o, c, a) => o.MethodNameContains.Add(c.String(a)), "--method-contains");
        Add(static (o, c, a) => o.ConditionTargets.Add(c.String(a)), "--condition-target", "--target");
        Add(static (o, c, a) => o.InvariantTargets.Add(c.String(a)), "--invariant-target", "--focus-target");
        Add(static (o, c, a) => o.Conditions.Add(c.String(a)), "--condition");
        Add(static (o, c, a) => o.ConditionContains.Add(c.String(a)), "--condition-contains");
        Add(static (o, c, a) => o.ReachabilityFilters.Add(c.Reachability(a)), "--reachability");
        Add(static (o, _, _) => o.WithProofs = true, "--with-proofs");
        Add(static (o, c, a) => o.ProofOutcomes.Add(c.TruthValue(a)), "--proof-outcome");
        Add(static (o, c, a) => o.ProofConditions.Add(c.String(a)), "--proof-condition");
        Add(static (o, c, a) => o.ProofConditionContains.Add(c.String(a)), "--proof-condition-contains");
        Add(static (o, _, _) => o.Json = true, SymbolicCliOutputPolicy.Json);
        Add(static (o, _, _) => o.CompactJson = true, SymbolicCliOutputPolicy.CompactJson, SymbolicCliOutputPolicy.Compact);
        Add(static (o, c, a) => { o.CompactMaxLines = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; }, "--max-lines");
        Add(static (o, c, a) => { o.CompactMaxProgramPoints = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; }, "--max-points");
        Add(static (o, c, a) => { o.CompactMaxHazards = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; o.HasCompactHazardOutputLimit = true; }, "--max-hazards");
        Add(static (o, c, a) => { o.CompactMaxFacts = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; }, "--max-facts");
        Add(static (o, c, a) => { o.CompactMaxConditions = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; }, "--max-conditions");
        Add(static (o, c, a) => { o.CompactMaxProofs = c.NonNegativeInt(a); o.HasCompactOutputLimit = true; }, "--max-proofs");
        Add(static (o, _, _) => { o.CompactSummaryOnly = true; o.CompactJson = true; }, "--summary-only");
        Add(static (o, _, _) => o.FailOnCompactTruncation = true, "--fail-on-compact-truncation");
        Add(static (o, c, a) => o.AddCompactThreshold(c.String(a), a), "--fail-on-compact-threshold");
        Add(static (o, _, _) => o.CheckReachability = true, "--check-reachability");
        Add(static (o, c, a) => o.ImpliedConditions.Add(c.String(a)), "--implies");
        Add(static (o, _, _) => o.RuntimeHazards = true, "--runtime-hazards");
        Add(static (o, _, _) => o.Complexity = true, "--complexity");
        Add(static (o, _, _) => o.Capabilities = true, "--capabilities");
        Add(static (o, _, _) => o.FailOnHazard = true, "--fail-on-hazard");
        Add(static (o, _, _) => o.FailOnUnprovenImplies = true, "--fail-on-unproven-implies");
        Add(static (o, c, a) => o.AllowedCapabilities.Add(c.Capability(a)), "--allowed-capability");
        Add(static (o, _, _) => o.FailOnCapabilityViolation = true, "--fail-on-capability-violation");
        Add(static (o, _, _) => o.FailOnCapabilityUnknown = true, "--fail-on-capability-unknown");
        Add(static (o, c, a) => o.MaximumComplexity = c.Complexity(a), "--fail-on-complexity-exceeded");
        Add(static (o, _, _) => o.FailOnComplexityUnknown = true, "--fail-on-complexity-unknown");
        Add(static (o, c, a) => o.MaximumConservativeUnknowns = c.NonNegativeInt(a), "--max-conservative-unknowns");
        Add(static (o, c, a) => o.HazardKinds.Add(c.HazardKind(a)), "--hazard-kind");
        Add(static (o, c, a) => o.HazardStatuses.Add(c.HazardStatus(a)), "--hazard-status");
        Add(static (o, c, a) => o.HazardExceptionTypes.Add(c.String(a)), "--hazard-exception-type", "--exception-type");
        Add(static (o, c, a) => o.HazardCategories.Add(c.String(a)), "--hazard-category");
        Add(static (o, _, _) => o.IncludeUnprovenHazards = true, "--include-unproven-hazards");
        Add(static (o, c, a) => { o.SmtMode = c.SmtMode(a); o.SmtModeSpecified = true; }, "--smt-mode");
        Add(static (o, c, a) => o.SmtTimeoutMs = c.PositiveInt(a), "--smt-timeout-ms");
        Add(static (o, c, a) => o.SmtMethodBudgetMs = c.PositiveInt(a), "--smt-method-budget-ms");
        Add(static (o, c, a) => o.SmtMaxPathConditions = c.PositiveInt(a), "--smt-max-path-conditions");
        Add(static (o, c, a) => o.SmtMaxExpressionNodes = c.PositiveInt(a), "--smt-max-expression-nodes");
        Add(static (o, c, a) => { o.SmtTransientRetryCount = c.NonNegativeInt(a); o.SmtTransientRetryCountSpecified = true; }, "--smt-transient-retries");
        Add(static (o, _, _) => { o.SmtRecycleContextOnTransientFailure = false; o.SmtRecycleContextOnTransientFailureSpecified = true; }, "--smt-keep-context-on-transient-failure");
        Add(static (o, _, _) => { o.SmtDisposeContextOnExit = true; o.SmtDisposeContextOnExitSpecified = true; }, "--smt-dispose-context-on-exit");
        Add(static (o, c, a) => o.AddAnalysisLimitOverride(c.String(a), a), "--analysis-limit");
        return handlers;
    }

    public static SymbolicCliOptions Parse(string[] args)
    {
        var options = new SymbolicCliOptions();
        var cursor = new OptionCursor(args);
        while (cursor.MoveNext())
        {
            var arg = cursor.Current;
            if (!OptionHandlers.TryGetValue(arg, out var handler))
                throw new ArgumentException($"Unknown option '{arg}'.");
            handler(options, cursor, arg);
        }

        if (!options.ShowHelp)
        {
            NormalizeStringList(options.InvariantTargets);
            NormalizeStringList(options.PreprocessorSymbols);
            _ = options.CreateCompilationProfile();

            if (options.CompactSummaryOnly)
            {
                options.CompactMaxLines = 0;
                options.CompactMaxProgramPoints = 0;
                options.CompactMaxHazards = 0;
            }

            if (options.Json && options.CompactJson)
                throw new ArgumentException("--json cannot be combined with --compact-json.");

            if ((options.Sarif ? 1 : 0) +
                (options.Markdown ? 1 : 0) +
                (options.Json ? 1 : 0) +
                (options.CompactJson ? 1 : 0) > 1)
                throw new ArgumentException(
                    "--json, --compact-json, --sarif, and --markdown are mutually exclusive.");

            if ((options.Sarif || options.Markdown) && !options.Explain)
                throw new ArgumentException("--sarif and --markdown require explain.");

            if (options.ReportLimitSpecified && !options.Explain)
                throw new ArgumentException(
                    "--report-max-diagnostics, --report-max-hazards, and --report-max-items require explain.");

            if (options.Json && options.HasInvariantTargetFilter && !options.Explain)
                throw new ArgumentException(
                    "--invariant-target cannot be combined with --json; use text or --compact-json.");

            if (options.HasCompactOutputLimit && !options.CompactJson)
                throw new ArgumentException(
                    "--max-lines, --max-points, --max-hazards, --max-facts, --max-conditions, and --max-proofs require --compact-json.");

            if ((options.FailOnCompactTruncation || options.CompactThresholds.Count != 0) &&
                !options.CompactJson)
                throw new ArgumentException(
                    "--fail-on-compact-truncation and --fail-on-compact-threshold require --compact-json.");

            if (options.FailOnUnprovenImplies && options.ImpliedConditions.Count == 0)
                throw new ArgumentException("--fail-on-unproven-implies requires at least one --implies condition.");

            if (!options.Capabilities &&
                (options.FailOnCapabilityViolation ||
                 options.FailOnCapabilityUnknown ||
                 options.AllowedCapabilities.Count != 0))
                throw new ArgumentException(
                    "--allowed-capability, --fail-on-capability-violation, and --fail-on-capability-unknown require --capabilities.");

            if (options.AllowedCapabilities.Count != 0 && !options.FailOnCapabilityViolation)
                throw new ArgumentException(
                    "--allowed-capability requires --fail-on-capability-violation.");

            if (!options.Complexity &&
                (options.MaximumComplexity.HasValue || options.FailOnComplexityUnknown))
                throw new ArgumentException(
                    "--fail-on-complexity-exceeded and --fail-on-complexity-unknown require --complexity.");

            if (options.MaximumConservativeUnknowns.HasValue &&
                (options.RuntimeHazards || options.Complexity || options.Capabilities))
                throw new ArgumentException(
                    "--max-conservative-unknowns is supported only for invariant query results.");

            if (options.HasCompactHazardOutputLimit && !options.RuntimeHazards)
                throw new ArgumentException("--max-hazards requires --runtime-hazards.");

            if (!options.RuntimeHazards &&
                (options.IncludeUnprovenHazards ||
                 options.FailOnHazard ||
                 options.HazardKinds.Count != 0 ||
                 options.HazardStatuses.Count != 0 ||
                 options.HazardExceptionTypes.Count != 0 ||
                 options.HazardCategories.Count != 0))
                throw new ArgumentException(
                    "--fail-on-hazard, --hazard-kind, --hazard-status, --hazard-exception-type, --hazard-category, and --include-unproven-hazards require --runtime-hazards.");

            if (options.HazardStatuses.Any(static status => status != SymbolicRuntimeHazardStatus.Proven) &&
                !options.IncludeUnprovenHazards)
                throw new ArgumentException(
                    "--hazard-status values other than Proven require --include-unproven-hazards.");

            var sourceCount = (options.FilePath != null ? 1 : 0) +
                              (options.ReadSourceFromStdin ? 1 : 0) +
                              (options.InlineSourceText != null ? 1 : 0);
            if (sourceCount == 0)
                throw new ArgumentException("Specify one source input: --file, --stdin, or --source-text.");

            if (sourceCount > 1)
                throw new ArgumentException("--file, --stdin, and --source-text are mutually exclusive.");

            if (options.ProjectPath != null && options.SolutionPath != null)
                throw new ArgumentException("--project cannot be combined with --solution.");

            if (options.IsProjectAware && options.FilePath == null)
                throw new ArgumentException("--project and --solution require --file.");

            if (!options.IsProjectAware && options.FilePath != null && !File.Exists(CliHost.GetFullPath(options.FilePath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.SourceNotFound,
                    SymbolicErrorCategory.Input,
                    "--file does not exist: " + options.FilePath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.FilePath);

            if (options.SourceFileName != null && !options.HasInlineSource)
                throw new ArgumentException("--source-file-name requires --stdin or --source-text.");

            if (string.IsNullOrWhiteSpace(options.SourceFileName) && options.SourceFileName != null)
                throw new ArgumentException("--source-file-name requires a non-empty path.");

            if (options.SourceMapUri != null && !options.HasInlineSource)
                throw new ArgumentException("--source-map-uri requires --stdin or --source-text.");

            if ((options.SourceMapOriginalLineSpecified || options.SourceMapOriginalColumnSpecified) &&
                options.SourceMapUri == null)
                throw new ArgumentException(
                    "--source-map-original-line and --source-map-original-column require --source-map-uri.");

            if (string.IsNullOrWhiteSpace(options.SourceMapUri) && options.SourceMapUri != null)
                throw new ArgumentException("--source-map-uri requires a non-empty URI.");

            if (options.ProjectPath != null && !File.Exists(CliHost.GetFullPath(options.ProjectPath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.ProjectLoadFailed,
                    SymbolicErrorCategory.Project,
                    "--project does not exist: " + options.ProjectPath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.ProjectPath);

            if (options.SolutionPath != null && !File.Exists(CliHost.GetFullPath(options.SolutionPath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.ProjectLoadFailed,
                    SymbolicErrorCategory.Project,
                    "--solution does not exist: " + options.SolutionPath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.SolutionPath);

            if (!options.IsProjectAware && options.ProjectName != null)
                throw new ArgumentException("--project-name requires --project or --solution.");

            if (!options.IsProjectAware && options.MSBuildProperties.Count != 0)
                throw new ArgumentException(
                    "--configuration, --framework, and --msbuild-property require --project or --solution.");

            if (options.IsProjectAware && options.StandaloneCompilationOptionsSpecified)
                throw new ArgumentException(
                    "Standalone compilation options cannot be combined with --project or --solution; configure the project instead.");

            if (options.Position.HasValue && options.Line != 0)
                throw new ArgumentException("--position cannot be combined with --line.");

            if (options.Position.HasValue && options.IsAnySpanQuery)
                throw new ArgumentException("--position cannot be combined with span query options.");

            if (options.IsAnySpanQuery && options.Line != 0)
                throw new ArgumentException("Span query options cannot be combined with --line.");

            if (options.IsAnySpanQuery && options.LineInvariants)
                throw new ArgumentException("Span query options cannot be combined with --line-invariants.");

            if (options.IsAnySpanQuery && options.Column != 1)
                throw new ArgumentException("Span query options cannot be combined with --column.");

            if (options.IsSpanQuery && (!options.SpanStart.HasValue || !options.SpanEnd.HasValue))
                throw new ArgumentException("--span-start and --span-end must be provided together.");

            if (options.IsLineColumnSpanQuery &&
                (!options.SpanStartLine.HasValue ||
                 !options.SpanStartColumn.HasValue ||
                 !options.SpanEndLine.HasValue ||
                 !options.SpanEndColumn.HasValue))
                throw new ArgumentException(
                    "--span-start-line, --span-start-column, --span-end-line, and --span-end-column must be provided together.");

            if (options.IsSpanQuery && options.IsLineColumnSpanQuery)
                throw new ArgumentException("Absolute span options cannot be combined with line/column span options.");

            if (options.SpanEnd.HasValue &&
                options.SpanStart.HasValue &&
                options.SpanEnd.Value < options.SpanStart.Value)
                throw new ArgumentException("--span-end cannot be less than --span-start.");

            if (options.SpanStartLine.HasValue &&
                options.SpanEndLine.HasValue &&
                (options.SpanEndLine.Value < options.SpanStartLine.Value ||
                 (options.SpanEndLine.Value == options.SpanStartLine.Value &&
                  options.SpanEndColumn!.Value < options.SpanStartColumn!.Value)))
                throw new ArgumentException("Line/column span end cannot be before span start.");

            if (options.AllLines &&
                (options.Position.HasValue || options.IsAnySpanQuery || options.Line != 0 || options.Column != 1 ||
                 options.LineInvariants))
                throw new ArgumentException(
                    "--all-lines cannot be combined with --line, --column, --position, span query options, or --line-invariants.");

            if (options.Position.HasValue && options.LineInvariants)
                throw new ArgumentException("--line-invariants cannot be combined with --position.");

            if (options.RuntimeHazards && options.Position.HasValue)
                throw new ArgumentException(
                    "--runtime-hazards supports --line, --span-start/--span-end, or --all-lines, not --position.");

            if (options.RuntimeHazards && options.HasInvariantTargetFilter)
                throw new ArgumentException("--invariant-target cannot be combined with --runtime-hazards.");

            if (options.RuntimeHazards && (options.LineInvariants || options.LineExpressions ||
                                           options.PostLineInvariants || options.Column != 1 ||
                                           options.IsLineColumnSpanQuery))
                throw new ArgumentException(
                    "--runtime-hazards cannot be combined with --line-invariants, --line-expressions, --post-line-invariants, --column, or line/column span options.");

            if (options.RuntimeHazards && (options.ImpliedConditions.Count != 0 || options.CheckReachability ||
                                           options.HasResultFilter))
                throw new ArgumentException(
                    "--runtime-hazards cannot be combined with invariant proof, reachability, or program-point filters.");

            if (options.RuntimeHazards && options.Complexity)
                throw new ArgumentException("--runtime-hazards cannot be combined with --complexity.");

            if (options.RuntimeHazards && options.Capabilities)
                throw new ArgumentException("--runtime-hazards cannot be combined with --capabilities.");

            if (options.Complexity) options.ValidateFocusedAnalysisCompatibility("--complexity");

            if (options.LineExpressions && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines.");

            if (options.PostLineInvariants && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "--post-line-invariants requires --line-invariants, --span-start/--span-end, or --all-lines.");

            if (options.FilterLineStart.HasValue &&
                options.FilterLineEnd.HasValue &&
                options.FilterLineStart.Value > options.FilterLineEnd.Value)
                throw new ArgumentException("--line-start cannot be greater than --line-end.");

            if (!options.AllLines && !options.Position.HasValue && !options.IsAnySpanQuery && options.Line == 0)
                throw new ArgumentException("--line, --position, --span-start/--span-end, or --all-lines is required.");

            if (options.Explain)
            {
                options.CheckReachability = true;
                if (options.CompactJson)
                    throw new ArgumentException(
                        "explain supports text, --json, --sarif, or --markdown, not compact query output formats.");

                if (options.RuntimeHazards || options.Complexity || options.Capabilities)
                    throw new ArgumentException(
                        "explain cannot be combined with --runtime-hazards, --complexity, or --capabilities.");

                if (options.AllLines || options.IsAnySpanQuery || options.LineInvariants)
                    throw new ArgumentException("explain supports --line, --line with --column, or --position only.");

                if (options.HasExitGates)
                    throw new ArgumentException(
                        "CI exit gates require a focused query mode and cannot be combined with explain.");
            }

            if (options.Complexity && options.Line == 0 && !options.Position.HasValue)
                throw new ArgumentException("--complexity requires --line or --position.");

            if (options.Complexity && options.Capabilities)
                throw new ArgumentException("--complexity cannot be combined with --capabilities.");

            if (options.Capabilities) options.ValidateFocusedAnalysisCompatibility("--capabilities");

            if (options.Capabilities && options.Line == 0 && !options.Position.HasValue)
                throw new ArgumentException("--capabilities requires --line or --position.");

            if (options.HasResultFilter && !options.AllLines && !options.LineInvariants && !options.IsAnySpanQuery)
                throw new ArgumentException(
                    "Result filters require --line-invariants, --span-start/--span-end, or --all-lines.");

            options.ValidateCompactThresholds();

            foreach (var referencePath in options.ReferencePaths)
                if (!File.Exists(CliHost.GetFullPath(referencePath)))
                    throw SymbolicCliErrorWriter.CreateException(
                        SymbolicErrorCodes.ReferenceNotFound,
                        SymbolicErrorCategory.Input,
                        "--reference does not exist: " + referencePath,
                        SymbolicErrorExitCodes.MissingInput,
                        "path",
                        referencePath);
        }

        return options;
    }

    private void ValidateFocusedAnalysisCompatibility(string optionName)
    {
        if (HasInvariantTargetFilter)
            throw new ArgumentException($"--invariant-target cannot be combined with {optionName}.");

        if (HasCompactOutputLimit)
            throw new ArgumentException(
                $"--max-lines, --max-points, --max-hazards, --max-facts, --max-conditions, and --max-proofs are not supported with {optionName}.");

        if (AllLines || IsAnySpanQuery || LineInvariants)
            throw new ArgumentException($"{optionName} supports --line, --line with --column, or --position only.");

        if (LineExpressions || PostLineInvariants || HasResultFilter)
            throw new ArgumentException($"{optionName} cannot be combined with invariant program-point filters.");

        if (ImpliedConditions.Count != 0 || CheckReachability)
            throw new ArgumentException(
                $"{optionName} cannot be combined with implied-condition proofs or reachability checks.");
    }

    public SymbolicSourceCompilationProfile CreateCompilationProfile()
    {
        return new SymbolicSourceCompilationProfile(
            LanguageVersion,
            PreprocessorSymbols,
            NullableContext,
            AllowUnsafe,
            DocumentationMode,
            Platform,
            OptimizationLevel,
            AssemblyName);
    }

    public SymbolicSourceInput CreateSourceInput(string? standardInput = null)
    {
        if (IsProjectAware)
            throw new InvalidOperationException("Project-aware source input must be loaded through MSBuild.");

        if (FilePath != null)
            return SymbolicSourceInput.FromFile(CliHost.GetFullPath(FilePath), CreateCompilationProfile());

        var sourceText = ReadSourceFromStdin
            ? standardInput ?? throw new InvalidOperationException("Standard input was not read.")
            : InlineSourceText ?? throw new InvalidOperationException("Inline source text is required.");
        var input = SymbolicSourceInput.FromTextWithProfile(
            sourceText,
            CreateCompilationProfile(),
            SourceFileName);
        return SourceMapUri == null
            ? input
            : input.WithSourceMap(new SymbolicSourceMap(
                SourceMapUri,
                SourceMapOriginalLine,
                SourceMapOriginalColumn));
    }

    public void ApplyProjectConfiguration(SharpProofProjectAnalysisContext? context)
    {
        ProjectSmtOptions = context?.SmtOptions;
        ProjectAnalysisLimits = context?.AnalysisLimits;
    }

    public SymbolicSourceQueryFilter CreateResultFilter()
    {
        return new SymbolicSourceQueryFilter(
            NodeKinds,
            WithFacts,
            ReachabilityFilters,
            MethodNames,
            WithConditions,
            ConditionTargets,
            Conditions,
            ConditionContains,
            MethodNameContains,
            FilterLines,
            FilterLineStart,
            FilterLineEnd,
            ProgramPointKinds,
            WithProofs,
            ProofOutcomes,
            ProofConditions,
            ProofConditionContains);
    }

    public SmtAnalysisOptions CreateSmtOptions()
    {
        var projectOptions = ProjectSmtOptions;
        var mode = SmtModeSpecified ? SmtMode : projectOptions?.Mode ?? SmtMode;
        var defaults = projectOptions != null && projectOptions.Mode == mode
            ? projectOptions
            : SmtAnalysisOptions.ForMode(mode);
        var lifecycleDefaults = defaults.Lifecycle;
        return defaults
            .WithOverrides(
                SmtTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(SmtTimeoutMs.Value) : null,
                SmtMethodBudgetMs.HasValue ? TimeSpan.FromMilliseconds(SmtMethodBudgetMs.Value) : null,
                SmtMaxPathConditions,
                SmtMaxExpressionNodes)
            .WithLifecycle(new SmtSolverLifecycleOptions(
                SmtTransientRetryCountSpecified
                    ? SmtTransientRetryCount
                    : lifecycleDefaults.MaxTransientRetries,
                SmtRecycleContextOnTransientFailureSpecified
                    ? SmtRecycleContextOnTransientFailure
                    : lifecycleDefaults.RecycleContextOnTransientFailure,
                SmtDisposeContextOnExitSpecified
                    ? SmtDisposeContextOnExit
                    : lifecycleDefaults.DisposeCurrentThreadContextOnServiceDispose));
    }

    public SymbolicQueryOptions CreateQueryOptions(
        SmtAnalysisService? smtAnalysis,
        bool includeResultFilter)
    {
        return new SymbolicQueryOptions(
                CreateReferences(),
                smtAnalysis,
                ImpliedConditions,
                LineExpressions,
                PostLineInvariants,
                includeResultFilter && HasResultFilter ? CreateResultFilter() : null)
            .WithAnalysisLimits(CreateAnalysisLimits());
    }

    public SymbolicAnalysisLimits CreateAnalysisLimits()
    {
        var defaults = ProjectAnalysisLimits ?? SymbolicAnalysisLimits.Default;
        return new SymbolicAnalysisLimits(
            GetAnalysisLimit("merged-if-else-facts", defaults.MaxMergedIfElseFacts),
            GetAnalysisLimit("merged-switch-facts", defaults.MaxMergedSwitchFacts),
            GetAnalysisLimit("merged-try-facts", defaults.MaxMergedTryFacts),
            GetAnalysisLimit("try-completion-branches", defaults.MaxTryCompletionBranches),
            GetAnalysisLimit("finite-foreach-element-facts", defaults.MaxFiniteForeachElementFacts),
            GetAnalysisLimit(
                "scoped-block-completion-statements",
                defaults.MaxScopedBlockCompletionStatements),
            GetAnalysisLimit("structural-null-state-depth", defaults.MaxStructuralNullStateDepth),
            GetAnalysisLimit("merged-path-conditions", defaults.MaxMergedPathConditions),
            GetAnalysisLimit(
                "mergeable-facts-per-target-per-state",
                defaults.MaxMergeableFactsPerTargetPerState),
            GetAnalysisLimit(
                "fact-choice-combinations-per-target",
                defaults.MaxFactChoiceCombinationsPerTarget),
            GetAnalysisLimit("guard-facts-per-target-per-state", defaults.MaxGuardFactsPerTargetPerState));
    }

    public SymbolicQueryTarget CreateQueryTarget()
    {
        if (AllLines) return SymbolicQueryTarget.AllLines();

        if (LineInvariants)
            return HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);

        if (IsAnySpanQuery)
            return IsLineColumnSpanQuery
                ? SymbolicQueryTarget.LineSpan(
                    SpanStartLine!.Value,
                    SpanStartColumn!.Value,
                    SpanEndLine!.Value,
                    SpanEndColumn!.Value)
                : SymbolicQueryTarget.Span(SpanStart!.Value, SpanEnd!.Value);

        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : SymbolicQueryTarget.Point(Line, Column);
    }

    public SymbolicQueryTarget CreateRuntimeHazardTarget()
    {
        if (AllLines) return SymbolicQueryTarget.AllLines();

        return IsSpanQuery
            ? SymbolicQueryTarget.Span(SpanStart!.Value, SpanEnd!.Value)
            : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicQueryTarget CreateComplexityTarget()
    {
        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicQueryTarget CreateCapabilityTarget()
    {
        return Position.HasValue
            ? SymbolicQueryTarget.Position(Position.Value)
            : HasColumn
                ? SymbolicQueryTarget.Point(Line, Column)
                : SymbolicQueryTarget.Line(Line);
    }

    public SymbolicRuntimeHazardQueryOptions CreateRuntimeHazardOptions()
    {
        return new SymbolicRuntimeHazardQueryOptions(
            IncludeUnprovenHazards,
            HazardKinds);
    }

    public SymbolicRuntimeHazardQueryResult FilterRuntimeHazards(SymbolicRuntimeHazardQueryResult result)
    {
        if (!HasRuntimeHazardFilter) return result;

        var hazards = result.Hazards
            .Where(hazard =>
                (HazardStatuses.Count == 0 || HazardStatuses.Contains(hazard.Status)) &&
                (HazardExceptionTypes.Count == 0 ||
                 HazardExceptionTypes.Contains(hazard.ExceptionType, StringComparer.OrdinalIgnoreCase)) &&
                (HazardCategories.Count == 0 ||
                 HazardCategories.Contains(hazard.Category, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return new SymbolicRuntimeHazardQueryResult(
            result.FilePath,
            result.LineCount,
            result.ScopeStart,
            result.ScopeEnd,
            result.Line,
            hazards,
            result.SmtDiagnostics);
    }

    private void ValidateCompactThresholds()
    {
        if (CompactThresholds.Count == 0) return;

        string[] allowedMetrics;
        if (RuntimeHazards)
            allowedMetrics = new[] { "hazards" };
        else if (Capabilities)
            allowedMetrics = new[] { "capability-sites", "capability-unknowns" };
        else if (Complexity)
            allowedMetrics = new[] { "complexity-drivers", "complexity-unknowns" };
        else
            allowedMetrics = new[]
            {
                "program-points",
                "conservative-unknowns",
                "proof-unknowns",
                "reachability-unknowns"
            };

        var unsupported = CompactThresholds.Keys
            .Where(metric => !allowedMetrics.Contains(metric, StringComparer.Ordinal))
            .OrderBy(static metric => metric, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length == 0) return;

        throw new ArgumentException(
            "--fail-on-compact-threshold metric(s) " +
            string.Join(", ", unsupported) +
            " are not supported for this query mode. Supported metrics: " +
            string.Join(", ", allowedMetrics) + ".");
    }

    public IEnumerable<MetadataReference>? CreateReferences()
    {
        if (ReferencePaths.Count == 0) return null;

        return ReferencePaths.Select(static path => MetadataReference.CreateFromFile(CliHost.GetFullPath(path)));
    }

    private static string ReadString(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length) throw new ArgumentException(optionName + " requires a value.");

        return args[++index];
    }

    private static int ReadInt(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (!int.TryParse(value, out var parsed))
            throw new ArgumentException(optionName + " requires an integer value.");

        return parsed;
    }

    private static int ReadPositiveInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed <= 0) throw new ArgumentException(optionName + " requires a positive integer value.");

        return parsed;
    }

    private static int ReadNonNegativeInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed < 0) throw new ArgumentException(optionName + " requires a non-negative integer value.");

        return parsed;
    }

    private void AddAnalysisLimitOverride(string value, string optionName)
    {
        AddNamedInteger(
            value, optionName, AnalysisLimitOverrides, IsAnalysisLimitName,
            "<name>=<positive-integer>", "limit name", 1);
    }

    private void AddCompactThreshold(string value, string optionName)
    {
        AddNamedInteger(
            value, optionName, CompactThresholds, IsCompactThresholdName,
            "<metric>=<non-negative-integer>", "metric", 0);
    }

    private static void AddNamedInteger(
        string value,
        string optionName,
        IDictionary<string, int> destination,
        Func<string, bool> isKnownName,
        string requirement,
        string nameDescription,
        int minimum)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ArgumentException(optionName + " requires " + requirement + ".");

        var name = value[..separator].Trim().ToLowerInvariant();
        if (!isKnownName(name))
            throw new ArgumentException(optionName + " has an unknown " + nameDescription + " '" + name + "'.");

        if (!int.TryParse(
                value[(separator + 1)..].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
            out var parsed) ||
            parsed < minimum)
            throw new ArgumentException(optionName + " requires " + requirement + ".");

        destination[name] = parsed;
    }

    private void AddMSBuildProperty(string value, string optionName)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0)
            throw new ArgumentException(optionName + " requires <name>=<value>.");

        var name = value[..separator].Trim();
        var propertyValue = value[(separator + 1)..].Trim();
        if (name.Length == 0 || propertyValue.Length == 0)
            throw new ArgumentException(optionName + " requires <name>=<value>.");

        MSBuildProperties[name] = propertyValue;
    }

    private int GetAnalysisLimit(string name, int fallback)
    {
        return AnalysisLimitOverrides.TryGetValue(name, out var value) ? value : fallback;
    }

    private static bool IsAnalysisLimitName(string name)
    {
        return name is "merged-if-else-facts" or
            "merged-switch-facts" or
            "merged-try-facts" or
            "try-completion-branches" or
            "finite-foreach-element-facts" or
            "scoped-block-completion-statements" or
            "structural-null-state-depth" or
            "merged-path-conditions" or
            "mergeable-facts-per-target-per-state" or
            "fact-choice-combinations-per-target" or
            "guard-facts-per-target-per-state";
    }

    private static bool IsCompactThresholdName(string name)
    {
        return name is "program-points" or
            "conservative-unknowns" or
            "proof-unknowns" or
            "reachability-unknowns" or
            "hazards" or
            "capability-sites" or
            "capability-unknowns" or
            "complexity-drivers" or
            "complexity-unknowns";
    }

    private static LanguageVersion ReadLanguageVersion(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim();
        if (LanguageVersionFacts.TryParse(value, out var languageVersion)) return languageVersion;

        throw new ArgumentException(optionName + " requires a recognized C# language version.");
    }

    private static NullableContextOptions ReadNullableContext(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim().ToLowerInvariant();
        return value switch
        {
            "disable" or "disabled" => NullableContextOptions.Disable,
            "enable" or "enabled" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => throw new ArgumentException(
                optionName + " must be disable, enable, warnings, or annotations.")
        };
    }

    private static DocumentationMode ReadDocumentationMode(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<DocumentationMode>(
            args, ref index, optionName, "must be none, parse, or diagnose.");
    }

    private static Platform ReadPlatform(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<Platform>(
            args, ref index, optionName, "requires a recognized Roslyn platform value.");
    }

    private static OptimizationLevel ReadOptimizationLevel(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<OptimizationLevel>(
            args, ref index, optionName, "must be debug or release.");
    }

    private static SmtAnalysisMode ReadSmtMode(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim().ToLowerInvariant();
        switch (value)
        {
            case "disabled":
                return SmtAnalysisMode.Off;
            case "bounded":
                return SmtAnalysisMode.Bounded;
            case "deep":
                return SmtAnalysisMode.Deep;
            default:
                throw new ArgumentException(optionName + " must be disabled, bounded, or deep.");
        }
    }

    private static SymbolicRuntimeHazardKind ReadHazardKind(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<SymbolicRuntimeHazardKind>(
            args,
            ref index,
            optionName,
            "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardKind>()) + ".");
    }

    private static SymbolicRuntimeHazardStatus ReadHazardStatus(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<SymbolicRuntimeHazardStatus>(
            args,
            ref index,
            optionName,
            "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardStatus>()) + ".");
    }

    private static SymbolicCapability ReadCapability(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<SymbolicCapability>(
            args,
            ref index,
            optionName,
            "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicCapability>()) + ".");
    }

    private static SharpProof.Attributes.ComplexityKind ReadComplexityBound(
        string[] args,
        ref int index,
        string optionName)
    {
        return ReadDefinedEnum<SharpProof.Attributes.ComplexityKind>(
            args,
            ref index,
            optionName,
            "must be one of: " + string.Join(", ", Enum.GetNames<SharpProof.Attributes.ComplexityKind>()) + ".");
    }

    private static SymbolicReachability ReadReachability(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<SymbolicReachability>(
            args, ref index, optionName, "must be NotChecked, Unknown, Reachable, or Unreachable.");
    }

    private static SymbolicTruthValue ReadTruthValue(string[] args, ref int index, string optionName)
    {
        return ReadDefinedEnum<SymbolicTruthValue>(
            args, ref index, optionName, "must be Unknown, ProvenTrue, ProvenFalse, or Unreachable.");
    }

    private static TEnum ReadDefinedEnum<TEnum>(
        string[] args,
        ref int index,
        string optionName,
        string requirement)
        where TEnum : struct, Enum
    {
        var value = ReadString(args, ref index, optionName).Trim();
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;

        throw new ArgumentException(optionName + " " + requirement);
    }

    private static string ReadProgramPointKind(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim();
        if (SymbolicProgramPointKinds.TryNormalizeKnownKind(value, out var normalizedKind)) return normalizedKind;

        throw new ArgumentException(optionName + " must be Statement, Expression, or Other.");
    }
}
