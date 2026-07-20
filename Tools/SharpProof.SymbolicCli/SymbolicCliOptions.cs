internal sealed class SymbolicCliOptions
{
    public static string Usage { get; } = ToolEmbeddedText.Load(
        typeof(SymbolicCliOptions).Assembly,
        "SharpProof.SymbolicCli.Usage.txt");

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

    public bool FailOnAnalysisTruncation { get; private set; }

    public Dictionary<string, int> Thresholds { get; } = new(StringComparer.Ordinal);

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

    private SharpProofAnalysisBudget? ProjectAnalysisLimits { get; set; }

    private Dictionary<string, int> AnalysisLimitOverrides { get; } = new(StringComparer.Ordinal);

    public bool RequiresSmt => Explain || CheckReachability || ImpliedConditions.Count != 0 || RuntimeHazards;

    public bool HasExitGates =>
        FailOnHazard ||
        FailOnUnprovenImplies ||
        FailOnCapabilityViolation ||
        FailOnCapabilityUnknown ||
        MaximumComplexity.HasValue ||
        FailOnComplexityUnknown ||
        MaximumConservativeUnknowns.HasValue ||
        FailOnAnalysisTruncation ||
        Thresholds.Count != 0;

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

    private static readonly ToolOptionSet<SymbolicCliOptions> OptionHandlers =
        new ToolOptionSet<SymbolicCliOptions>()
        .Add(static (o, _, _) => o.Explain = true, "explain")
        .Add(static (o, _, _) => o.ShowHelp = true, "--help", "-h")
        .Add(static (o, _, _) => o.ErrorJson = true, SymbolicCliOutputPolicy.ErrorJson)
        .Add(static (o, _, _) => o.Sarif = true, SymbolicCliOutputPolicy.Sarif)
        .Add(static (o, _, _) => o.Markdown = true, SymbolicCliOutputPolicy.Markdown)
        .Add(static (o, c, a) => { o.ReportMaxDiagnostics = c.Int32(a, 0); o.ReportLimitSpecified = true; }, "--report-max-diagnostics")
        .Add(static (o, c, a) => { o.ReportMaxHazards = c.Int32(a, 0); o.ReportLimitSpecified = true; }, "--report-max-hazards")
        .Add(static (o, c, a) => { o.ReportMaxItems = c.Int32(a, 0); o.ReportLimitSpecified = true; }, "--report-max-items")
        .Add(static (o, c, a) => o.FilePath = c.RequiredValue(a), "--file")
        .Add(static (o, _, _) => o.ReadSourceFromStdin = true, "--stdin")
        .Add(static (o, c, a) => o.InlineSourceText = c.RequiredValue(a), "--source-text")
        .Add(static (o, c, a) => o.SourceFileName = c.RequiredValue(a), "--source-file-name")
        .Add(static (o, c, a) => o.SourceMapUri = c.RequiredValue(a), "--source-map-uri")
        .Add(static (o, c, a) => { o.SourceMapOriginalLine = c.Int32(a, 1); o.SourceMapOriginalLineSpecified = true; }, "--source-map-original-line")
        .Add(static (o, c, a) => { o.SourceMapOriginalColumn = c.Int32(a, 1); o.SourceMapOriginalColumnSpecified = true; }, "--source-map-original-column")
        .Add(static (o, c, a) => o.ProjectPath = c.RequiredValue(a), "--project")
        .Add(static (o, c, a) => o.SolutionPath = c.RequiredValue(a), "--solution")
        .Add(static (o, c, a) => o.ProjectName = c.RequiredValue(a), "--project-name")
        .Add(static (o, c, a) => o.MSBuildProperties["Configuration"] = c.RequiredValue(a), "--configuration")
        .Add(static (o, c, a) => o.MSBuildProperties["TargetFramework"] = c.RequiredValue(a), "--framework", "--target-framework")
        .Add(static (o, c, a) => o.AddMSBuildProperty(c.RequiredValue(a), a), "--msbuild-property")
        .Add(static (o, c, a) => o.Line = c.Int32(a, 1), "--line")
        .Add(static (o, c, a) => { o.Column = c.Int32(a, 1); o.HasColumn = true; }, "--column")
        .Add(static (o, c, a) => o.Position = c.Int32(a, 0), "--position")
        .Add(static (o, c, a) => o.SpanStart = c.Int32(a, 0), "--span-start")
        .Add(static (o, c, a) => o.SpanEnd = c.Int32(a, 0), "--span-end")
        .Add(static (o, c, a) => o.SpanStartLine = c.Int32(a, 1), "--span-start-line")
        .Add(static (o, c, a) => o.SpanStartColumn = c.Int32(a, 1), "--span-start-column")
        .Add(static (o, c, a) => o.SpanEndLine = c.Int32(a, 1), "--span-end-line")
        .Add(static (o, c, a) => o.SpanEndColumn = c.Int32(a, 1), "--span-end-column")
        .Add(static (o, _, _) => o.LineInvariants = true, "--line-invariants", "--all-line-points")
        .Add(static (o, _, _) => o.AllLines = true, "--all-lines", "--file-invariants")
        .Add(static (o, _, _) => o.LineExpressions = true, "--line-expressions", "--include-expressions")
        .Add(static (o, _, _) => o.PostLineInvariants = true, "--post-line-invariants")
        .Add(static (o, c, a) => { o.ReferencePaths.Add(c.RequiredValue(a)); o.StandaloneCompilationOptionsSpecified = true; }, "--reference", "-r")
        .Add(static (o, c, a) => { o.LanguageVersion = ReadLanguageVersion(c, a); o.StandaloneCompilationOptionsSpecified = true; }, "--language-version", "--lang-version")
        .Add(static (o, c, a) => { o.PreprocessorSymbols.Add(c.RequiredValue(a)); o.StandaloneCompilationOptionsSpecified = true; }, "--define", "-d")
        .Add(static (o, c, a) => { o.NullableContext = ReadNullableContext(c, a); o.StandaloneCompilationOptionsSpecified = true; }, "--nullable")
        .Add(static (o, _, _) => { o.AllowUnsafe = true; o.StandaloneCompilationOptionsSpecified = true; }, "--allow-unsafe", "--unsafe")
        .Add(static (o, c, a) => { o.DocumentationMode = c.DefinedEnum<DocumentationMode>(a, "must be none, parse, or diagnose."); o.StandaloneCompilationOptionsSpecified = true; }, "--documentation-mode")
        .Add(static (o, c, a) => { o.Platform = c.DefinedEnum<Platform>(a, "requires a recognized Roslyn platform value."); o.StandaloneCompilationOptionsSpecified = true; }, "--platform")
        .Add(static (o, c, a) => { o.OptimizationLevel = c.DefinedEnum<OptimizationLevel>(a, "must be debug or release."); o.StandaloneCompilationOptionsSpecified = true; }, "--optimization", "--optimize")
        .Add(static (o, c, a) => { o.AssemblyName = c.RequiredValue(a); o.StandaloneCompilationOptionsSpecified = true; }, "--assembly-name")
        .Add(static (o, c, a) => o.NodeKinds.Add(c.RequiredValue(a)), "--node-kind")
        .Add(static (o, c, a) => o.ProgramPointKinds.Add(ReadProgramPointKind(c, a)), "--program-point-kind", "--point-kind")
        .Add(static (o, c, a) => o.FilterLines.Add(c.Int32(a, 1)), "--filter-line")
        .Add(static (o, c, a) => o.FilterLineStart = c.Int32(a, 1), "--line-start")
        .Add(static (o, c, a) => o.FilterLineEnd = c.Int32(a, 1), "--line-end")
        .Add(static (o, _, _) => o.WithFacts = true, "--with-facts")
        .Add(static (o, _, _) => o.WithConditions = true, "--with-conditions")
        .Add(static (o, c, a) => o.MethodNames.Add(c.RequiredValue(a)), "--method")
        .Add(static (o, c, a) => o.MethodNameContains.Add(c.RequiredValue(a)), "--method-contains")
        .Add(static (o, c, a) => o.ConditionTargets.Add(c.RequiredValue(a)), "--condition-target", "--target")
        .Add(static (o, c, a) => o.InvariantTargets.Add(c.RequiredValue(a)), "--invariant-target", "--focus-target")
        .Add(static (o, c, a) => o.Conditions.Add(c.RequiredValue(a)), "--condition")
        .Add(static (o, c, a) => o.ConditionContains.Add(c.RequiredValue(a)), "--condition-contains")
        .Add(static (o, c, a) => o.ReachabilityFilters.Add(c.DefinedEnum<SymbolicReachability>(a, "must be NotChecked, Unknown, Reachable, or Unreachable.")), "--reachability")
        .Add(static (o, _, _) => o.WithProofs = true, "--with-proofs")
        .Add(static (o, c, a) => o.ProofOutcomes.Add(c.DefinedEnum<SymbolicTruthValue>(a, "must be Unknown, ProvenTrue, ProvenFalse, or Unreachable.")), "--proof-outcome")
        .Add(static (o, c, a) => o.ProofConditions.Add(c.RequiredValue(a)), "--proof-condition")
        .Add(static (o, c, a) => o.ProofConditionContains.Add(c.RequiredValue(a)), "--proof-condition-contains")
        .Add(static (o, _, _) => o.Json = true, SymbolicCliOutputPolicy.Json)
        .Add(static (o, _, _) => o.FailOnAnalysisTruncation = true, "--fail-on-analysis-truncation")
        .Add(static (o, c, a) => o.AddThreshold(c.RequiredValue(a), a), "--fail-on-threshold")
        .Add(static (o, _, _) => o.CheckReachability = true, "--check-reachability")
        .Add(static (o, c, a) => o.ImpliedConditions.Add(c.RequiredValue(a)), "--implies")
        .Add(static (o, _, _) => o.RuntimeHazards = true, "--runtime-hazards")
        .Add(static (o, _, _) => o.Complexity = true, "--complexity")
        .Add(static (o, _, _) => o.Capabilities = true, "--capabilities")
        .Add(static (o, _, _) => o.FailOnHazard = true, "--fail-on-hazard")
        .Add(static (o, _, _) => o.FailOnUnprovenImplies = true, "--fail-on-unproven-implies")
        .Add(static (o, c, a) => o.AllowedCapabilities.Add(c.DefinedEnum<SymbolicCapability>(a, "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicCapability>()) + ".")), "--allowed-capability")
        .Add(static (o, _, _) => o.FailOnCapabilityViolation = true, "--fail-on-capability-violation")
        .Add(static (o, _, _) => o.FailOnCapabilityUnknown = true, "--fail-on-capability-unknown")
        .Add(static (o, c, a) => o.MaximumComplexity = c.DefinedEnum<SharpProof.Attributes.ComplexityKind>(a, "must be one of: " + string.Join(", ", Enum.GetNames<SharpProof.Attributes.ComplexityKind>()) + "."), "--fail-on-complexity-exceeded")
        .Add(static (o, _, _) => o.FailOnComplexityUnknown = true, "--fail-on-complexity-unknown")
        .Add(static (o, c, a) => o.MaximumConservativeUnknowns = c.Int32(a, 0), "--max-conservative-unknowns")
        .Add(static (o, c, a) => o.HazardKinds.Add(c.DefinedEnum<SymbolicRuntimeHazardKind>(a, "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardKind>()) + ".")), "--hazard-kind")
        .Add(static (o, c, a) => o.HazardStatuses.Add(c.DefinedEnum<SymbolicRuntimeHazardStatus>(a, "must be one of: " + string.Join(", ", Enum.GetNames<SymbolicRuntimeHazardStatus>()) + ".")), "--hazard-status")
        .Add(static (o, c, a) => o.HazardExceptionTypes.Add(c.RequiredValue(a)), "--hazard-exception-type", "--exception-type")
        .Add(static (o, c, a) => o.HazardCategories.Add(c.RequiredValue(a)), "--hazard-category")
        .Add(static (o, _, _) => o.IncludeUnprovenHazards = true, "--include-unproven-hazards")
        .Add(static (o, c, a) => { o.SmtMode = ReadSmtMode(c, a); o.SmtModeSpecified = true; }, "--smt-mode")
        .Add(static (o, c, a) => o.SmtTimeoutMs = c.Int32(a, 1), "--smt-timeout-ms")
        .Add(static (o, c, a) => o.SmtMethodBudgetMs = c.Int32(a, 1), "--smt-method-budget-ms")
        .Add(static (o, c, a) => o.SmtMaxPathConditions = c.Int32(a, 1), "--smt-max-path-conditions")
        .Add(static (o, c, a) => o.SmtMaxExpressionNodes = c.Int32(a, 1), "--smt-max-expression-nodes")
        .Add(static (o, c, a) => { o.SmtTransientRetryCount = c.Int32(a, 0); o.SmtTransientRetryCountSpecified = true; }, "--smt-transient-retries")
        .Add(static (o, _, _) => { o.SmtRecycleContextOnTransientFailure = false; o.SmtRecycleContextOnTransientFailureSpecified = true; }, "--smt-keep-context-on-transient-failure")
        .Add(static (o, _, _) => { o.SmtDisposeContextOnExit = true; o.SmtDisposeContextOnExitSpecified = true; }, "--smt-dispose-context-on-exit")
        .Add(static (o, c, a) => o.AddAnalysisLimitOverride(c.RequiredValue(a), a), "--analysis-limit");

    public static SymbolicCliOptions Parse(string[] args)
    {
        var options = new SymbolicCliOptions();
        OptionHandlers.Parse(args, options);

        if (!options.ShowHelp)
        {
            NormalizeStringList(options.InvariantTargets);
            NormalizeStringList(options.PreprocessorSymbols);
            NormalizeStringList(options.NodeKinds);
            NormalizeStringList(options.MethodNames);
            NormalizeStringList(options.MethodNameContains);
            NormalizeStringList(options.ConditionTargets);
            NormalizeStringList(options.Conditions);
            NormalizeStringList(options.ConditionContains);
            NormalizeStringList(options.ProofConditions);
            NormalizeStringList(options.ProofConditionContains);
            _ = options.CreateCompilationProfile();

            Reject((options.Sarif ? 1 : 0) +
                (options.Markdown ? 1 : 0) +
                (options.Json ? 1 : 0) > 1,
                "--json, --sarif, and --markdown are mutually exclusive.");
            Reject((options.Sarif || options.Markdown) && !options.Explain,
                "--sarif and --markdown require explain.");
            Reject(options.ReportLimitSpecified && !options.Explain,
                "--report-max-diagnostics, --report-max-hazards, and --report-max-items require explain.");
            Reject(options.Json && options.HasInvariantTargetFilter && !options.Explain,
                "--invariant-target cannot be combined with --json; use text output.");
            Reject(options.FailOnUnprovenImplies && options.ImpliedConditions.Count == 0,
                "--fail-on-unproven-implies requires at least one --implies condition.");
            Reject(!options.Capabilities &&
                (options.FailOnCapabilityViolation ||
                 options.FailOnCapabilityUnknown ||
                 options.AllowedCapabilities.Count != 0),
                "--allowed-capability, --fail-on-capability-violation, and --fail-on-capability-unknown require --capabilities.");
            Reject(options.AllowedCapabilities.Count != 0 && !options.FailOnCapabilityViolation,
                "--allowed-capability requires --fail-on-capability-violation.");
            Reject(!options.Complexity &&
                   (options.MaximumComplexity.HasValue || options.FailOnComplexityUnknown),
                "--fail-on-complexity-exceeded and --fail-on-complexity-unknown require --complexity.");
            Reject(options.MaximumConservativeUnknowns.HasValue &&
                   (options.RuntimeHazards || options.Complexity || options.Capabilities),
                "--max-conservative-unknowns is supported only for invariant query results.");
            Reject(!options.RuntimeHazards &&
                (options.IncludeUnprovenHazards ||
                 options.FailOnHazard ||
                 options.HazardKinds.Count != 0 ||
                 options.HazardStatuses.Count != 0 ||
                 options.HazardExceptionTypes.Count != 0 ||
                 options.HazardCategories.Count != 0),
                "--fail-on-hazard, --hazard-kind, --hazard-status, --hazard-exception-type, --hazard-category, and --include-unproven-hazards require --runtime-hazards.");
            Reject(options.HazardStatuses.Any(static status => status != SymbolicRuntimeHazardStatus.Proven) &&
                   !options.IncludeUnprovenHazards,
                "--hazard-status values other than Proven require --include-unproven-hazards.");

            var sourceCount = (options.FilePath != null ? 1 : 0) +
                              (options.ReadSourceFromStdin ? 1 : 0) +
                              (options.InlineSourceText != null ? 1 : 0);
            Reject(sourceCount == 0, "Specify one source input: --file, --stdin, or --source-text.");
            Reject(sourceCount > 1, "--file, --stdin, and --source-text are mutually exclusive.");
            Reject(options.ProjectPath != null && options.SolutionPath != null,
                "--project cannot be combined with --solution.");
            Reject(options.IsProjectAware && options.FilePath == null,
                "--project and --solution require --file.");

            if (!options.IsProjectAware && options.FilePath != null && !File.Exists(CliHost.GetFullPath(options.FilePath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.SourceNotFound,
                    SharpProofErrorCategory.Input,
                    "--file does not exist: " + options.FilePath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.FilePath);

            Reject(options.SourceFileName != null && !options.HasInlineSource,
                "--source-file-name requires --stdin or --source-text.");
            Reject(string.IsNullOrWhiteSpace(options.SourceFileName) && options.SourceFileName != null,
                "--source-file-name requires a non-empty path.");
            Reject(options.SourceMapUri != null && !options.HasInlineSource,
                "--source-map-uri requires --stdin or --source-text.");
            Reject((options.SourceMapOriginalLineSpecified || options.SourceMapOriginalColumnSpecified) &&
                   options.SourceMapUri == null,
                "--source-map-original-line and --source-map-original-column require --source-map-uri.");
            Reject(string.IsNullOrWhiteSpace(options.SourceMapUri) && options.SourceMapUri != null,
                "--source-map-uri requires a non-empty URI.");

            if (options.ProjectPath != null && !File.Exists(CliHost.GetFullPath(options.ProjectPath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.ProjectLoadFailed,
                    SharpProofErrorCategory.Project,
                    "--project does not exist: " + options.ProjectPath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.ProjectPath);

            if (options.SolutionPath != null && !File.Exists(CliHost.GetFullPath(options.SolutionPath)))
                throw SymbolicCliErrorWriter.CreateException(
                    SymbolicErrorCodes.ProjectLoadFailed,
                    SharpProofErrorCategory.Project,
                    "--solution does not exist: " + options.SolutionPath,
                    SymbolicErrorExitCodes.MissingInput,
                    "path",
                    options.SolutionPath);

            Reject(!options.IsProjectAware && options.ProjectName != null,
                "--project-name requires --project or --solution.");
            Reject(!options.IsProjectAware && options.MSBuildProperties.Count != 0,
                "--configuration, --framework, and --msbuild-property require --project or --solution.");
            Reject(options.IsProjectAware && options.StandaloneCompilationOptionsSpecified,
                "Standalone compilation options cannot be combined with --project or --solution; configure the project instead.");
            Reject(options.Position.HasValue && options.Line != 0,
                "--position cannot be combined with --line.");
            Reject(options.Position.HasValue && options.IsAnySpanQuery,
                "--position cannot be combined with span query options.");
            Reject(options.IsAnySpanQuery && options.Line != 0,
                "Span query options cannot be combined with --line.");
            Reject(options.IsAnySpanQuery && options.LineInvariants,
                "Span query options cannot be combined with --line-invariants.");
            Reject(options.IsAnySpanQuery && options.Column != 1,
                "Span query options cannot be combined with --column.");
            Reject(options.IsSpanQuery && (!options.SpanStart.HasValue || !options.SpanEnd.HasValue),
                "--span-start and --span-end must be provided together.");
            Reject(options.IsLineColumnSpanQuery &&
                (!options.SpanStartLine.HasValue ||
                 !options.SpanStartColumn.HasValue ||
                 !options.SpanEndLine.HasValue ||
                 !options.SpanEndColumn.HasValue),
                "--span-start-line, --span-start-column, --span-end-line, and --span-end-column must be provided together.");
            Reject(options.IsSpanQuery && options.IsLineColumnSpanQuery,
                "Absolute span options cannot be combined with line/column span options.");
            Reject(options.SpanEnd.HasValue &&
                options.SpanStart.HasValue &&
                options.SpanEnd.Value < options.SpanStart.Value,
                "--span-end cannot be less than --span-start.");
            Reject(options.SpanStartLine.HasValue &&
                options.SpanEndLine.HasValue &&
                (options.SpanEndLine.Value < options.SpanStartLine.Value ||
                 (options.SpanEndLine.Value == options.SpanStartLine.Value &&
                  options.SpanEndColumn!.Value < options.SpanStartColumn!.Value)),
                "Line/column span end cannot be before span start.");

            Reject(options.AllLines &&
                (options.Position.HasValue || options.IsAnySpanQuery || options.Line != 0 || options.Column != 1 ||
                 options.LineInvariants),
                "--all-lines cannot be combined with --line, --column, --position, span query options, or --line-invariants.");
            Reject(options.Position.HasValue && options.LineInvariants,
                "--line-invariants cannot be combined with --position.");
            Reject(options.RuntimeHazards && options.Position.HasValue,
                "--runtime-hazards supports --line, --span-start/--span-end, or --all-lines, not --position.");
            Reject(options.RuntimeHazards && options.HasInvariantTargetFilter,
                "--invariant-target cannot be combined with --runtime-hazards.");
            Reject(options.RuntimeHazards && (options.LineInvariants || options.LineExpressions ||
                   options.PostLineInvariants || options.Column != 1 || options.IsLineColumnSpanQuery),
                "--runtime-hazards cannot be combined with --line-invariants, --line-expressions, --post-line-invariants, --column, or line/column span options.");
            Reject(options.RuntimeHazards && (options.ImpliedConditions.Count != 0 ||
                   options.CheckReachability || options.HasResultFilter),
                "--runtime-hazards cannot be combined with invariant proof, reachability, or program-point filters.");
            Reject(options.RuntimeHazards && options.Complexity,
                "--runtime-hazards cannot be combined with --complexity.");
            Reject(options.RuntimeHazards && options.Capabilities,
                "--runtime-hazards cannot be combined with --capabilities.");

            if (options.Complexity) options.ValidateFocusedAnalysisCompatibility("--complexity");

            Reject(options.LineExpressions && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery,
                "--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines.");
            Reject(options.PostLineInvariants && !options.LineInvariants && !options.AllLines && !options.IsAnySpanQuery,
                "--post-line-invariants requires --line-invariants, --span-start/--span-end, or --all-lines.");
            Reject(options.FilterLineStart.HasValue &&
                options.FilterLineEnd.HasValue &&
                options.FilterLineStart.Value > options.FilterLineEnd.Value,
                "--line-start cannot be greater than --line-end.");
            Reject(!options.AllLines && !options.Position.HasValue && !options.IsAnySpanQuery && options.Line == 0,
                "--line, --position, --span-start/--span-end, or --all-lines is required.");

            if (options.Explain)
            {
                options.CheckReachability = true;
                Reject(options.RuntimeHazards || options.Complexity || options.Capabilities,
                    "explain cannot be combined with --runtime-hazards, --complexity, or --capabilities.");
                Reject(options.AllLines || options.IsAnySpanQuery || options.LineInvariants,
                    "explain supports --line, --line with --column, or --position only.");
                Reject(options.HasExitGates,
                    "CI exit gates require a focused query mode and cannot be combined with explain.");
            }

            Reject(options.Complexity && options.Line == 0 && !options.Position.HasValue,
                "--complexity requires --line or --position.");
            Reject(options.Complexity && options.Capabilities,
                "--complexity cannot be combined with --capabilities.");

            if (options.Capabilities) options.ValidateFocusedAnalysisCompatibility("--capabilities");

            Reject(options.Capabilities && options.Line == 0 && !options.Position.HasValue,
                "--capabilities requires --line or --position.");
            Reject(options.HasResultFilter && !options.AllLines && !options.LineInvariants && !options.IsAnySpanQuery,
                "Result filters require --line-invariants, --span-start/--span-end, or --all-lines.");

            options.ValidateThresholds();

            foreach (var referencePath in options.ReferencePaths)
                if (!File.Exists(CliHost.GetFullPath(referencePath)))
                    throw SymbolicCliErrorWriter.CreateException(
                        SymbolicErrorCodes.ReferenceNotFound,
                        SharpProofErrorCategory.Input,
                        "--reference does not exist: " + referencePath,
                        SymbolicErrorExitCodes.MissingInput,
                        "path",
                        referencePath);
        }

        return options;
    }

    private void ValidateFocusedAnalysisCompatibility(string optionName)
    {
        Reject(HasInvariantTargetFilter, $"--invariant-target cannot be combined with {optionName}.");
        Reject(AllLines || IsAnySpanQuery || LineInvariants,
            $"{optionName} supports --line, --line with --column, or --position only.");
        Reject(LineExpressions || PostLineInvariants || HasResultFilter,
            $"{optionName} cannot be combined with invariant program-point filters.");
        Reject(ImpliedConditions.Count != 0 || CheckReachability,
            $"{optionName} cannot be combined with implied-condition proofs or reachability checks.");
    }

    private static void Reject(bool invalid, string message)
    {
        if (invalid) throw new ArgumentException(message);
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

    public void ApplyProjectConfiguration(SymbolicProjectQueryContext? context)
    {
        ProjectSmtOptions = context?.Configuration.SmtOptions;
        ProjectAnalysisLimits = context?.Configuration.AnalysisLimits;
    }

    public SymbolicQueryResult FilterResult(SymbolicQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return HasResultFilter ? result.Filter(MatchesResult) : result;
    }

    private bool MatchesResult(SymbolicProgramPointResult result)
    {
        if (WithFacts && result.Facts.Count == 0 ||
            WithConditions && result.PathConditionCount == 0 ||
            WithProofs && result.ConditionProofs.Count == 0 ||
            NodeKinds.Count != 0 && !Contains(NodeKinds, result.NodeKind, StringComparison.OrdinalIgnoreCase) ||
            ProgramPointKinds.Count != 0 &&
            !Contains(ProgramPointKinds, result.ProgramPointKind, StringComparison.OrdinalIgnoreCase) ||
            FilterLines.Count != 0 && !FilterLines.Contains(result.Line) ||
            FilterLineStart.HasValue && result.Line < FilterLineStart.Value ||
            FilterLineEnd.HasValue && result.Line > FilterLineEnd.Value ||
            ReachabilityFilters.Count != 0 && !ReachabilityFilters.Contains(result.Reachability) ||
            MethodNames.Count != 0 && !Contains(MethodNames, result.MethodName, StringComparison.OrdinalIgnoreCase) ||
            MethodNameContains.Count != 0 &&
            !ContainsFragment(MethodNameContains, result.MethodName, StringComparison.OrdinalIgnoreCase) ||
            ConditionTargets.Count != 0 && !result.Invariant.Conditions.Any(condition =>
                Contains(ConditionTargets, condition.Target, StringComparison.OrdinalIgnoreCase)) ||
            Conditions.Count != 0 && !result.Invariant.Conditions.Any(condition =>
                Contains(Conditions, condition.Text, StringComparison.Ordinal)) ||
            ConditionContains.Count != 0 && !result.Invariant.Conditions.Any(condition =>
                ContainsFragment(ConditionContains, condition.Text, StringComparison.OrdinalIgnoreCase)) ||
            ProofOutcomes.Count != 0 &&
            !result.ConditionProofs.Any(proof => ProofOutcomes.Contains(proof.TruthValue)) ||
            ProofConditions.Count != 0 && !result.ConditionProofs.Any(proof =>
                Contains(ProofConditions, proof.Condition, StringComparison.Ordinal)) ||
            ProofConditionContains.Count != 0 && !result.ConditionProofs.Any(proof =>
                ContainsFragment(ProofConditionContains, proof.Condition, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;

        static bool Contains(IEnumerable<string> values, string? candidate, StringComparison comparison) =>
            candidate != null && values.Any(value => string.Equals(value, candidate, comparison));

        static bool ContainsFragment(IEnumerable<string> values, string? candidate, StringComparison comparison) =>
            candidate != null && values.Any(value => candidate.IndexOf(value, comparison) >= 0);
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

    public SymbolicQueryOptions CreateQueryOptions(SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicQueryOptions(
                CreateReferences(),
                smtAnalysis,
                ImpliedConditions,
                LineExpressions,
                PostLineInvariants)
            .WithAnalysisLimits(CreateAnalysisLimits());
    }

    public SharpProofAnalysisBudget CreateAnalysisLimits()
    {
        return SharpProofAnalysisBudget.FromNamedValues(
            ProjectAnalysisLimits ?? SharpProofAnalysisBudget.Default,
            GetAnalysisLimit);
    }

    public SharpProofTarget CreateQueryTarget()
    {
        if (AllLines) return new SharpProofTarget(SharpProofTargetKind.AllLines);

        if (LineInvariants)
            return HasColumn
                ? new SharpProofTarget(SharpProofTargetKind.Point, Line: Line, Column: Column)
                : new SharpProofTarget(SharpProofTargetKind.Line, Line: Line);

        if (IsAnySpanQuery)
            return IsLineColumnSpanQuery
                ? new SharpProofTarget(
                    SharpProofTargetKind.LineSpan,
                    StartLine: SpanStartLine!.Value,
                    StartColumn: SpanStartColumn!.Value,
                    EndLine: SpanEndLine!.Value,
                    EndColumn: SpanEndColumn!.Value)
                : new SharpProofTarget(
                    SharpProofTargetKind.Span,
                    SpanStart: SpanStart!.Value,
                    SpanEnd: SpanEnd!.Value);

        return Position.HasValue
            ? new SharpProofTarget(SharpProofTargetKind.Position, Position: Position.Value)
            : new SharpProofTarget(SharpProofTargetKind.Point, Line: Line, Column: Column);
    }

    public SharpProofTarget CreateRuntimeHazardTarget()
    {
        if (AllLines) return new SharpProofTarget(SharpProofTargetKind.AllLines);

        return IsSpanQuery
            ? new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: SpanStart!.Value, SpanEnd: SpanEnd!.Value)
            : new SharpProofTarget(SharpProofTargetKind.Line, Line: Line);
    }

    public SharpProofTarget CreateComplexityTarget()
    {
        return Position.HasValue
            ? new SharpProofTarget(SharpProofTargetKind.Position, Position: Position.Value)
            : HasColumn
                ? new SharpProofTarget(SharpProofTargetKind.Point, Line: Line, Column: Column)
                : new SharpProofTarget(SharpProofTargetKind.Line, Line: Line);
    }

    public SharpProofTarget CreateCapabilityTarget()
    {
        return Position.HasValue
            ? new SharpProofTarget(SharpProofTargetKind.Position, Position: Position.Value)
            : HasColumn
                ? new SharpProofTarget(SharpProofTargetKind.Point, Line: Line, Column: Column)
                : new SharpProofTarget(SharpProofTargetKind.Line, Line: Line);
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

    private void ValidateThresholds()
    {
        if (Thresholds.Count == 0) return;

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

        var unsupported = Thresholds.Keys
            .Where(metric => !allowedMetrics.Contains(metric, StringComparer.Ordinal))
            .OrderBy(static metric => metric, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length == 0) return;

        throw new ArgumentException(
            "--fail-on-threshold metric(s) " +
            string.Join(", ", unsupported) +
            " are not supported for this query mode. Supported metrics: " +
            string.Join(", ", allowedMetrics) + ".");
    }

    public IEnumerable<MetadataReference>? CreateReferences()
    {
        if (ReferencePaths.Count == 0) return null;

        return ReferencePaths.Select(static path => MetadataReference.CreateFromFile(CliHost.GetFullPath(path)));
    }

    private void AddAnalysisLimitOverride(string value, string optionName) =>
        AddNamedInteger(
            value, optionName, AnalysisLimitOverrides, IsAnalysisLimitName,
            "<name>=<positive-integer>", "limit name", 1);

    private void AddThreshold(string value, string optionName) =>
        AddNamedInteger(
            value, optionName, Thresholds, IsThresholdName,
            "<metric>=<non-negative-integer>", "metric", 0);

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

    private int GetAnalysisLimit(string name, int fallback) =>
        AnalysisLimitOverrides.TryGetValue(name, out var value) ? value : fallback;

    private static bool IsAnalysisLimitName(string name) => SharpProofAnalysisBudget.IsNamedLimit(name);

    private static bool IsThresholdName(string name)
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

    private static LanguageVersion ReadLanguageVersion(ToolArgumentReader reader, string optionName)
    {
        var value = reader.RequiredValue(optionName).Trim();
        if (LanguageVersionFacts.TryParse(value, out var languageVersion)) return languageVersion;

        throw new ArgumentException(optionName + " requires a recognized C# language version.");
    }

    private static NullableContextOptions ReadNullableContext(ToolArgumentReader reader, string optionName)
    {
        var value = reader.RequiredValue(optionName).Trim().ToLowerInvariant();
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

    private static SmtAnalysisMode ReadSmtMode(ToolArgumentReader reader, string optionName)
    {
        var value = reader.RequiredValue(optionName).Trim().ToLowerInvariant();
        return value switch
        {
            "disabled" => SmtAnalysisMode.Off,
            "bounded" => SmtAnalysisMode.Bounded,
            "deep" => SmtAnalysisMode.Deep,
            _ => throw new ArgumentException(optionName + " must be disabled, bounded, or deep.")
        };
    }

    private static string ReadProgramPointKind(ToolArgumentReader reader, string optionName)
    {
        var value = reader.RequiredValue(optionName).Trim();
        if (SymbolicProgramPointKinds.TryNormalizeKnownKind(value, out var normalizedKind)) return normalizedKind;

        throw new ArgumentException(optionName + " must be Statement, Expression, or Other.");
    }
}
