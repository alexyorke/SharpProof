namespace SharpProof.Symbolic.Ir;

internal static class AnalyzerUtilitiesControlFlowAnalysis {
    internal static AnalyzerControlFlowResult<TState> Run<TState>(
        ControlFlowGraph graph,
        TState initialState,
        IControlFlowDomain<TState> domain,
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
        var context = new EffectAnalysisContext<TState>(
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
        var visitor = new EffectDataFlowOperationVisitor<TState>(context);
        var analysis = new EffectDataFlowAnalysis<TState>(new EffectAnalysisDomain<TState>(domain), visitor);
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

    internal sealed record AnalyzerControlFlowResult<TState>(TState ExitState, bool Truncated);

    private enum EffectAbstractValue { Unknown }

    private sealed class EffectAbstractValueDomain : AbstractValueDomain<EffectAbstractValue> {
        public override EffectAbstractValue UnknownOrMayBeValue => EffectAbstractValue.Unknown;
        public override EffectAbstractValue Bottom => EffectAbstractValue.Unknown;
        public override EffectAbstractValue Merge(EffectAbstractValue value1, EffectAbstractValue value2) =>
            EffectAbstractValue.Unknown;
        public override int Compare(EffectAbstractValue oldValue, EffectAbstractValue newValue, bool assertMonotonicity) => 0;
    }

    private sealed class EffectAnalysisData<TState>(TState state) : AbstractAnalysisData {
        internal TState State { get; } = state;
    }

    private sealed class EffectAnalysisDomain<TState>(IControlFlowDomain<TState> domain) :
        AbstractAnalysisDomain<EffectAnalysisData<TState>> {
        public override EffectAnalysisData<TState> Clone(EffectAnalysisData<TState> value) => new(value.State);
        public override EffectAnalysisData<TState> Merge(EffectAnalysisData<TState> value1, EffectAnalysisData<TState> value2) =>
            new(domain.Merge(value1.State, value2.State));
        public override int Compare(EffectAnalysisData<TState> oldValue, EffectAnalysisData<TState> newValue) {
            if (domain.Equivalent(oldValue.State, newValue.State)) return 0;
            var merged = domain.Merge(oldValue.State, newValue.State);
            if (domain.Equivalent(merged, newValue.State)) return -1;
            return domain.Equivalent(merged, oldValue.State) ? 1 : -1;
        }
        public override bool Equals(EffectAnalysisData<TState> value1, EffectAnalysisData<TState> value2) =>
            domain.Equivalent(value1.State, value2.State);
    }

    private sealed class EffectBlockAnalysisResult<TState>(BasicBlock block, TState state) :
        AbstractBlockAnalysisResult(block) {
        internal TState State { get; } = state;
    }

    private sealed class EffectAnalysisContext<TState> : AbstractDataFlowAnalysisContext<
        EffectAnalysisData<TState>,
        EffectAnalysisContext<TState>,
        DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue>,
        EffectAbstractValue> {
        internal EffectAnalysisContext(
            EffectAbstractValueDomain valueDomain,
            ControlFlowGraph graph,
            ISymbol owningSymbol,
            AnalyzerOptions options,
            InterproceduralAnalysisConfiguration interprocedural,
            Analyzer.Utilities.WellKnownTypeProvider wellKnownTypeProvider,
            PointsToAnalysisResult? pointsTo,
            TState initialState,
            IControlFlowDomain<TState> domain,
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
        internal TState InitialState { get; }
        internal IControlFlowDomain<TState> Domain { get; }
        internal CancellationToken CancellationToken { get; }
        internal bool Truncated { get; private set; }
        private int Transfers { get; set; }
        private const int MaxTransfers = 4096;
        internal TState Transfer(TState state, IOperation operation) {
            if (Transfers >= MaxTransfers) {
                Truncated = true;
                return state;
            }
            Transfers++;
            if (Transfers >= MaxTransfers) Truncated = true;
            return Domain.Transfer(state, operation);
        }
        // AnalyzerUtilities requests a context for every unvisited local function on method exit.
        // The result callback is intentionally null because SharpProof analyzes callable bodies itself.
        public override EffectAnalysisContext<TState> ForkForInterproceduralAnalysis(
            IMethodSymbol invokedMethod,
            ControlFlowGraph invokedCfg,
            PointsToAnalysisResult? pointsToAnalysisResult,
            DataFlowAnalysisResult<CopyBlockAnalysisResult, CopyAbstractValue>? copyAnalysisResult,
            DataFlowAnalysisResult<ValueContentBlockAnalysisResult, ValueContentAbstractValue>? valueContentAnalysisResult,
            InterproceduralAnalysisData<EffectAnalysisData<TState>, EffectAnalysisContext<TState>, EffectAbstractValue>?
                interproceduralAnalysisData) => new(
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
            AbstractDataFlowAnalysisContext<EffectAnalysisData<TState>, EffectAnalysisContext<TState>,
                DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue>, EffectAbstractValue> other) =>
            ReferenceEquals(this, other);
    }

    private sealed class EffectDataFlowOperationVisitor<TState> : DataFlowOperationVisitor<
        EffectAnalysisData<TState>,
        EffectAnalysisContext<TState>,
        DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue>,
        EffectAbstractValue> {
        private readonly EffectAnalysisContext<TState> context;
        private readonly Dictionary<(IOperation Operation, string State), TState> flowedOperations = [];
        private readonly Dictionary<(int Ordinal, string State), TState> completedBlocks = [];

        internal EffectDataFlowOperationVisitor(EffectAnalysisContext<TState> context) : base(context) =>
            this.context = context;

        public override EffectAnalysisData<TState> Flow(IOperation statement, BasicBlock block, EffectAnalysisData<TState> input) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var cacheKey = (statement, context.Domain.GetKey(input.State));
            if (!flowedOperations.TryGetValue(cacheKey, out var state)) {
                state = context.Transfer(input.State, statement);
                flowedOperations.Add(cacheKey, state);
            }
            return new(state);
        }
        public override (EffectAnalysisData<TState> output, bool isFeasibleBranch) FlowBranch(
            BasicBlock source,
            BranchWithInfo branch,
            EffectAnalysisData<TState> input) {
            context.CancellationToken.ThrowIfCancellationRequested();
            // FlowBranch is called once per edge, but SharpProof evaluates a block branch value once.
            var cacheKey = (source.Ordinal, context.Domain.GetKey(input.State));
            if (!completedBlocks.TryGetValue(cacheKey, out var state)) {
                state = input.State;
                if (branch.BranchValue != null) state = context.Transfer(state, branch.BranchValue);
                state = context.Domain.CompleteBlock(state, source);
                completedBlocks.Add(cacheKey, state);
            }
            var conditionalSuccessor = source.ConditionKind != ControlFlowConditionKind.None &&
                                       branch.ControlFlowConditionKind == source.ConditionKind;
            state = context.Domain.Refine(state, branch.BranchValue, source.ConditionKind, conditionalSuccessor, source);
            return (new(state), !context.Domain.IsUnreachable(state));
        }
        protected override EffectAbstractValue GetAbstractDefaultValue(ITypeSymbol? type) => EffectAbstractValue.Unknown;
        protected override bool HasAnyAbstractValue(EffectAnalysisData<TState> data) => false;
        protected override void SetValueForParameterOnEntry(
            IParameterSymbol parameter,
            AnalysisEntity analysisEntity,
            ArgumentInfo<EffectAbstractValue>? argumentInfo) { }
        protected override void EscapeValueForParameterOnExit(IParameterSymbol parameter, AnalysisEntity analysisEntity) { }
        protected override void ResetCurrentAnalysisData() { }
        protected override void UpdateValuesForAnalysisData(EffectAnalysisData<TState> analysisData) { }
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
        protected override EffectAnalysisData<TState> MergeAnalysisData(EffectAnalysisData<TState> value1, EffectAnalysisData<TState> value2) =>
            new(context.Domain.Merge(value1.State, value2.State));
        protected override EffectAnalysisData<TState> GetClonedAnalysisData(EffectAnalysisData<TState> value) => new(value.State);
        public override EffectAnalysisData<TState> GetEmptyAnalysisData() => new(context.InitialState);
        protected override EffectAnalysisData<TState> GetExitBlockOutputData(
            DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue> result) =>
            new(result.ExitBlockOutput.State);
        protected override bool Equals(EffectAnalysisData<TState> value1, EffectAnalysisData<TState> value2) =>
            context.Domain.Equivalent(value1.State, value2.State);
        protected override void ApplyMissingCurrentAnalysisDataForUnhandledExceptionData(
            EffectAnalysisData<TState> analysisData,
            ThrownExceptionInfo thrownExceptionInfo) { }
    }

    private sealed class EffectDataFlowAnalysis<TState>(
        EffectAnalysisDomain<TState> domain,
        EffectDataFlowOperationVisitor<TState> visitor) : ForwardDataFlowAnalysis<
            EffectAnalysisData<TState>,
            EffectAnalysisContext<TState>,
            DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue>,
            EffectBlockAnalysisResult<TState>,
            EffectAbstractValue>(domain, visitor) {
        internal DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue>? Run(EffectAnalysisContext<TState> context) =>
            TryGetOrComputeResultCore(context, cacheResult: false);
        protected override DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue> ToResult(
            EffectAnalysisContext<TState> analysisContext,
            DataFlowAnalysisResult<EffectBlockAnalysisResult<TState>, EffectAbstractValue> dataFlowAnalysisResult) =>
            dataFlowAnalysisResult;
        protected override EffectBlockAnalysisResult<TState> ToBlockResult(BasicBlock basicBlock, EffectAnalysisData<TState> blockAnalysisData) =>
            new(basicBlock, blockAnalysisData.State);
    }
}
