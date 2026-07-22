namespace SharpProof.Symbolic.Ir;

internal static class AnalyzerUtilitiesControlFlowAnalysis {
    internal static AnalyzerControlFlowResult Run(
        ControlFlowGraph graph,
        EffectFlowState initialState,
        IControlFlowDomain<EffectFlowState> domain,
        Compilation compilation,
        ISymbol owningSymbol,
        CancellationToken cancellationToken) {
        var options = new AnalyzerOptions([]);
        var interprocedural = InterproceduralAnalysisConfiguration.Create(
            options,
            [],
            graph,
            compilation,
            InterproceduralAnalysisKind.None,
            0,
            0);
        var pointsTo = graph.Blocks.Any(static block => block.BranchValue is IIsNullOperation)
            ? TryGetPointsToResult(graph, owningSymbol, options, compilation, interprocedural)
            : null;
        domain.SetControlFlowGraph(graph, pointsTo);
        var valueDomain = new EffectAbstractValueDomain();
        var context = new EffectAnalysisContext(
            valueDomain,
            graph,
            owningSymbol,
            options,
            interprocedural,
            Analyzer.Utilities.WellKnownTypeProvider.GetOrCreate(compilation),
            pointsTo,
            initialState,
            domain,
            cancellationToken);
        var visitor = new EffectDataFlowOperationVisitor(context);
        var analysis = new EffectDataFlowAnalysis(new EffectAnalysisDomain(domain), visitor);
        var result = analysis.Run(context) ?? throw new InvalidOperationException("AnalyzerUtilities data-flow analysis failed.");
        return new(result.ExitBlockOutput.State, context.Truncated);
    }

    private static PointsToAnalysisResult? TryGetPointsToResult(
        ControlFlowGraph graph,
        ISymbol owningSymbol,
        AnalyzerOptions options,
        Compilation compilation,
        InterproceduralAnalysisConfiguration interprocedural) {
        try {
            return PointsToAnalysis.TryGetOrComputeResult(
                graph,
                owningSymbol,
                options,
                Analyzer.Utilities.WellKnownTypeProvider.GetOrCreate(compilation),
                PointsToAnalysisKind.Complete,
                interprocedural,
                interproceduralAnalysisPredicate: null,
                pessimisticAnalysis: true,
                performCopyAnalysis: true,
                exceptionPathsAnalysis: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException) {
            return null;
        }
    }

    internal sealed record AnalyzerControlFlowResult(EffectFlowState ExitState, bool Truncated);

    private enum EffectAbstractValue { Unknown }

    private sealed class EffectAbstractValueDomain : AbstractValueDomain<EffectAbstractValue> {
        public override EffectAbstractValue UnknownOrMayBeValue => EffectAbstractValue.Unknown;
        public override EffectAbstractValue Bottom => EffectAbstractValue.Unknown;
        public override EffectAbstractValue Merge(EffectAbstractValue value1, EffectAbstractValue value2) =>
            EffectAbstractValue.Unknown;
        public override int Compare(EffectAbstractValue oldValue, EffectAbstractValue newValue, bool assertMonotonicity) => 0;
    }

    private sealed class EffectAnalysisData(EffectFlowState state) : AbstractAnalysisData {
        internal EffectFlowState State { get; } = state;
    }

    private sealed class EffectAnalysisDomain(IControlFlowDomain<EffectFlowState> domain) :
        AbstractAnalysisDomain<EffectAnalysisData> {
        public override EffectAnalysisData Clone(EffectAnalysisData value) => new(value.State);
        public override EffectAnalysisData Merge(EffectAnalysisData value1, EffectAnalysisData value2) =>
            new(domain.Merge(value1.State, value2.State));
        public override int Compare(EffectAnalysisData oldValue, EffectAnalysisData newValue) {
            if (domain.Equivalent(oldValue.State, newValue.State)) return 0;
            var merged = domain.Merge(oldValue.State, newValue.State);
            if (domain.Equivalent(merged, newValue.State)) return -1;
            return domain.Equivalent(merged, oldValue.State) ? 1 : -1;
        }
        public override bool Equals(EffectAnalysisData value1, EffectAnalysisData value2) =>
            domain.Equivalent(value1.State, value2.State);
    }

    private sealed class EffectBlockAnalysisResult(BasicBlock block, EffectFlowState state) :
        AbstractBlockAnalysisResult(block) {
        internal EffectFlowState State { get; } = state;
    }

    private sealed class EffectAnalysisContext : AbstractDataFlowAnalysisContext<
        EffectAnalysisData,
        EffectAnalysisContext,
        DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue>,
        EffectAbstractValue> {
        internal EffectAnalysisContext(
            EffectAbstractValueDomain valueDomain,
            ControlFlowGraph graph,
            ISymbol owningSymbol,
            AnalyzerOptions options,
            InterproceduralAnalysisConfiguration interprocedural,
            Analyzer.Utilities.WellKnownTypeProvider wellKnownTypeProvider,
            PointsToAnalysisResult? pointsTo,
            EffectFlowState initialState,
            IControlFlowDomain<EffectFlowState> domain,
            CancellationToken cancellationToken) : base(
                valueDomain,
                wellKnownTypeProvider,
                graph,
                owningSymbol,
                options,
                interprocedural,
                pessimisticAnalysis: true,
                predicateAnalysis: false,
                exceptionPathsAnalysis: true,
                copyAnalysisResult: null,
                pointsToAnalysisResult: pointsTo,
                valueContentAnalysisResult: null,
                tryGetOrComputeAnalysisResult: static _ => null,
                parentControlFlowGraph: null,
                interproceduralAnalysisData: null,
                interproceduralAnalysisPredicate: null) {
            InitialState = initialState;
            Domain = domain;
            CancellationToken = cancellationToken;
        }
        internal EffectFlowState InitialState { get; }
        internal IControlFlowDomain<EffectFlowState> Domain { get; }
        internal CancellationToken CancellationToken { get; }
        internal bool Truncated { get; private set; }
        private int Transfers { get; set; }
        private const int MaxTransfers = 4096;
        internal EffectFlowState Transfer(EffectFlowState state, IOperation operation) {
            if (Transfers >= MaxTransfers) {
                Truncated = true;
                return state;
            }
            Transfers++;
            if (Transfers >= MaxTransfers) Truncated = true;
            return Domain.Transfer(state, operation);
        }
        public override EffectAnalysisContext ForkForInterproceduralAnalysis(
            IMethodSymbol invokedMethod,
            ControlFlowGraph invokedCfg,
            PointsToAnalysisResult? pointsToAnalysisResult,
            DataFlowAnalysisResult<CopyBlockAnalysisResult, CopyAbstractValue>? copyAnalysisResult,
            DataFlowAnalysisResult<ValueContentBlockAnalysisResult, ValueContentAbstractValue>? valueContentAnalysisResult,
            InterproceduralAnalysisData<EffectAnalysisData, EffectAnalysisContext, EffectAbstractValue>?
                interproceduralAnalysisData) => new(
                    // AnalyzerUtilities requests a context for every unvisited local function on method exit.
                    // The result callback is intentionally null because SharpProof analyzes callable bodies itself.
                    (EffectAbstractValueDomain)ValueDomain,
                    invokedCfg,
                    invokedMethod,
                    AnalyzerOptions,
                    InterproceduralAnalysisConfiguration,
                    WellKnownTypeProvider,
                    pointsToAnalysisResult,
                    InitialState,
                    Domain,
                    CancellationToken);
        protected override void ComputeHashCodePartsSpecific(ref Analyzer.Utilities.RoslynHashCode hashCode) { }
        protected override bool ComputeEqualsByHashCodeParts(
            AbstractDataFlowAnalysisContext<EffectAnalysisData, EffectAnalysisContext,
                DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue>, EffectAbstractValue> other) =>
            ReferenceEquals(this, other);
    }

    private sealed class EffectDataFlowOperationVisitor : DataFlowOperationVisitor<
        EffectAnalysisData,
        EffectAnalysisContext,
        DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue>,
        EffectAbstractValue> {
        private readonly EffectAnalysisContext context;
        private readonly Dictionary<(IOperation Operation, string State), EffectFlowState> flowedOperations = [];
        private readonly Dictionary<(int Ordinal, string State), EffectFlowState> completedBlocks = [];

        internal EffectDataFlowOperationVisitor(EffectAnalysisContext context) : base(context) =>
            this.context = context;

        public override EffectAnalysisData Flow(IOperation statement, BasicBlock block, EffectAnalysisData input) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var cacheKey = (statement, input.State.Key);
            if (!flowedOperations.TryGetValue(cacheKey, out var state)) {
                state = context.Transfer(input.State, statement);
                flowedOperations.Add(cacheKey, state);
            }
            return new(state);
        }
        public override (EffectAnalysisData output, bool isFeasibleBranch) FlowBranch(
            BasicBlock source,
            BranchWithInfo branch,
            EffectAnalysisData input) {
            context.CancellationToken.ThrowIfCancellationRequested();
            // FlowBranch is called once per edge, but SharpProof evaluates a block branch value once.
            var cacheKey = (source.Ordinal, input.State.Key);
            if (!completedBlocks.TryGetValue(cacheKey, out var state)) {
                state = input.State;
                if (branch.BranchValue != null) state = context.Transfer(state, branch.BranchValue);
                state = context.Domain.CompleteBlock(state, source);
                completedBlocks.Add(cacheKey, state);
            }
            var conditionalSuccessor = source.ConditionKind != ControlFlowConditionKind.None &&
                                       branch.ControlFlowConditionKind == source.ConditionKind;
            state = context.Domain.Refine(state, branch.BranchValue, source.ConditionKind, conditionalSuccessor);
            return (new(state), !state.IsUnreachable);
        }
        protected override EffectAbstractValue GetAbstractDefaultValue(ITypeSymbol? type) => EffectAbstractValue.Unknown;
        protected override bool HasAnyAbstractValue(EffectAnalysisData data) => false;
        protected override void SetValueForParameterOnEntry(
            IParameterSymbol parameter,
            AnalysisEntity analysisEntity,
            ArgumentInfo<EffectAbstractValue>? argumentInfo) { }
        protected override void EscapeValueForParameterOnExit(IParameterSymbol parameter, AnalysisEntity analysisEntity) { }
        protected override void ResetCurrentAnalysisData() { }
        protected override void UpdateValuesForAnalysisData(EffectAnalysisData analysisData) { }
        protected override void StopTrackingDataForParameter(IParameterSymbol parameter, AnalysisEntity analysisEntity) { }
        protected override void SetAbstractValueForArrayElementInitializer(
            IArrayCreationOperation arrayCreation,
            ImmutableArray<AbstractIndex> indices,
            ITypeSymbol elementType,
            IOperation initializer,
            EffectAbstractValue value) { }
        protected override void SetAbstractValueForAssignment(
            IOperation target,
            IOperation? assignedValueOperation,
            EffectAbstractValue assignedValue,
            bool mayBeAssignment) { }
        protected override void SetAbstractValueForTupleElementAssignment(
            AnalysisEntity tupleElementEntity,
            IOperation assignedValueOperation,
            EffectAbstractValue assignedValue) { }
        protected override void ResetValueTypeInstanceAnalysisData(AnalysisEntity analysisEntity) { }
        protected override void ResetReferenceTypeInstanceAnalysisData(PointsToAbstractValue pointsToValue) { }
        protected override EffectAnalysisData MergeAnalysisData(EffectAnalysisData value1, EffectAnalysisData value2) =>
            new(context.Domain.Merge(value1.State, value2.State));
        protected override EffectAnalysisData GetClonedAnalysisData(EffectAnalysisData value) => new(value.State);
        public override EffectAnalysisData GetEmptyAnalysisData() => new(context.InitialState);
        protected override EffectAnalysisData GetExitBlockOutputData(
            DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue> result) =>
            new(result.ExitBlockOutput.State);
        protected override bool Equals(EffectAnalysisData value1, EffectAnalysisData value2) =>
            context.Domain.Equivalent(value1.State, value2.State);
        protected override void ApplyMissingCurrentAnalysisDataForUnhandledExceptionData(
            EffectAnalysisData analysisData,
            ThrownExceptionInfo thrownExceptionInfo) { }
    }

    private sealed class EffectDataFlowAnalysis(
        EffectAnalysisDomain domain,
        EffectDataFlowOperationVisitor visitor) : ForwardDataFlowAnalysis<
            EffectAnalysisData,
            EffectAnalysisContext,
            DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue>,
            EffectBlockAnalysisResult,
            EffectAbstractValue>(domain, visitor) {
        internal DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue>? Run(EffectAnalysisContext context) =>
            TryGetOrComputeResultCore(context, cacheResult: false);
        protected override DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue> ToResult(
            EffectAnalysisContext analysisContext,
            DataFlowAnalysisResult<EffectBlockAnalysisResult, EffectAbstractValue> dataFlowAnalysisResult) =>
            dataFlowAnalysisResult;
        protected override EffectBlockAnalysisResult ToBlockResult(BasicBlock basicBlock, EffectAnalysisData blockAnalysisData) =>
            new(basicBlock, blockAnalysisData.State);
    }
}
