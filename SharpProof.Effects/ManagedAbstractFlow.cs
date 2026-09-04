using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static SharpProof.Effects.ManagedAbstractValue;

namespace SharpProof.Effects;

/// <summary>
/// Bounded scalar facts computed by the shared deterministic dataflow engine.
/// Unsupported, over-budget, and cyclic bodies return an explicit incomplete result.
/// </summary>
internal sealed class ManagedAbstractFlow
{
    internal const int MaxAnalyzedBlocks = 256;
    internal const int MaxAnalyzedOperations = 4096;

    /// <summary>
    /// Ceiling on expression/statement nesting walked recursively, matching the
    /// verifier's expression-depth budget. Deeply nested trees abstain instead
    /// of exhausting the stack, because StackOverflowException is uncatchable
    /// and would take the compiler host down with it.
    /// </summary>
    private const int MaximumWalkDepth = 256;

    // Instances are shared per compilation across Roslyn's concurrent analysis
    // threads, so the recursion guard cannot live in an instance field.
    [ThreadStatic]
    private static int s_walkDepth;

    private static readonly ConditionalWeakTable<Compilation, ManagedAbstractFlow> Sessions = new();
    private static readonly ConditionalWeakTable<
        Compilation,
        ConcurrentDictionary<(SyntaxTree Tree, int Start, int Length), bool>>
        CompileTimeUnreachableStatementCache = new();
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _contractApi;
    private readonly INamedTypeSymbol? _inRangeAttribute;
    private readonly INamedTypeSymbol? _notNullAttribute;
    private readonly INamedTypeSymbol? _positiveAttribute;
    private readonly TrustedBoundaryPolicy _trustedBoundaries;
    private readonly DefiniteOperationFacts _completionFacts;

    private ManagedAbstractFlow(Compilation compilation)
        : this(compilation, new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation))
    {
    }

    private ManagedAbstractFlow(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _compilation = compilation;
        _apiSpecs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
        var contractApi = ContractApiIdentityResolver.ForCompilation(compilation);
        _contractApi = contractApi.Contract;
        _notNullAttribute = contractApi.ResolveAttribute(ContractApiMetadata.NotNull);
        _positiveAttribute = contractApi.ResolveAttribute(ContractApiMetadata.Positive);
        _inRangeAttribute = contractApi.ResolveAttribute(ContractApiMetadata.InRange);
        _trustedBoundaries =
            TrustedBoundaryPolicy.ForCompilation(compilation);
        _completionFacts = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);
    }

    internal static ManagedAbstractFlow ForCompilation(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        return Sessions.GetValue(compilation, static value => new(value));
    }

    internal static ManagedAbstractFlow Create(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs)
    {
        return new(compilation, apiSpecs);
    }

    internal ManagedFlowState CreateEntryState(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        var state = ManagedFlowState.Empty;
        foreach (var parameter in method.Parameters)
        {
            var value = TopForType(parameter.Type);
            if (parameter.RefKind != RefKind.Out)
            {
                value = ApplyAttributes(value, parameter.GetAttributes());
            }

            state = state.Set(parameter, value);
        }
        return state;
    }

    /// <summary>
    /// Overrides the solver's iteration bound so the non-convergence path can be
    /// exercised. Mirrors
    /// <c>ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting</c>.
    /// </summary>
    internal ManagedFlowAnalysis AnalyzeWithIterationLimitForTesting(
        IMethodSymbol method,
        ControlFlowGraph graph,
        ManagedFlowState? entryState,
        int maxIterations,
        CancellationToken cancellationToken)
    {
        return Analyze(method, graph, entryState, cancellationToken, maxIterations);
    }

    internal ManagedFlowAnalysis Analyze(
        IMethodSymbol method, ControlFlowGraph graph, ManagedFlowState? entryState, CancellationToken cancellationToken,
        int? maxIterationsOverride = null)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        graph = ArgumentNullGuard.NotNull(graph, nameof(graph));

        var budgetReason = CheckBudget(graph, cancellationToken);
        if (budgetReason != EffectAnalysisIncompleteReason.None)
        {
            return ManagedFlowAnalysis.BudgetExceeded(budgetReason);
        }

        if (!IsAcyclic(graph))
        {
            return ManagedFlowAnalysis.Cyclic();
        }

        var result = new ManagedFlowResult(this);

        // CheckBudget bounds the source CFG, but CreateDataflowGraph adds a
        // synthetic block per edge, so the iteration limit has to be taken from
        // the expanded graph rather than from MaxAnalyzedBlocks.
        var dataflowGraph = CreateDataflowGraph(graph, result, cancellationToken);
        try
        {
            _ = ForwardDataflowAnalysis.Analyze(dataflowGraph,
                FlowDomain.Instance, entryState ?? CreateEntryState(method),
                new ForwardDataflowAnalysisOptions(
                    maxIterations: maxIterationsOverride
                        ?? dataflowGraph.Blocks.Length * 4));
        }
        catch (DataflowConvergenceException)
        {
            // Every other resource limit here degrades to an incomplete summary.
            // Reaching the iteration bound must not escape as AD0001.
            return ManagedFlowAnalysis.BudgetExceeded(
                EffectAnalysisIncompleteReason.BlockBudgetExceeded);
        }

        return ManagedFlowAnalysis.Complete(result);
    }

    internal ManagedAbstractValue Evaluate(IOperation operation, ManagedFlowState state)
    {
        return EvaluateCore(
            ArgumentNullGuard.NotNull(operation, nameof(operation)),
            ArgumentNullGuard.NotNull(state, nameof(state)));
    }

    private DataflowGraph<ManagedFlowState> CreateDataflowGraph(
        ControlFlowGraph graph, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        var blocks = ImmutableArray.CreateBuilder<DataflowBlock<ManagedFlowState>>();
        var edges = ImmutableArray.CreateBuilder<DataflowEdge>();
        foreach (var block in graph.Blocks)
        {
            var captured = block;
            blocks.Add(new(block.Ordinal, state => TransferBlock(state, captured, result, cancellationToken)));
        }
        foreach (var block in graph.Blocks)
        {
            foreach (var (branch, expected) in Successors(block))
            {
                var condition = block.BranchValue;
                var edgeBlock = blocks.Count;
                blocks.Add(new(edgeBlock, state => expected.HasValue && condition != null &&
                    !result.HasMutation(condition)
                    ? Assume(state, condition, expected.Value) : state));
                edges.Add(new(block.Ordinal, edgeBlock));
                edges.Add(new(edgeBlock, branch.Destination!.Ordinal));
            }
        }

        return new(blocks, edges);
    }

    private ManagedFlowState TransferBlock(
        ManagedFlowState state, BasicBlock block, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        state = TransferMany(state, block.Operations, result, cancellationToken);
        return state.IsBottom || block.BranchValue == null
            ? state
            : Transfer(state, block.BranchValue, result, cancellationToken);
    }

    private ManagedFlowState Transfer(
        ManagedFlowState state, IOperation operation, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        if (state.IsBottom)
        {
            return state;
        }

        if (!TryEnterWalk())
        {
            // Abandoning the sub-tree means we no longer know what it wrote, so
            // every fact has to be dropped rather than carried forward stale.
            return state.Forget();
        }

        try
        {
            return TransferCore(state, operation, result, cancellationToken);
        }
        finally
        {
            ExitWalk();
        }
    }

    private ManagedFlowState TransferCore(
        ManagedFlowState state, IOperation operation, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (operation)
        {
            case IAnonymousFunctionOperation or ILocalFunctionOperation:
                break;
            case IVariableDeclaratorOperation declarator:
                if (IsUntrackedManagedReference(declarator.Symbol.RefKind))
                {
                    state = state.WithUntrackedAlias();
                }

                if (declarator.Initializer == null)
                {
                    state = state.Set(declarator.Symbol, ManagedAbstractValue.TopForType(declarator.Symbol.Type));
                }
                else
                {
                    var hasMutation = result.HasMutation(
                        declarator.Initializer.Value);
                    state = Transfer(state, declarator.Initializer.Value, result, cancellationToken);
                    state = state.Set(
                        declarator.Symbol,
                        hasMutation
                            ? TopForType(declarator.Symbol.Type)
                            : EvaluateCore(declarator.Initializer.Value, state));
                }
                break;
            case IFlowCaptureOperation capture:
                result.RecordCoalesceAssignmentCapture(capture);
                var captureHasMutation = result.HasMutation(
                    capture.Value);
                state = Transfer(state, capture.Value, result, cancellationToken);
                state = state.Set(
                    capture.Id,
                    captureHasMutation
                        ? TopForType(capture.Value.Type)
                        : EvaluateCore(capture.Value, state));
                break;
            case ISimpleAssignmentOperation assignment:
                state = MarkUntrackedAlias(
                    state,
                    assignment.Target,
                    out var aliasesUntrackedStorage);

                var valueHasMutation = result.HasMutation(
                    assignment.Value);
                state = TransferMany(state, assignment.ChildOperations, result, cancellationToken);
                var assignedValue = valueHasMutation
                    ? TopForType(assignment.Type)
                    : EvaluateCore(assignment.Value, state);
                if (!aliasesUntrackedStorage)
                {
                    state = SetStorage(
                        state,
                        assignment.Target,
                        assignedValue);
                    state = SetStorage(
                        state,
                        result.ResolveCoalesceAssignmentTarget(assignment.Target),
                        assignedValue);
                }
                break;
            case ICompoundAssignmentOperation compound:
                state = MarkUntrackedAlias(
                    state,
                    compound.Target,
                    out var compoundAliasesUntrackedStorage);

                state = TransferMany(state, compound.ChildOperations, result, cancellationToken);
                if (!compoundAliasesUntrackedStorage)
                {
                    state = SetStorage(state, compound.Target, TopForType(compound.Type));
                }
                break;
            case IIncrementOrDecrementOperation increment:
                state = MarkUntrackedAlias(
                    state,
                    increment.Target,
                    out var incrementAliasesUntrackedStorage);

                state = Transfer(state, increment.Target, result, cancellationToken);
                if (!incrementAliasesUntrackedStorage)
                {
                    state = SetStorage(state, increment.Target, Increment(increment, state));
                }
                break;
            case IInvocationOperation invocation:
                state = TransferMany(state, invocation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return IsRequires(invocation) ? Assume(state, invocation.Arguments[0].Value, true)
                    : HavocCall(state, invocation.TargetMethod, invocation.Arguments);
            case IObjectCreationOperation creation:
                state = TransferMany(state, creation.Arguments, result, cancellationToken);
                result.Record(operation, state);
                state = HavocArguments(state, creation.Arguments);
                return creation.Initializer == null ? state
                    : Transfer(state, creation.Initializer, result, cancellationToken);
            case IDynamicInvocationOperation or IFunctionPointerInvocationOperation:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return state.Forget();
            case IReturnOperation or IThrowOperation:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                result.Record(operation, state);
                return ManagedFlowState.Bottom;
            default:
                state = TransferMany(state, operation.ChildOperations, result, cancellationToken);
                break;
        }
        result.Record(operation, state);
        return state;
    }

    private ManagedFlowState TransferMany(
        ManagedFlowState state, IEnumerable<IOperation> operations, ManagedFlowResult result, CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            state = Transfer(state, operation, result, cancellationToken);
            if (state.IsBottom)
            {
                break;
            }
        }
        return state;
    }

    private ManagedAbstractValue Increment(IIncrementOrDecrementOperation operation, ManagedFlowState state)
    {
        return TryIncrement(operation, state, out var updated) &&
               FitsType(updated, operation.Type)
            ? Integer(updated)
            : TopForType(operation.Type);
    }

    private bool TryIncrement(
        IIncrementOrDecrementOperation operation,
        ManagedFlowState state,
        out IntervalValue interval)
    {
        interval = default;
        var @operator = operation.Kind == OperationKind.Increment
            ? BinaryOperatorKind.Add
            : BinaryOperatorKind.Subtract;
        return EvaluateCore(operation.Target, state).TryGetInteger(out var target) &&
            TryArithmetic(
                @operator,
                target,
                IntervalValue.Constant(1),
                out interval);
    }

    internal ManagedFlowState Assume(ManagedFlowState state, IOperation condition, bool expected)
    {
        return Assume(state, condition, expected, conditionValue: null);
    }

    private ManagedFlowState Assume(
        ManagedFlowState state,
        IOperation condition,
        bool expected,
        ManagedAbstractValue? conditionValue)
    {
        condition = Unwrap(condition);
        if ((conditionValue ?? EvaluateCore(condition, state))
            .TryGetBoolean(out var constant))
        {
            return constant == expected ? state : ManagedFlowState.Bottom;
        }

        return condition switch
        {
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary =>
                Assume(state, unary.Operand, !expected),
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd,
                OperatorMethod: null,
                IsLifted: false
            } binary when expected =>
                Assume(Assume(state, binary.LeftOperand, true), binary.RightOperand, true),
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalOr,
                OperatorMethod: null,
                IsLifted: false
            } binary when !expected =>
                Assume(Assume(state, binary.LeftOperand, false), binary.RightOperand, false),
            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>
                AssumeComparison(state, binary.LeftOperand, binary.RightOperand,
                binary.OperatorKind, expected),
            IIsNullOperation isNull when TryStorage(isNull.Operand, out var storage) =>
                Refine(state, storage, BinaryOperatorKind.Equals, Null, expected),
            IIsPatternOperation pattern => AssumeNullPattern(state, pattern, expected),
            _ when TryStorage(condition, out var storage) =>
                state.Set(storage, Boolean(expected)),
            _ => state
        };
    }

    private ManagedFlowState AssumeComparison(
        ManagedFlowState state, IOperation left, IOperation right, BinaryOperatorKind @operator, bool expected)
    {
        var hasLeftStorage = TryStorage(left, out var leftStorage);
        var hasRightStorage = TryStorage(right, out var rightStorage);
        var leftValue = EvaluateCore(left, state);
        var rightValue = EvaluateCore(right, state);
        if (hasLeftStorage)
        {
            state = Refine(state, leftStorage, @operator, rightValue, expected);
        }

        return hasRightStorage
            ? Refine(
                state,
                rightStorage,
                CSharpScalarSemantics.ReverseBinary(@operator),
                leftValue,
                expected)
            : state;
    }

    private static ManagedFlowState AssumeNullPattern(
        ManagedFlowState state, IIsPatternOperation operation, bool expected)
    {
        var pattern = operation.Pattern;
        var negated = false;
        while (pattern is INegatedPatternOperation not)
        {
            negated = !negated;
            pattern = not.Pattern;
        }
        return pattern is IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } } &&
               TryStorage(operation.Value, out var storage)
            ? Refine(state, storage, BinaryOperatorKind.Equals, Null, expected != negated)
            : state;
    }

    internal static ManagedFlowState Refine(
        ManagedFlowState state, object storage, BinaryOperatorKind @operator, ManagedAbstractValue value, bool expected)
    {
        var current = state.Get(storage);
        if (value.TryGetInteger(out var integer))
        {
            return state.Set(storage,
                RefineInteger(current, @operator, integer, expected));
        }

        if (value.TryGetBoolean(out var boolean) && current.IsBoolean &&
            @operator is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
        {
            var booleanEquals = @operator == BinaryOperatorKind.Equals;
            return state.Set(storage, Boolean(
                expected == booleanEquals ? boolean : !boolean));
        }
        if (!value.IsDefinitelyNull)
        {
            return state;
        }

        if (!current.TryGetNullness(out var nullness))
        {
            return state;
        }

        if (@operator is not (
            BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return state;
        }

        var equals = expected == (@operator == BinaryOperatorKind.Equals);
        var refined = equals ? NullnessDomain.Instance.AssumeNull(nullness)
            : NullnessDomain.Instance.AssumeNonNull(nullness);
        return state.Set(storage, Reference(refined, current.Cardinality));
    }

    private static ManagedAbstractValue RefineInteger(
        ManagedAbstractValue current,
        BinaryOperatorKind @operator,
        IntervalValue value,
        bool expected)
    {
        if (!current.TryGetInteger(out var interval))
        {
            return current;
        }

        var normalized = expected
            ? @operator
            : CSharpScalarSemantics.NegateBinary(@operator);
        var domain = IntervalDomain.Instance;
        var refined = normalized switch
        {
            BinaryOperatorKind.Equals => Intersect(interval, value),
            BinaryOperatorKind.NotEquals when value.IsSingleton &&
                interval.IsSingleton &&
                interval.SingletonValue == value.SingletonValue =>
                IntervalValue.Bottom,
            BinaryOperatorKind.LessThan when
                value.UpperBound is > long.MinValue =>
                domain.AssumeAtMost(interval, value.UpperBound.Value - 1),
            BinaryOperatorKind.LessThanOrEqual when
                value.UpperBound.HasValue =>
                domain.AssumeAtMost(interval, value.UpperBound.Value),
            BinaryOperatorKind.GreaterThan when
                value.LowerBound is < long.MaxValue =>
                domain.AssumeAtLeast(interval, value.LowerBound.Value + 1),
            BinaryOperatorKind.GreaterThanOrEqual when
                value.LowerBound.HasValue =>
                domain.AssumeAtLeast(interval, value.LowerBound.Value),
            _ => interval
        };

        return refined.IsBottom
            ? Bottom
            : Integer(
                refined,
                current.ExcludesZero || (value.IsSingleton
                    ? normalized == BinaryOperatorKind.NotEquals &&
                      value.SingletonValue == 0
                    : !refined.Contains(0)));
    }

    private static IntervalValue Intersect(
        IntervalValue current, IntervalValue restriction)
    {
        var domain = IntervalDomain.Instance;
        var refined = restriction.LowerBound is { } lower
            ? domain.AssumeAtLeast(current, lower)
            : current;
        return restriction.UpperBound is { } upper
            ? domain.AssumeAtMost(refined, upper)
            : refined;
    }

    private ManagedAbstractValue EvaluateCore(IOperation operation, ManagedFlowState state)
    {
        if (!TryEnterWalk())
        {
            return ManagedAbstractValue.Unknown;
        }

        try
        {
            return EvaluateBounded(operation, state);
        }
        finally
        {
            ExitWalk();
        }
    }

    private static bool TryEnterWalk()
    {
        if (s_walkDepth >= MaximumWalkDepth)
        {
            return false;
        }

        s_walkDepth++;
        return true;
    }

    private static void ExitWalk()
    {
        s_walkDepth--;
    }

    private ManagedAbstractValue EvaluateBounded(IOperation operation, ManagedFlowState state)
    {
        operation = Unwrap(operation);
        if (operation.ConstantValue.HasValue)
        {
            return FromConstant(operation.ConstantValue.Value, operation.Type);
        }

        return operation switch
        {
            IParameterReferenceOperation parameter => state.Get(parameter.Parameter),
            ILocalReferenceOperation local => state.Get(local.Local),
            IFlowCaptureReferenceOperation capture => state.Get(capture.Id),
            IDefaultValueOperation value => DefaultForType(value.Type),
            IInstanceReferenceOperation or IConditionalAccessInstanceOperation or IObjectCreationOperation or
                ITypeOfOperation => NonNull,
            IArrayCreationOperation array => EvaluateArray(array, state),
            IPropertyReferenceOperation property => EvaluateProperty(property, state),
            IInvocationOperation invocation => ReturnValue(invocation.TargetMethod, invocation.Type),
            IIsNullOperation isNull => NullTest(isNull, state),
            IConversionOperation conversion => ConvertValue(conversion, state),
            IUnaryOperation unary => EvaluateUnary(unary, state),
            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>
                Binary(binary.OperatorKind,
                EvaluateCore(binary.LeftOperand, state), EvaluateCore(binary.RightOperand, state), binary.Type),
            IConditionalOperation conditional => EvaluateConditional(conditional, state),
            ICoalesceOperation coalesce => EvaluateCoalesce(coalesce, state),
            ISimpleAssignmentOperation assignment => EvaluateCore(assignment.Value, state),
            IFlowCaptureOperation capture => EvaluateCore(capture.Value, state),
            _ => TopForType(operation.Type)
        };
    }

    private ManagedAbstractValue EvaluateArray(IArrayCreationOperation array, ManagedFlowState state)
    {
        if (array.DimensionSizes.Length != 1 ||
            !EvaluateCore(array.DimensionSizes[0], state).TryGetInteger(out var size))
        {
            return NonNull;
        }

        return Reference(NullnessValue.NonNull, IntervalDomain.Instance.AssumeAtLeast(size, 0));
    }

    private ManagedAbstractValue EvaluateProperty(IPropertyReferenceOperation property, ManagedFlowState state)
    {
        if (CompilerIdentityBridge.IsIntrinsicSequenceLength(property))
        {
            var instance = property.Instance!;
            var receiver = EvaluateCore(instance, state);
            if (receiver.TryGetCardinality(out var length))
            {
                return Integer(length);
            }

            if (instance.Type is IArrayTypeSymbol ||
                instance.Type?.SpecialType == SpecialType.System_String)
            {
                return Integer(IntervalValue.Range(
                    0, property.Type?.SpecialType == SpecialType.System_Int64 ? long.MaxValue : int.MaxValue));
            }
        }
        return ReturnValue(property.Property.GetMethod, property.Type);
    }

    private ManagedAbstractValue ReturnValue(IMethodSymbol? method, ITypeSymbol? type)
    {
        var value = TopForType(type);
        if (method != null &&
            _trustedBoundaries.AuthorizesDeclaredContracts(method))
        {
            value = ApplyAttributes(
                value,
                method.GetReturnTypeAttributes());
        }
        if (method == null ||
            !_apiSpecs.TryGet(method, out var spec) ||
            !value.TryGetNullness(out var nullness))
        {
            return value;
        }

        nullness = spec.Template.Facets.Nullness.Result switch
        {
            SpecNullness.NonNull =>
                NullnessDomain.Instance.AssumeNonNull(nullness),
            SpecNullness.Null => NullnessValue.Null,
            _ => nullness
        };
        if (nullness == NullnessValue.Null)
        {
            return Null;
        }

        var cardinality = spec.Template.Facets.Cardinality.Result switch
        {
            SpecCardinality.Empty => IntervalValue.Constant(0),
            SpecCardinality.NonEmpty => IntervalValue.Range(1, null),
            SpecCardinality.Exact when
                spec.Template.Facets.Cardinality.ExactCount is { } count =>
                IntervalValue.Constant(count),
            _ => value.Cardinality
        };
        return Reference(nullness, cardinality);
    }

    private ManagedAbstractValue NullTest(IIsNullOperation operation, ManagedFlowState state)
    {
        return EvaluateCore(operation.Operand, state).TryGetNullness(out var value)
            ? value switch
            {
                NullnessValue.Null => Boolean(true),
                NullnessValue.NonNull => Boolean(false),
                _ => BooleanUnknown
            }
            : BooleanUnknown;
    }

    private ManagedAbstractValue ConvertValue(IConversionOperation conversion, ManagedFlowState state)
    {
        var operand = EvaluateCore(conversion.Operand, state);
        if (ValuePreserving(conversion))
        {
            return operand;
        }

        if (string.Equals(
                conversion.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal) &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                .GetConversion(conversion).IsBoxing)
        {
            return IsNullableType(conversion.Operand.Type) &&
                   operand.TryGetNullness(out var boxedNullness)
                ? Reference(boxedNullness, operand.Cardinality)
                : NonNull;
        }

        if (IsNullableType(conversion.Type) &&
            conversion.OperatorMethod == null &&
            !conversion.Conversion.IsUserDefined)
        {
            if (operand.TryGetNullness(out var nullableNullness))
            {
                return Reference(nullableNullness, operand.Cardinality);
            }

            return IsNullableType(conversion.Operand.Type)
                ? Reference(NullnessValue.MaybeNull)
                : NonNull;
        }

        return !conversion.IsTryCast && conversion.OperatorMethod == null && conversion.Conversion.IsReference &&
               operand.TryGetNullness(out var nullness)
            ? Reference(nullness, operand.Cardinality)
            : TopForType(conversion.Type);
    }

    private ManagedAbstractValue EvaluateUnary(IUnaryOperation unary, ManagedFlowState state)
    {
        var operand = EvaluateCore(unary.Operand, state);
        if (unary.OperatorKind == UnaryOperatorKind.Not)
        {
            return NegateBoolean(operand);
        }

        return unary.OperatorKind == UnaryOperatorKind.Minus && operand.TryGetInteger(out var interval) &&
               TryNegate(interval, out var negated)
            ? KeepWithinType(negated, unary.Type)
            : TopForType(unary.Type);
    }

    private ManagedAbstractValue EvaluateConditional(IConditionalOperation operation, ManagedFlowState state)
    {
        var conditionValue = EvaluateCore(operation.Condition, state);
        if (conditionValue.TryGetBoolean(out var condition))
        {
            return EvaluateCore(condition ? operation.WhenTrue : operation.WhenFalse!, state);
        }

        return operation.WhenFalse == null
            ? Unknown
            : ManagedAbstractValue.Join(
                EvaluateCore(
                    operation.WhenTrue,
                    Assume(state, operation.Condition, true, conditionValue)),
                EvaluateCore(
                    operation.WhenFalse,
                    Assume(state, operation.Condition, false, conditionValue)));
    }

    private ManagedAbstractValue EvaluateCoalesce(ICoalesceOperation operation, ManagedFlowState state)
    {
        var value = EvaluateCore(operation.Value, state);
        if (value.IsDefinitelyNonNull)
        {
            return value;
        }

        if (value.IsDefinitelyNull)
        {
            return EvaluateCore(operation.WhenNull, state);
        }

        var whenNull = EvaluateCore(operation.WhenNull, state);
        var joined = Join(value, whenNull);

        // A coalesce cannot produce null when its fallback is definitely non-null.
        // Keep the joined reference cardinality while refining its nullness.
        return whenNull.IsDefinitelyNonNull && !joined.IsUnknown && !joined.IsBottom
            ? Reference(NullnessValue.NonNull, joined.Cardinality)
            : joined;
    }

    internal bool ProvesNoOverflow(IOperation operation, ManagedFlowState state)
    {
        operation = Unwrap(operation);
        IntervalValue interval;
        ITypeSymbol? type;
        switch (operation)
        {
            case IBinaryOperation binary when binary.OperatorKind is
                BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply:
                if (!EvaluateCore(binary.LeftOperand, state).TryGetInteger(out var left) ||
                    !EvaluateCore(binary.RightOperand, state).TryGetInteger(out var right) ||
                    !TryArithmetic(binary.OperatorKind, left, right, out interval))
                {
                    return false;
                }

                type = binary.Type;
                break;
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus } unary:
                if (!EvaluateCore(unary.Operand, state).TryGetInteger(out var operand) ||
                    !TryNegate(operand, out interval))
                {
                    return false;
                }

                type = unary.Type;
                break;
            case IIncrementOrDecrementOperation increment:
                if (!TryIncrement(increment, state, out interval))
                {
                    return false;
                }

                type = increment.Type;
                break;
            case IConversionOperation conversion:
                if (!EvaluateCore(conversion.Operand, state).TryGetInteger(out interval))
                {
                    return false;
                }

                type = conversion.Type;
                break;
            default:
                return false;
        }
        return FitsType(interval, type);
    }

    private ManagedAbstractValue ApplyAttributes(
        ManagedAbstractValue value, ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (Matches(attribute, _notNullAttribute) && value.TryGetNullness(out var nullness))
            {
                value = Reference(
                    NullnessDomain.Instance.AssumeNonNull(nullness), value.Cardinality);
            }
            else if (Matches(attribute, _positiveAttribute) && value.TryGetInteger(out var positive))
            {
                value = Integer(IntervalDomain.Instance.AssumeAtLeast(positive, 1));
            }
            else if (Matches(attribute, _inRangeAttribute) && value.TryGetInteger(out var range) &&
                     attribute.ConstructorArguments.Length == 2 &&
                     attribute.ConstructorArguments[0].Value is long minimum &&
                     attribute.ConstructorArguments[1].Value is long maximum && minimum <= maximum)
            {
                value = Integer(IntervalDomain.Instance.AssumeAtMost(
                    IntervalDomain.Instance.AssumeAtLeast(range, minimum), maximum));
            }
        }
        return value;
    }

    private static bool Matches(AttributeData attribute, INamedTypeSymbol? expected)
    {
        return expected != null && SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition, expected.OriginalDefinition);
    }

    private bool IsRequires(IInvocationOperation invocation)
    {
        return invocation.TargetMethod is
        {
            IsStatic: true,
            ReturnsVoid: true,
            Name: ContractApiCatalog.RequiresMethodName,
            Parameters.Length: 1
        } method &&
        method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean &&
        invocation.Arguments.Length == 1 &&
        _contractApi != null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _contractApi.OriginalDefinition);
    }

    private static ManagedFlowState HavocCall(
        ManagedFlowState state, IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments)
    {
        return method.MethodKind == MethodKind.LocalFunction ||
            method.ContainingType.TypeKind == TypeKind.Delegate ||
            arguments.Any(static argument =>
                CanCarryDelegate(argument.Value))
            ? state.Forget()
            : HavocArguments(state, arguments);
    }

    private static bool CanCarryDelegate(IOperation value)
    {
        value = Unwrap(value);
        return value is IDelegateCreationOperation ||
            value.Type is { } type &&
            (type.TypeKind == TypeKind.Delegate ||
             type.SpecialType is SpecialType.System_Delegate or
                 SpecialType.System_MulticastDelegate);
    }

    private static ManagedFlowState HavocArguments(
        ManagedFlowState state, ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                state = HavocArgumentStorage(state, argument.Value);
            }
        }

        return state;
    }

    private static ManagedFlowState HavocArgumentStorage(
        ManagedFlowState state,
        IOperation value)
    {
        value = Unwrap(value);
        if (value is IConditionalOperation conditional)
        {
            state = HavocArgumentStorage(state, conditional.WhenTrue);
            return conditional.WhenFalse == null
                ? state.Forget()
                : HavocArgumentStorage(state, conditional.WhenFalse);
        }

        if (value is IFlowCaptureReferenceOperation)
        {
            // A ref conditional is represented in the CFG by one capture that
            // can alias either source storage. This value domain does not
            // retain capture-to-storage aliases, so all scalar facts must be
            // forgotten rather than updating only the synthetic capture.
            return state.Forget();
        }

        return TryStorage(value, out var storage)
            ? state.Set(storage, TopForType(value.Type))
            : state;
    }

    private static ManagedFlowState SetStorage(
        ManagedFlowState state, IOperation operation, ManagedAbstractValue value)
    {
        return TryStorage(operation, out var storage) ? state.Set(storage, value) : state;
    }

    private static bool IsUntrackedRefLocal(IOperation operation)
    {
        return DefiniteOperationFacts.UnwrapHarmlessValue(operation) is ILocalReferenceOperation local &&
            IsUntrackedManagedReference(local.Local.RefKind);
    }

    private static ManagedFlowState MarkUntrackedAlias(
        ManagedFlowState state,
        IOperation target,
        out bool aliased)
    {
        aliased = IsUntrackedRefLocal(target);
        // Roslyn lowers ref-local declarations and writes through ref locals
        // to assignments whose storage this domain cannot identify.
        return aliased ? state.WithUntrackedAlias() : state;
    }

    private static bool IsUntrackedManagedReference(RefKind refKind)
    {
        return refKind is RefKind.Ref or RefKind.RefReadOnly or RefKind.RefReadOnlyParameter;
    }

    private static bool TryStorage(IOperation operation, out object storage)
    {
        operation = Unwrap(operation);
        storage = operation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            IFlowCaptureReferenceOperation capture => capture.Id,
            _ => null!
        };
        return operation is IParameterReferenceOperation or ILocalReferenceOperation or IFlowCaptureReferenceOperation;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else if (operation is IConversionOperation conversion && ValuePreserving(conversion))
            {
                operation = conversion.Operand;
            }
            else
            {
                return operation;
            }
        }
    }

    private static bool ValuePreserving(IConversionOperation conversion)
    {
        if (conversion is { OperatorMethod: not null } or
            { Conversion: { IsUserDefined: true } } or
            { IsTryCast: true })
        {
            return false;
        }

        if (conversion.Conversion is { IsIdentity: true } or
            { IsImplicit: true, IsReference: true })
        {
            return true;
        }

        return IntegerType(conversion.Operand.Type, out var source) &&
               IntegerType(conversion.Type, out var target) &&
               source.Minimum >= target.Minimum && source.Maximum <= target.Maximum;
    }

    internal static bool IsAcyclic(ControlFlowGraph graph)
    {
        return IsAcyclic(graph, included: null);
    }

    internal static bool IsAcyclic(
        ControlFlowGraph graph,
        ISet<int>? included)
    {
        var marks = new byte[graph.Blocks.Length];
        bool VisitIncluded(BasicBlock block)
        {
            if (marks[block.Ordinal] != 0)
            {
                return marks[block.Ordinal] == 2;
            }

            marks[block.Ordinal] = 1;
            foreach (var (branch, _) in Successors(block))
            {
                if (branch.Destination != null && !Visit(branch.Destination))
                {
                    return false;
                }
            }

            marks[block.Ordinal] = 2;
            return true;
        }

        bool Visit(BasicBlock block)
        {
            if (included != null && !included.Contains(block.Ordinal))
            {
                return true;
            }

            return VisitIncluded(block);
        }

        return graph.Blocks
            .Where(block => block.IsReachable &&
                (included == null || included.Contains(block.Ordinal)))
            .All(VisitIncluded);
    }

    private static IEnumerable<(ControlFlowBranch Branch, bool? Expected)> Successors(BasicBlock block)
    {
        bool? expected = block.ConditionKind switch
        {
            ControlFlowConditionKind.WhenTrue => true,
            ControlFlowConditionKind.WhenFalse => false,
            _ => null
        };
        var constant = block.BranchValue?.ConstantValue is { HasValue: true, Value: bool value }
            ? value
            : (bool?)null;
        if (block.FallThroughSuccessor is
            { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } &&
            IsFeasible(!expected, constant))
        {
            yield return (block.FallThroughSuccessor!, !expected);
        }

        if (block.ConditionalSuccessor is
            { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } &&
            IsFeasible(expected, constant))
        {
            yield return (block.ConditionalSuccessor!, expected);
        }
    }

    private static bool IsFeasible(bool? expected, bool? constant)
    {
        return !expected.HasValue ||
               !constant.HasValue ||
               expected.Value == constant.Value;
    }

    internal static bool IsCompileTimeUnreachable(
        Compilation compilation,
        IOperation operation)
    {
        var statement = operation.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement == null)
        {
            return false;
        }

        var key = (
            statement.SyntaxTree,
            statement.SpanStart,
            statement.Span.Length);
        var cache = CompileTimeUnreachableStatementCache.GetValue(
            compilation,
            static _ => new());
        if (cache.ContainsKey(key))
        {
            return true;
        }

        var unreachable = false;
        var statementModel = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, statement.SyntaxTree);
        try
        {
            if (statementModel.AnalyzeControlFlow(statement) is
                { Succeeded: true, StartPointIsReachable: false })
            {
                unreachable = true;
            }
        }
        catch (ArgumentException)
        {
            // Unsupported statement shapes retain the permissive fallback.
        }
        if (unreachable)
        {
            cache.TryAdd(key, true);
            return true;
        }

        foreach (var syntax in operation.Syntax.Ancestors())
        {
            SyntaxNode? condition = syntax switch
            {
                WhileStatementSyntax @while
                    when @while.Statement.Span.Contains(operation.Syntax.Span) =>
                    @while.Condition,
                ForStatementSyntax @for
                    when @for.Condition != null &&
                         (@for.Statement.Span.Contains(operation.Syntax.Span) ||
                          @for.Incrementors.Any(incrementor =>
                              incrementor.Span.Contains(operation.Syntax.Span))) =>
                    @for.Condition,
                _ => null
            };
            if (condition == null)
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, condition.SyntaxTree);
            if (model.GetConstantValue(condition) is { HasValue: true, Value: false })
            {
                unreachable = true;
                break;
            }
        }

        return unreachable;
    }

    private static EffectAnalysisIncompleteReason CheckBudget(
        ControlFlowGraph graph, CancellationToken cancellationToken)
    {
        if (graph.Blocks.Length > MaxAnalyzedBlocks)
        {
            return EffectAnalysisIncompleteReason.BlockBudgetExceeded;
        }

        var operationCount = 0;
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var root in block.Operations.Concat(
                         block.BranchValue == null ? [] : [block.BranchValue]))
            {
                foreach (var _ in root.DescendantsAndSelf())
                {
                    if (operationCount == MaxAnalyzedOperations)
                    {
                        return EffectAnalysisIncompleteReason.OperationBudgetExceeded;
                    }

                    operationCount++;
                }
            }
        }
        return EffectAnalysisIncompleteReason.None;
    }

    private static bool TryNegate(IntervalValue value, out IntervalValue result)
    {
        if (value.IsBottom || !value.LowerBound.HasValue || !value.UpperBound.HasValue ||
            value.LowerBound.Value == long.MinValue)
        {
            result = IntervalValue.Top;
            return false;
        }
        result = IntervalValue.Range(-value.UpperBound.Value, -value.LowerBound.Value);
        return true;
    }

    private sealed class FlowDomain : ClosedAbstractDomain<ManagedFlowState>
    {
        internal static FlowDomain Instance { get; } = new();
        public override ManagedFlowState Bottom => ManagedFlowState.Bottom;
        public override ManagedFlowState Top => ManagedFlowState.Top;
        public override ManagedFlowState Join(ManagedFlowState left, ManagedFlowState right)
        {
            return ManagedFlowState.Join(left, right);
        }

        public override ManagedFlowState Widen(ManagedFlowState previous, ManagedFlowState candidate)
        {
            return Join(previous, candidate);
        }

        public override ManagedFlowState Havoc(ManagedFlowState value)
        {
            return value.Forget();
        }

        public override bool LessThanOrEqual(ManagedFlowState left, ManagedFlowState right)
        {
            return ManagedFlowState.LessThanOrEqual(left, right);
        }
    }
    internal bool IsBlockedAfterNoncompletingStatement(
        IOperation operation)
    {
        var statement = operation.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement?.Parent is not BlockSyntax block)
        {
            return false;
        }
        if (statement is LocalFunctionStatementSyntax ||
            operation.Syntax.Ancestors().TakeWhile(candidate =>
                candidate != statement).Any(static candidate =>
                    candidate is AnonymousFunctionExpressionSyntax))
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, block.SyntaxTree);
        foreach (var prior in block.Statements
                     .TakeWhile(candidate => candidate != statement))
        {
            var priorOperation = model.GetOperation(prior);
            if (priorOperation != null &&
                !_completionFacts.MayCompleteNormally(priorOperation))
            {
                return true;
            }
        }
        return false;
    }
}

internal enum ManagedFlowStatus
{
    Complete,
    BudgetExceeded,
    Cyclic
}

internal sealed class ManagedFlowAnalysis
{
    private ManagedFlowAnalysis(
        ManagedFlowStatus status,
        EffectAnalysisIncompleteReason reason,
        ManagedFlowResult? result)
    {
        var valid = status switch
        {
            ManagedFlowStatus.Complete =>
                reason == EffectAnalysisIncompleteReason.None && result != null,
            ManagedFlowStatus.BudgetExceeded =>
                reason is EffectAnalysisIncompleteReason.BlockBudgetExceeded or
                    EffectAnalysisIncompleteReason.OperationBudgetExceeded &&
                result == null,
            ManagedFlowStatus.Cyclic =>
                reason == EffectAnalysisIncompleteReason.CyclicControlFlow && result == null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("Managed-flow status, reason, and result are inconsistent.");
        }

        Status = status;
        IncompleteReason = reason;
        Result = result;
    }

    internal ManagedFlowStatus Status
    {
        get;
    }
    internal EffectAnalysisIncompleteReason IncompleteReason
    {
        get;
    }
    internal ManagedFlowResult? Result
    {
        get;
    }
    internal bool IsComplete => Status == ManagedFlowStatus.Complete;

    internal static ManagedFlowAnalysis Complete(ManagedFlowResult result)
    {
        return new(ManagedFlowStatus.Complete, EffectAnalysisIncompleteReason.None,
            ArgumentNullGuard.NotNull(result, nameof(result)));
    }

    internal static ManagedFlowAnalysis BudgetExceeded(EffectAnalysisIncompleteReason reason)
    {
        return new(ManagedFlowStatus.BudgetExceeded, reason, null);
    }

    internal static ManagedFlowAnalysis Cyclic()
    {
        return new(ManagedFlowStatus.Cyclic, EffectAnalysisIncompleteReason.CyclicControlFlow, null);
    }
}

internal sealed class ManagedFlowResult(ManagedAbstractFlow flow)
{
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures = new();
    private readonly Dictionary<object, ManagedFlowState> _states = new(ManagedKeyComparer.Instance);
    private readonly Dictionary<IOperation, bool> _mutationFacts = new();
    private readonly Dictionary<IOperation, bool> _reachabilityFacts = new();

    internal bool HasMutation(IOperation operation)
    {
        if (_mutationFacts.TryGetValue(operation, out var hasMutation))
        {
            return hasMutation;
        }

        hasMutation = ManagedMutationFacts.HasMutation(operation);
        _mutationFacts.Add(operation, hasMutation);
        return hasMutation;
    }

    internal void RecordCoalesceAssignmentCapture(
        IFlowCaptureOperation capture)
    {
        _coalesceCaptures.Record(capture);
    }

    internal IOperation ResolveCoalesceAssignmentTarget(IOperation target)
    {
        return _coalesceCaptures.Resolve(target);
    }

    internal void Record(IOperation operation, ManagedFlowState state)
    {
        if (state.IsBottom)
        {
            return;
        }

        Add(operation, state);
        Add(Key(operation), state);
    }

    internal bool TryGetState(IOperation operation, out ManagedFlowState state)
    {
        return _states.TryGetValue(operation, out state!) || _states.TryGetValue(Key(operation), out state!);
    }

    internal bool IsReachable(IOperation operation)
    {
        if (_reachabilityFacts.TryGetValue(operation, out var reachable))
        {
            return reachable;
        }

        reachable = !flow.IsBlockedAfterNoncompletingStatement(operation) &&
            operation.DescendantsAndSelf().Any(candidate =>
                TryGetState(candidate, out var state) && !state.IsBottom);
        _reachabilityFacts.Add(operation, reachable);
        return reachable;
    }

    internal static IOperation? GetUnavoidableDirectOperation(
        IOperation root, SyntaxNode? directSyntax)
    {
        if (directSyntax == null ||
            root.Syntax.SyntaxTree != directSyntax.SyntaxTree ||
            root.Syntax.Span != directSyntax.Span ||
            root.DescendantsAndSelf().Any(IsControlFlow))
        {
            return null;
        }

        var operation = root;
        while (true)
        {
            operation = operation switch
            {
                IReturnOperation { ReturnedValue: { } value } => value,
                IExpressionStatementOperation statement => statement.Operation,
                _ => operation
            };
            var unwrapped = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
            if (ReferenceEquals(unwrapped, operation))
            {
                return operation;
            }

            operation = unwrapped;
        }
    }

    internal bool TryEvaluate(IOperation origin, IOperation value, out ManagedAbstractValue result)
    {
        return TryEvaluate(
            origin,
            value,
            HasMutation(value),
            out result);
    }

    private bool TryEvaluate(
        IOperation origin,
        IOperation value,
        bool hasMutation,
        out ManagedAbstractValue result)
    {
        if (!hasMutation &&
            (TryGetState(value, out var state) ||
             TryGetState(origin, out state)))
        {
            result = flow.Evaluate(value, state);
            return true;
        }

        result = Unknown;
        return false;
    }

    internal bool TryEvaluateAtOrigin(
        IOperation origin,
        IOperation value,
        out ManagedAbstractValue result)
    {
        if (!HasMutation(value) &&
            TryGetState(origin, out var state))
        {
            result = flow.Evaluate(value, state);
            return true;
        }
        result = Unknown;
        return false;
    }

    internal bool ProvesNonNull(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNonNull;
    }

    internal bool ProvesNull(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNull;
    }

    internal bool ProvesNonZero(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNonZero;
    }

    internal bool ProvesNonNegative(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) &&
        result.TryGetInteger(out var interval) && interval.LowerBound >= 0;
    }

    internal bool ProvesNoSignedDivisionOverflow(
        IOperation origin, IOperation left, IOperation right, long minimum)
    {
        var rightHasMutation = HasMutation(right);
        return !rightHasMutation &&
        TryEvaluate(origin, left, out var leftValue) &&
        TryEvaluate(origin, right, rightHasMutation, out var rightValue) &&
        leftValue.TryGetInteger(out var leftInterval) &&
        rightValue.TryGetInteger(out var rightInterval) &&
        (!leftInterval.Contains(minimum) || !rightInterval.Contains(-1));
    }

    internal bool ProvesArrayAccess(IArrayElementReferenceOperation element)
    {
        if (element.Indices.Length != 1)
        {
            return false;
        }

        var index = element.Indices[0];
        var indexHasMutation = HasMutation(index);
        return !indexHasMutation &&
        TryEvaluate(element, element.ArrayReference, out var array) &&
        TryEvaluate(element, index, indexHasMutation, out var indexValue) &&
        array.IsDefinitelyNonNull &&
        array.TryGetCardinality(out var length) && length.LowerBound.HasValue &&
        indexValue.TryGetInteger(out var interval) &&
        interval.LowerBound >= 0 && interval.UpperBound < length.LowerBound;
    }

    internal bool ProvesNoOverflow(IOperation operation)
    {
        return !HasMutation(operation)
            ? TryGetState(operation, out var state) && flow.ProvesNoOverflow(operation, state)
            : false;
    }

    private static bool IsControlFlow(IOperation operation)
    {
        return operation is IConditionalOperation or IConditionalAccessOperation or ISwitchOperation or
            ISwitchExpressionOperation or ILoopOperation or ITryOperation;
    }

    private void Add(object key, ManagedFlowState state)
    {
        _states[key] = _states.TryGetValue(key, out var current) ? ManagedFlowState.Join(current, state) : state;
    }

    internal static bool HasSameIdentity(IOperation operation, IOperation? candidate)
    {
        return candidate is not null &&
            (ReferenceEquals(operation, candidate) || Key(operation) == Key(candidate));
    }

    private static (SyntaxTree, int, int, OperationKind) Key(IOperation operation)
    {
        return (operation.Syntax.SyntaxTree, operation.Syntax.SpanStart, operation.Syntax.Span.Length, operation.Kind);
    }
}

internal sealed class ManagedFlowState
{
    private static readonly ManagedKeyComparer Comparer = ManagedKeyComparer.Instance;
    private static readonly ImmutableDictionary<object, ManagedAbstractValue> NoValues =
        ImmutableDictionary.Create<object, ManagedAbstractValue>(Comparer);
    private readonly ImmutableDictionary<object, ManagedAbstractValue>? _values;
    private readonly bool _hasUntrackedAlias;

    private ManagedFlowState(
        ImmutableDictionary<object, ManagedAbstractValue>? values,
        bool hasUntrackedAlias = false)
    {
        _values = values;
        _hasUntrackedAlias = hasUntrackedAlias;
    }

    internal static ManagedFlowState Bottom { get; } = new(null);
    internal static ManagedFlowState Empty { get; } = new(NoValues);
    internal static ManagedFlowState Top { get; } = new(NoValues, hasUntrackedAlias: true);
    internal bool IsBottom => _values == null;
    internal ManagedAbstractValue Get(object storage)
    {
        if (_values == null)
        {
            return ManagedAbstractValue.Bottom;
        }

        if (_values.TryGetValue(storage, out var value))
        {
            return value;
        }

        return storage is ISymbol symbol
            ? TopForType(symbol is IParameterSymbol parameter ? parameter.Type :
                symbol is ILocalSymbol local ? local.Type : null)
            : Unknown;
    }

    internal ManagedFlowState Set(object storage, ManagedAbstractValue value)
    {
        if (_values == null || value.IsBottom)
        {
            return Bottom;
        }

        return _hasUntrackedAlias ? this : new(_values.SetItem(storage, value));
    }

    internal ManagedFlowState WithUntrackedAlias()
    {
        return IsBottom ? this : Top;
    }

    internal ManagedFlowState Forget()
    {
        return IsBottom || _hasUntrackedAlias ? this : Empty;
    }

    internal static ManagedFlowState Join(ManagedFlowState left, ManagedFlowState right)
    {
        if (left._values == null)
        {
            return right;
        }

        if (right._values == null)
        {
            return left;
        }

        if (left._hasUntrackedAlias || right._hasUntrackedAlias)
        {
            return Top;
        }

        var result = NoValues.ToBuilder();
        foreach (var key in left._values.Keys.Union(right._values.Keys, Comparer))
        {
            result[key] = ManagedAbstractValue.Join(left.Get(key), right.Get(key));
        }

        return new(result.ToImmutable());
    }

    internal static bool LessThanOrEqual(ManagedFlowState left, ManagedFlowState right)
    {
        if (left._values == null)
        {
            return true;
        }

        if (right._values == null)
        {
            return false;
        }

        if (right._hasUntrackedAlias)
        {
            return true;
        }

        if (left._hasUntrackedAlias)
        {
            return false;
        }

        return left._values.Keys.Union(right._values.Keys, Comparer).All(key =>
        {
            var rightValue = right.Get(key);
            return ManagedAbstractValue.Join(left.Get(key), rightValue) == rightValue;
        });
    }
}

internal sealed class ManagedKeyComparer : IEqualityComparer<object>
{
    internal static ManagedKeyComparer Instance { get; } = new();
    public new bool Equals(object? left, object? right)
    {
        return left is IOperation || right is IOperation
            ? ReferenceEquals(left, right)
            : left is ISymbol leftSymbol && right is ISymbol rightSymbol
                ? SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
                : object.Equals(left, right);
    }

    public int GetHashCode(object value)
    {
        return value is IOperation
                ? RuntimeHelpers.GetHashCode(value)
                : value is ISymbol symbol
                    ? SymbolEqualityComparer.Default.GetHashCode(symbol)
                    : value.GetHashCode();
    }
}

internal readonly record struct ManagedAbstractValue(
    IntervalValue Scalar,
    NullnessValue Nullness,
    IntervalValue Cardinality,
    bool ExcludesZero,
    bool IsUnknown,
    bool IsBoolean)
{
    internal static ManagedAbstractValue Bottom => default;
    internal static ManagedAbstractValue Unknown { get; } = new(default, default, default, false, true, false);
    internal static ManagedAbstractValue BooleanUnknown
    {
        get;
    } =
        new(IntervalValue.Range(0, 1), default, default, false, false, true);
    internal static ManagedAbstractValue Null => Reference(NullnessValue.Null);
    internal static ManagedAbstractValue NonNull => Reference(NullnessValue.NonNull);
    internal bool IsBottom => !IsUnknown && Scalar.IsBottom && Nullness == NullnessValue.Bottom;
    internal bool IsDefinitelyNull => Nullness == NullnessValue.Null;
    internal bool IsDefinitelyNonNull => Nullness == NullnessValue.NonNull;
    internal bool IsDefinitelyNonZero => !Scalar.IsBottom && (ExcludesZero || !Scalar.Contains(0));

    internal static ManagedAbstractValue Boolean(bool value)
    {
        return new(IntervalValue.Constant(value ? 1 : 0), default, default, false, false, true);
    }

    internal static ManagedAbstractValue Integer(IntervalValue value, bool excludesZero = false)
    {
        return value.IsBottom ? Bottom : new(value, default, default, excludesZero, false, false);
    }

    internal static ManagedAbstractValue Reference(
            NullnessValue value, IntervalValue cardinality = default)
    {
        return value == NullnessValue.Bottom ? Bottom : new(default, value, cardinality, false, false, false);
    }

    internal static ManagedAbstractValue TopForType(ITypeSymbol? type)
    {
        return ValueForType(type, useDefault: false);
    }

    internal static ManagedAbstractValue DefaultForType(ITypeSymbol? type)
    {
        return ValueForType(type, useDefault: true);
    }

    private static ManagedAbstractValue ValueForType(
        ITypeSymbol? type,
        bool useDefault)
    {
        if (type?.SpecialType == SpecialType.System_Boolean)
        {
            return useDefault ? Boolean(false) : BooleanUnknown;
        }

        if (IntegerType(type, out var integer))
        {
            return Integer(useDefault
                ? IntervalValue.Constant(0)
                : IntervalValue.Range(integer.Minimum, integer.Maximum));
        }

        return type?.IsReferenceType is true || IsNullableType(type)
            ? useDefault ? Null : Reference(NullnessValue.MaybeNull)
            : Unknown;
    }

    internal static ManagedAbstractValue FromConstant(object? value, ITypeSymbol? type)
    {
        if (value == null)
        {
            return Null;
        }

        if (value is bool boolean)
        {
            return Boolean(boolean);
        }

        if (value is string text)
        {
            return Reference(NullnessValue.NonNull, IntervalValue.Constant(text.Length));
        }

        try
        {
            return IntegerType(type, out _)
                ? Integer(IntervalValue.Constant(Convert.ToInt64(value, CultureInfo.InvariantCulture)))
                : Unknown;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return Unknown;
        }
    }

    internal bool TryGetBoolean(out bool value)
    {
        value = !Scalar.IsBottom && Scalar.IsSingleton && Scalar.SingletonValue != 0;
        return IsBoolean && Scalar.IsSingleton;
    }

    internal bool TryGetInteger(out IntervalValue value)
    {
        value = Scalar;
        return !IsBoolean && !Scalar.IsBottom;
    }

    internal bool TryGetNullness(out NullnessValue value)
    {
        value = Nullness;
        return Nullness != NullnessValue.Bottom;
    }

    internal bool TryGetCardinality(out IntervalValue value)
    {
        value = Cardinality;
        return Nullness != NullnessValue.Bottom && !Cardinality.IsBottom;
    }

    internal static ManagedAbstractValue Binary(
        BinaryOperatorKind @operator, ManagedAbstractValue left, ManagedAbstractValue right, ITypeSymbol? type = null)
    {
        var unknown = type == null ? Unknown : TopForType(type);
        if (@operator is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And &&
            left.TryGetBoolean(out var leftBoolean) && right.TryGetBoolean(out var rightBoolean))
        {
            return Boolean(leftBoolean && rightBoolean);
        }

        if (@operator is BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or &&
            left.TryGetBoolean(out leftBoolean) && right.TryGetBoolean(out rightBoolean))
        {
            return Boolean(leftBoolean || rightBoolean);
        }

        if (@operator is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
        {
            return Equality(left, right, @operator == BinaryOperatorKind.NotEquals, unknown);
        }

        if (!left.TryGetInteger(out var leftInteger) || !right.TryGetInteger(out var rightInteger))
        {
            return unknown;
        }

        if (@operator is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
        {
            return Compare(leftInteger, rightInteger, @operator, unknown);
        }

        return TryArithmetic(@operator, leftInteger, rightInteger, out var result)
            ? KeepWithinType(result, type)
            : unknown;
    }

    internal static ManagedAbstractValue NegateBoolean(ManagedAbstractValue value)
    {
        // Negation preserves the Boolean domain even when the value is not a
        // singleton. Keeping BooleanUnknown allows subsequent Boolean
        // equality/refinement instead of widening the result to an untyped
        // value.
        return value.TryGetBoolean(out var boolean)
            ? Boolean(!boolean)
            : value.IsBoolean
                ? BooleanUnknown
                : Unknown;
    }

    internal static bool TryArithmetic(
        BinaryOperatorKind @operator, IntervalValue left, IntervalValue right, out IntervalValue result)
    {
        result = IntervalValue.Top;
        if (left.LowerBound is not { } leftMinimum ||
            left.UpperBound is not { } leftMaximum ||
            right.LowerBound is not { } rightMinimum ||
            right.UpperBound is not { } rightMaximum)
        {
            return false;
        }

        var a = new BigInteger(leftMinimum);
        var b = new BigInteger(leftMaximum);
        var c = new BigInteger(rightMinimum);
        var d = new BigInteger(rightMaximum);
        BigInteger minimum;
        BigInteger maximum;
        switch (@operator)
        {
            case BinaryOperatorKind.Add:
                minimum = a + c;
                maximum = b + d;
                break;
            case BinaryOperatorKind.Subtract:
                minimum = a - d;
                maximum = b - c;
                break;
            case BinaryOperatorKind.Multiply:
                {
                    minimum = maximum = a * c;
                    Include(ref minimum, ref maximum, a * d);
                    Include(ref minimum, ref maximum, b * c);
                    Include(ref minimum, ref maximum, b * d);

                    break;
                }
            default:
                return false;
        }

        if (minimum < long.MinValue || maximum > long.MaxValue)
        {
            return false;
        }

        result = IntervalValue.Range((long)minimum, (long)maximum);
        return true;

        static void Include(
            ref BigInteger minimum,
            ref BigInteger maximum,
            BigInteger candidate)
        {
            if (candidate < minimum)
            {
                minimum = candidate;
            }
            else if (candidate > maximum)
            {
                maximum = candidate;
            }
        }
    }

    /// <summary>
    /// Binary evaluation over IR scalars, where no Roslyn type symbol is
    /// available to bound the result. The IR integer domain is exactly Int64 —
    /// the frontend admits exact arithmetic only for <c>long</c>, see
    /// <c>CSharpScalarSemantics.SupportsExactIrArithmetic</c> — and
    /// <see cref="TryArithmetic"/> already refuses any interval that leaves that
    /// range, so a computed interval is kept rather than discarded for want of a
    /// type to check it against.
    /// </summary>
    internal static ManagedAbstractValue BinaryOverIrScalars(
        BinaryOperatorKind @operator, ManagedAbstractValue left, ManagedAbstractValue right)
    {
        if (@operator is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
                BinaryOperatorKind.Multiply &&
            left.TryGetInteger(out var leftInteger) &&
            right.TryGetInteger(out var rightInteger) &&
            TryArithmetic(@operator, leftInteger, rightInteger, out var result))
        {
            return Integer(result);
        }

        return Binary(@operator, left, right);
    }

    internal static ManagedAbstractValue KeepWithinType(IntervalValue value, ITypeSymbol? type)
    {
        return FitsType(value, type) ? Integer(value) : TopForType(type);
    }

    internal static bool FitsType(IntervalValue value, ITypeSymbol? type)
    {
        return IntegerType(type, out var semantics) &&
        value.LowerBound >= semantics.Minimum && value.UpperBound <= semantics.Maximum;
    }

    internal static ManagedAbstractValue Join(ManagedAbstractValue left, ManagedAbstractValue right)
    {
        if (left.IsBottom)
        {
            return right;
        }

        if (right.IsBottom)
        {
            return left;
        }

        if (left.IsUnknown || right.IsUnknown)
        {
            return Unknown;
        }

        if (!left.Scalar.IsBottom && !right.Scalar.IsBottom)
        {
            if (left.IsBoolean != right.IsBoolean)
            {
                return Unknown;
            }

            var scalar = IntervalDomain.Instance.Join(left.Scalar, right.Scalar);
            return left.IsBoolean
                ? scalar.IsSingleton ? Boolean(scalar.SingletonValue != 0) : BooleanUnknown
                : Integer(scalar, left.ExcludesZero && right.ExcludesZero);
        }
        if (left.Nullness == NullnessValue.Bottom || right.Nullness == NullnessValue.Bottom)
        {
            return Unknown;
        }

        var nullness = NullnessDomain.Instance.Join(left.Nullness, right.Nullness);
        if (!left.Cardinality.IsBottom && !right.Cardinality.IsBottom)
        {
            return Reference(nullness, IntervalDomain.Instance.Join(left.Cardinality, right.Cardinality));
        }

        if (left.Nullness == NullnessValue.Null && !right.Cardinality.IsBottom)
        {
            return Reference(nullness, right.Cardinality);
        }

        if (right.Nullness == NullnessValue.Null && !left.Cardinality.IsBottom)
        {
            return Reference(nullness, left.Cardinality);
        }

        return Reference(nullness);
    }

    private static ManagedAbstractValue Equality(
        ManagedAbstractValue left, ManagedAbstractValue right, bool negate, ManagedAbstractValue unknown)
    {
        bool? equal = null;
        if (left.TryGetBoolean(out var leftBoolean) && right.TryGetBoolean(out var rightBoolean))
        {
            equal = leftBoolean == rightBoolean;
        }
        else if (left.TryGetInteger(out var leftInteger) && right.TryGetInteger(out var rightInteger))
        {
            if (leftInteger.IsSingleton && rightInteger.IsSingleton)
            {
                equal = leftInteger.SingletonValue == rightInteger.SingletonValue;
            }
            else if (Disjoint(leftInteger, rightInteger))
            {
                equal = false;
            }
        }
        else if (left.TryGetNullness(out var leftNullness) && right.TryGetNullness(out var rightNullness))
        {
            if (leftNullness == NullnessValue.Null && rightNullness == NullnessValue.Null)
            {
                equal = true;
            }
            else if (leftNullness == NullnessValue.Null && rightNullness == NullnessValue.NonNull ||
                     rightNullness == NullnessValue.Null && leftNullness == NullnessValue.NonNull)
            {
                equal = false;
            }
        }
        return equal is bool established
            ? Boolean(negate != established)
            : unknown;
    }

    private static ManagedAbstractValue Compare(
        IntervalValue left, IntervalValue right, BinaryOperatorKind @operator, ManagedAbstractValue unknown)
    {
        bool? value = @operator switch
        {
            BinaryOperatorKind.LessThan when left.UpperBound < right.LowerBound => true,
            BinaryOperatorKind.LessThan when left.LowerBound >= right.UpperBound => false,
            BinaryOperatorKind.LessThanOrEqual when left.UpperBound <= right.LowerBound => true,
            BinaryOperatorKind.LessThanOrEqual when left.LowerBound > right.UpperBound => false,
            BinaryOperatorKind.GreaterThan when left.LowerBound > right.UpperBound => true,
            BinaryOperatorKind.GreaterThan when left.UpperBound <= right.LowerBound => false,
            BinaryOperatorKind.GreaterThanOrEqual when left.LowerBound >= right.UpperBound => true,
            BinaryOperatorKind.GreaterThanOrEqual when left.UpperBound < right.LowerBound => false,
            _ => null
        };
        return value.HasValue ? Boolean(value.Value) : unknown;
    }

    private static bool Disjoint(IntervalValue left, IntervalValue right)
    {
        return left.UpperBound.HasValue && right.LowerBound.HasValue && left.UpperBound.Value < right.LowerBound.Value ||
        right.UpperBound.HasValue && left.LowerBound.HasValue && right.UpperBound.Value < left.LowerBound.Value;
    }

    internal static bool IntegerType(ITypeSymbol? type, out CSharpIntegerSemantics semantics)
    {
        return CSharpScalarSemantics.TryGetInteger(type?.SpecialType ?? SpecialType.None, out semantics);
    }

    internal static bool IsNullableType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        };
    }
}

/// <summary>Fail-closed execution facts shared by analyzer and effect witnesses.</summary>
internal sealed class DefiniteOperationFacts(Compilation compilation, CancellationToken cancellationToken)
{
    // Explicit comparer: this set is the cycle guard for CompletesNormally, so
    // if it ever degraded to reference equality the failure mode would be
    // unbounded recursion rather than a wrong answer.
    private readonly HashSet<IMethodSymbol> _activeMethods =
        new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _contractApi =
        ContractApiIdentityResolver.ForCompilation(compilation).Contract;

    internal bool CompletesNormally(IOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            null => false,
            ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or
                IDiscardOperation or IInstanceReferenceOperation or IDefaultValueOperation or
                ITypeOfOperation or INameOfOperation => true,
            IInvocationOperation invocation => CompletesNormally(invocation),
            IObjectCreationOperation creation =>
                creation.Arguments.All(argument =>
                    CompletesNormally(argument.Value)) &&
                (creation.Constructor == null ||
                 creation.Constructor.DeclaringSyntaxReferences.Length != 1 ||
                 CompletesNormally(creation.Constructor)),
            IMethodReferenceOperation methodReference =>
                ChildrenCompleteNormally(methodReference) &&
                (methodReference.Method.IsStatic ||
                 methodReference.Instance != null &&
                 IsDefinitelyNonNull(methodReference.Instance)),
            IFieldReferenceOperation fieldReference =>
                ChildrenCompleteNormally(fieldReference) &&
                (fieldReference.Field.IsStatic ||
                 fieldReference.Instance != null &&
                 IsDefinitelyNonNull(fieldReference.Instance)),
            ISimpleAssignmentOperation assignment =>
                assignment.Target is ILocalReferenceOperation or IParameterReferenceOperation or IDiscardOperation &&
                CompletesNormally(assignment.Value),
            IBinaryOperation binary =>
                binary.OperatorMethod == null && !binary.IsChecked &&
                binary.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) &&
                ChildrenCompleteNormally(binary),
            IUnaryOperation unary =>
                unary.OperatorMethod == null && !unary.IsChecked && ChildrenCompleteNormally(unary),
            IIncrementOrDecrementOperation increment =>
                increment.OperatorMethod == null && !increment.IsChecked &&
                increment.Target is ILocalReferenceOperation or IParameterReferenceOperation,
            IConversionOperation conversion =>
                HarmlessConversion(conversion) &&
                CompletesNormally(conversion.Operand),
            IBlockOperation or IExpressionStatementOperation or IReturnOperation or
                IVariableDeclarationGroupOperation or IVariableDeclarationOperation or
                IVariableDeclaratorOperation or IVariableInitializerOperation or IArgumentOperation or
                IParenthesizedOperation or IConditionalOperation => ChildrenCompleteNormally(operation),
            _ => false
        };
    }

    private bool CompletesNormally(IInvocationOperation invocation)
    {
        return IsContractClause(invocation) ||
        !invocation.IsVirtual &&
        (invocation.Instance == null ||
         invocation.Instance is IInstanceReferenceOperation && CompletesNormally(invocation.Instance)) &&
        invocation.Arguments.All(argument => CompletesNormally(argument.Value)) &&
        CompletesNormally(invocation.TargetMethod);
    }

    private bool CompletesNormally(IMethodSymbol method)
    {
        if (method.IsStatic && method.ContainingType.StaticConstructors.Length != 0)
        {
            return false;
        }

        var normalized = method.OriginalDefinition;
        if (normalized.DeclaringSyntaxReferences.Length != 1 || !_activeMethods.Add(normalized))
        {
            return false;
        }

        try
        {
            var body = ExecutableBodySyntax.Get(
                normalized.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken));
            if (body == null)
            {
                return false;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, body.SyntaxTree);
            return CompletesNormally(model.GetOperation(body, cancellationToken));
        }
        finally
        {
            _activeMethods.Remove(normalized);
        }
    }

    /// <summary>
    /// Returns whether a source method has a reachable normal exit.  This is
    /// intentionally a control-flow fact rather than a may-throw fact: a
    /// method with both a throwing and a returning branch can still permit the
    /// caller's next source-order step. Async and iterator bodies execute
    /// behind a deferred call boundary, so their body termination cannot make
    /// the invocation itself noncompleting.
    /// </summary>
    internal bool MethodCanCompleteNormally(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = method.OriginalDefinition;
        var isImplicitConstructor = EffectMethodNodeBuilder
            .IsSourceImplicitParameterlessConstructor(normalized);
        if (!isImplicitConstructor &&
            normalized.DeclaringSyntaxReferences.Length != 1)
        {
            return true;
        }
        if (!isImplicitConstructor &&
            HasUnconditionalSelfInvocation(normalized))
        {
            return false;
        }
        if (!_activeMethods.Add(normalized))
        {
            // This is a may-complete query. Recursive re-entry is uncertainty,
            // not evidence that every invocation is nonreturning.
            return true;
        }

        try
        {
            if (isImplicitConstructor)
            {
                return ImplicitConstructorMayCompleteNormally(normalized);
            }

            var declaration = normalized.DeclaringSyntaxReferences[0]
                .GetSyntax(cancellationToken);
            if (DefersBodyCompletion(normalized, declaration))
            {
                return true;
            }
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, declaration.SyntaxTree);
            var operation = model.GetOperation(declaration, cancellationToken) ??
                (ExecutableBodySyntax.Get(declaration) is { } methodBody
                    ? model.GetOperation(methodBody, cancellationToken)
                    : null);
            if (operation == null)
            {
                return true;
            }
            return normalized.MethodKind == MethodKind.Constructor &&
                operation is IConstructorBodyOperation constructorBody
                ? ConstructorMayCompleteNormally(
                    normalized,
                    constructorBody)
                : MayCompleteNormally(operation);
        }
        catch (ArgumentException)
        {
            return true;
        }
        finally
        {
            _activeMethods.Remove(normalized);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1508:Avoid dead conditional code",
        Justification = "The analyzer does not track the nullable expression " +
            "selected from the declaration syntax.")]
    private bool HasUnconditionalSelfInvocation(IMethodSymbol method)
    {
        try
        {
            var declaration = method.DeclaringSyntaxReferences[0]
                .GetSyntax(cancellationToken);
            ExpressionSyntax? expression = declaration switch
            {
                MethodDeclarationSyntax
                { ExpressionBody.Expression: { } body } => body,
                MethodDeclarationSyntax
                { Body.Statements.Count: 1 } body when
                    body.Body!.Statements[0] is ExpressionStatementSyntax
                    { Expression: { } statement } => statement,
                _ => null
            };
            if (expression == null)
            {
                return false;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, expression.SyntaxTree);
            return model.GetOperation(expression, cancellationToken) is
                IInvocationOperation invocation &&
                SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    method.OriginalDefinition);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool ImplicitConstructorMayCompleteNormally(
        IMethodSymbol constructor)
    {
        if (constructor.ContainingType.IsValueType)
        {
            return true;
        }

        var baseConstructor = EffectMethodNodeBuilder
            .GetUniqueParameterlessBaseConstructor(constructor);
        return baseConstructor != null &&
            MethodCanCompleteNormally(baseConstructor);
    }

    private bool ConstructorMayCompleteNormally(
        IMethodSymbol constructor,
        IConstructorBodyOperation body)
    {
        if (!MayCompleteNormally(body.Initializer))
        {
            return false;
        }

        var initializer = EffectMethodNodeBuilder
            .GetConstructorInitializerInvocation(body);
        var delegatesToThis = initializer != null &&
            SymbolEqualityComparer.Default.Equals(
                initializer.TargetMethod.ContainingType.OriginalDefinition,
                constructor.ContainingType.OriginalDefinition);
        if (!delegatesToThis)
        {
            foreach (var operation in EffectMethodNodeBuilder
                         .GetMemberInitializerOperations(
                             compilation,
                             constructor.ContainingType,
                             staticInitializers: false,
                             cancellationToken))
            {
                if (operation != null && !MayCompleteNormally(operation))
                {
                    return false;
                }
            }
        }

        return MayCompleteNormally(body.BlockBody) &&
            MayCompleteNormally(body.ExpressionBody);
    }

    private static bool DefersBodyCompletion(
        IMethodSymbol method,
        SyntaxNode declaration)
    {
        if (method.IsAsync)
        {
            return true;
        }

        var body = ExecutableBodySyntax.Get(declaration);
        return body != null && body.DescendantNodesAndSelf(
                descendIntoChildren: static node =>
                    node is not AnonymousFunctionExpressionSyntax and
                    not LocalFunctionStatementSyntax)
            .Any(static node => node is YieldStatementSyntax);
    }

    /// <summary>
    /// Computes a deliberately permissive normal-completion fact.  This is
    /// used only to suppress effects after a call that is proven never to
    /// return, so uncertainty must retain the later effects.  In particular,
    /// ordinary assignments, writes, external calls, and unsupported shapes
    /// are all treated as potentially completing.
    /// </summary>
    internal bool MayCompleteNormally(IOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            null => true,
            IThrowOperation => false,
            IReturnOperation returnOperation =>
                ChildrenMayCompleteNormally(returnOperation),
            IMethodBodyOperation body =>
                SequenceMayCompleteNormally(body.ChildOperations),
            IConstructorBodyOperation body =>
                SequenceMayCompleteNormally(body.ChildOperations),
            IBlockOperation block =>
                SequenceMayCompleteNormally(block.ChildOperations),
            IConditionalOperation conditional =>
                MayCompleteNormally(conditional.Condition) &&
                (MayCompleteNormally(conditional.WhenTrue) ||
                 MayCompleteNormally(conditional.WhenFalse)),
            IConditionalAccessOperation conditionalAccess =>
                MayCompleteNormally(conditionalAccess.Operation) &&
                (MayCompleteNormally(conditionalAccess.WhenNotNull) ||
                 !DefiniteOperationFacts.IsDefinitelyNonNull(
                     conditionalAccess.Operation)),
            ISwitchExpressionOperation switchExpression =>
                MayCompleteSwitchExpression(switchExpression),
            ISwitchExpressionArmOperation arm =>
                MayCompleteNormally(arm.Pattern) &&
                (arm.Guard == null || MayCompleteNormally(arm.Guard)) &&
                MayCompleteNormally(arm.Value),
            IIsPatternOperation isPattern =>
                MayCompleteNormally(isPattern.Value) &&
                MayCompleteNormally(isPattern.Pattern),
            IPropertySubpatternOperation propertySubpattern =>
                MayCompleteNormally(propertySubpattern.Member) &&
                MayCompleteNormally(propertySubpattern.Pattern),
            IBinaryPatternOperation binaryPattern =>
                MayCompleteBinaryPattern(binaryPattern),
            IListPatternOperation listPattern =>
                MayCompleteListPattern(listPattern),
            IRecursivePatternOperation recursivePattern =>
                MayCompleteRecursivePattern(recursivePattern),
            IPatternOperation pattern =>
                ChildrenMayCompleteNormally(pattern),
            ICoalesceOperation coalesce =>
                MayCompleteNormally(coalesce.Value) &&
                (!IsDefinitelyNull(coalesce.Value) ||
                 MayCompleteNormally(coalesce.WhenNull)),
            IInvocationOperation invocation =>
                InvocationMayCompleteNormally(invocation),
            IAnonymousObjectCreationOperation or
                IDelegateCreationOperation =>
                ChildrenMayCompleteNormally(operation),
            IMethodReferenceOperation methodReference =>
                ChildrenMayCompleteNormally(methodReference) &&
                (methodReference.Method.IsStatic ||
                 methodReference.Instance == null ||
                 !IsDefinitelyNull(methodReference.Instance)),
            IObjectCreationOperation creation =>
                CreationMayCompleteNormally(creation),
            IArrayCreationOperation array =>
                ChildrenMayCompleteNormally(array),
            ILockOperation @lock =>
                MayCompleteNormally(@lock.LockedValue) &&
                MayCompleteNormally(@lock.Body),
            IBinaryOperation binary =>
                BinaryMayCompleteNormally(binary),
            IUnaryOperation or IConversionOperation or
                IIncrementOrDecrementOperation or ICompoundAssignmentOperation or
                ISimpleAssignmentOperation or IArrayElementReferenceOperation or
                IFieldReferenceOperation or
                IFlowCaptureOperation or IParenthesizedOperation or
                IArgumentOperation =>
                ChildrenMayCompleteNormally(operation),
            IPropertyReferenceOperation property =>
                ChildrenMayCompleteNormally(property) &&
                (property.Property.IsStatic ||
                 property.Instance == null ||
                 !IsDefinitelyNull(property.Instance)) &&
                (property.Property.GetMethod == null ||
                 MethodCanCompleteNormally(property.Property.GetMethod)),
            IObjectOrCollectionInitializerOperation initializer =>
                SequenceMayCompleteNormally(initializer.ChildOperations),
            IExpressionStatementOperation or
                IVariableDeclarationGroupOperation or
                IVariableDeclarationOperation or
                IVariableDeclaratorOperation or
                IVariableInitializerOperation =>
                ChildrenMayCompleteNormally(operation),
            ILabeledOperation labeled =>
                ChildrenMayCompleteNormally(labeled),
            ILoopOperation loop when
                LoopConditionIsAlwaysTrue(loop) &&
                loop.Body != null &&
                !LoopHasReachableExit(loop) => false,
            ITryOperation @try => TryMayCompleteNormally(@try),
            ILoopOperation or ISwitchOperation => true,
            _ => true
        };
    }

    private bool MayCompleteBinaryPattern(
        IBinaryPatternOperation pattern)
    {
        if (!MayCompleteNormally(pattern.LeftPattern))
        {
            return false;
        }

        var input = SwitchExpressionFacts.GetGoverningValue(pattern);
        var leftSelection =
            SwitchExpressionFacts.GetPatternSelectionForUnknownValue(
                pattern.LeftPattern,
                pattern.LeftPattern.InputType,
                input != null && IsDefinitelyNonNull(input));
        var rightIsRequired =
            pattern.OperatorKind == BinaryOperatorKind.And &&
            leftSelection == SwitchExpressionSelection.Always ||
            pattern.OperatorKind == BinaryOperatorKind.Or &&
            leftSelection == SwitchExpressionSelection.Never;
        return !rightIsRequired ||
            MayCompleteNormally(pattern.RightPattern);
    }

    private bool MayCompleteSwitchExpression(
        ISwitchExpressionOperation switchExpression)
    {
        if (!MayCompleteNormally(switchExpression.Value))
        {
            return false;
        }

        return SwitchExpressionFacts.GetReachableArms(
                switchExpression,
                MayCompleteNormally,
                IsDefinitelyNonNull(switchExpression.Value))
            .Any(MayCompleteNormally);
    }

    private bool TryMayCompleteNormally(ITryOperation @try)
    {
        if (@try.Finally != null && !MayCompleteNormally(@try.Finally))
        {
            return false;
        }

        return MayCompleteNormally(@try.Body) ||
            @try.Catches.Any(catchClause =>
                (catchClause.Filter == null ||
                 MayCompleteNormally(catchClause.Filter)) &&
                MayCompleteNormally(catchClause.Handler));
    }

    private bool MayCompleteRecursivePattern(
        IRecursivePatternOperation pattern)
    {
        if (HasNullableNullMismatchPath(pattern))
        {
            return true;
        }
        if (pattern.DeconstructSymbol is IMethodSymbol deconstruct &&
            !MethodCanCompleteNormally(deconstruct))
        {
            return false;
        }
        return pattern.DeconstructionSubpatterns.All(MayCompleteNormally) &&
            pattern.PropertySubpatterns.All(MayCompleteNormally);
    }

    private bool MayCompleteListPattern(IListPatternOperation pattern)
    {
        var value = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (HasNullableNullMismatchPath(pattern, value))
        {
            return true;
        }
        if (pattern.InputType?.IsValueType != true &&
            value?.Syntax.ToString().IndexOf(
                "null",
                StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        var lengthMethod =
            SwitchExpressionFacts.GetCallableListPatternMember(
                pattern.LengthSymbol);
        if (lengthMethod != null &&
            !MethodCanCompleteNormally(lengthMethod))
        {
            return false;
        }

        if (TryGetListPatternLength(pattern, out var length))
        {
            var (requiredLength, hasSlice) =
                SwitchExpressionFacts.GetListPatternShape(pattern);
            if (SwitchExpressionFacts.HasListPatternLengthMismatch(
                    requiredLength,
                    hasSlice,
                    length))
            {
                return true;
            }
        }

        foreach (var item in pattern.Patterns)
        {
            var method = SwitchExpressionFacts.GetCallableListPatternMember(
                pattern,
                item);
            if (method != null && !MethodCanCompleteNormally(method))
            {
                return false;
            }
            var nested = item is ISlicePatternOperation nestedSlice
                ? nestedSlice.Pattern
                : item;
            if (nested != null && !MayCompleteNormally(nested))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasNullableNullMismatchPath(
        IPatternOperation pattern,
        IOperation? value = null)
    {
        value ??= SwitchExpressionFacts.GetGoverningValue(pattern);
        return (ManagedAbstractValue.IsNullableType(pattern.InputType) ||
                ManagedAbstractValue.IsNullableType(value?.Type)) &&
            (value == null || !IsDefinitelyNonNull(value));
    }

    private bool TryGetListPatternLength(
        IListPatternOperation pattern,
        out long length)
    {
        var value = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (ArrayLengthFacts.TryGetConstantLength(value, out var arrayLength))
        {
            length = arrayLength;
            return true;
        }

        if (pattern.LengthSymbol is IPropertySymbol
            { GetMethod: { } getter } &&
            getter.DeclaringSyntaxReferences.Length == 1)
        {
            var syntax = getter.DeclaringSyntaxReferences[0].GetSyntax();
            ExpressionSyntax? expression = syntax switch
            {
                PropertyDeclarationSyntax property
                    when property.ExpressionBody != null =>
                    property.ExpressionBody.Expression,
                AccessorDeclarationSyntax accessor
                    when accessor.ExpressionBody != null =>
                    accessor.ExpressionBody.Expression,
                _ => null
            };
            if (expression is { } constantExpression)
            {
                var constant = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(
                        compilation,
                        constantExpression.SyntaxTree)
                    .GetConstantValue(constantExpression);
                if (constant is { HasValue: true, Value: int constantLength })
                {
                    length = constantLength;
                    return length >= 0;
                }
            }
        }
        length = 0;
        return false;
    }

    private static bool LoopConditionIsAlwaysTrue(ILoopOperation loop)
    {
        return loop switch
        {
            IWhileLoopOperation
            {
                ConditionIsUntil: false,
                Condition.ConstantValue: { HasValue: true, Value: true }
            } => true,
            IForLoopOperation { Condition: null } => true,
            IForLoopOperation
            {
                Condition.ConstantValue: { HasValue: true, Value: true }
            } => true,
            _ => false
        };
    }

    private bool LoopHasReachableExit(ILoopOperation loop)
    {
        return HasReachableExit(loop.Body);

        bool HasReachableExit(IOperation operation)
        {
            if (operation is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                return false;
            }

            if (operation is IReturnOperation)
            {
                return MandatoryFinallysMayComplete(operation);
            }

            if (operation is IBranchOperation branch &&
                (SymbolEqualityComparer.Default.Equals(
                     branch.Target,
                     loop.ExitLabel) ||
                 IsOutwardGoto(branch)))
            {
                return MandatoryFinallysMayComplete(branch);
            }

            return operation.ChildOperations.Any(HasReachableExit);
        }

        bool IsOutwardGoto(IBranchOperation branch)
        {
            return branch.BranchKind == BranchKind.GoTo &&
                branch.Target.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == loop.Syntax.SyntaxTree &&
                    !loop.Syntax.Span.Contains(reference.Span));
        }

        bool MandatoryFinallysMayComplete(IOperation exit)
        {
            for (var parent = exit.Parent;
                 parent != null && !ReferenceEquals(parent, loop);
                 parent = parent.Parent)
            {
                if (parent is ITryOperation { Finally: { } @finally } &&
                    !MayCompleteNormally(@finally))
                {
                    return false;
                }
            }
            return true;
        }
    }

    private bool InvocationMayCompleteNormally(IInvocationOperation invocation)
    {
        if (!MayCompleteNormally(invocation.Instance) ||
            invocation.Arguments.Any(argument =>
                !MayCompleteNormally(argument.Value)))
        {
            return false;
        }

        if (!invocation.TargetMethod.IsStatic &&
            invocation.TargetMethod.ReducedFrom == null &&
            invocation.Instance != null &&
            IsDefinitelyNull(invocation.Instance))
        {
            return false;
        }

        var target = invocation.TargetMethod.OriginalDefinition;
        return invocation.IsVirtual &&
            target.ContainingType?.IsSealed != true &&
            !target.IsSealed ||
            !HasSourceCompletionFlow(target) ||
            MethodCanCompleteNormally(target);
    }

    private bool CreationMayCompleteNormally(IObjectCreationOperation creation)
    {
        if (creation.Arguments.Any(argument =>
                !MayCompleteNormally(argument.Value)))
        {
            return false;
        }

        if (creation.Constructor is { } constructor &&
            HasSourceCompletionFlow(constructor) &&
            !MethodCanCompleteNormally(constructor))
        {
            return false;
        }

        return creation.Initializer == null ||
            MayCompleteNormally(creation.Initializer);
    }

    internal static bool HasSourceCompletionFlow(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        return method.DeclaringSyntaxReferences.Length != 0 ||
            EffectMethodNodeBuilder
                .IsSourceImplicitParameterlessConstructor(method);
    }

    private bool SequenceMayCompleteNormally(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (ManagedAbstractFlow.IsCompileTimeUnreachable(
                    compilation,
                    operation))
            {
                continue;
            }

            if (!MayCompleteNormally(operation))
            {
                return false;
            }
        }

        return true;
    }

    private bool ChildrenMayCompleteNormally(IOperation operation)
    {
        return operation.ChildOperations.All(MayCompleteNormally);
    }

    private bool BinaryMayCompleteNormally(IBinaryOperation binary)
    {
        if (!MayCompleteNormally(binary.LeftOperand) ||
            IsDefinitelyZeroDivision(binary))
        {
            return false;
        }

        if (ConversionEffectClassifier.SkipsLiftedOperator(
                binary,
                flow: null))
        {
            return ChildrenMayCompleteNormally(binary);
        }

        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or
                BinaryOperatorKind.ConditionalOr &&
            binary.OperatorMethod != null)
        {
            var truthOperator = ConditionalTruthOperatorFacts.Resolve(binary);
            if (truthOperator != null &&
                !MethodCanCompleteNormally(truthOperator))
            {
                return false;
            }

            if (truthOperator == null ||
                !ConditionalTruthOperatorFacts.ReturnsConstant(
                    compilation,
                    truthOperator,
                    out var truthResult))
            {
                // An unknown truth result retains the short-circuit path.
                return true;
            }

            return truthResult ||
                MayCompleteNormally(binary.RightOperand) &&
                MethodCanCompleteNormally(binary.OperatorMethod);
        }

        return MayCompleteNormally(binary.RightOperand) &&
            (binary.OperatorMethod == null ||
             MethodCanCompleteNormally(binary.OperatorMethod));
    }

    private static bool IsDefinitelyZeroDivision(IBinaryOperation binary)
    {
        return binary.OperatorKind is BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder &&
            binary.RightOperand.ConstantValue is { HasValue: true, Value: 0 };
    }

    private bool ChildrenCompleteNormally(IOperation operation)
    {
        return operation.ChildOperations.All(CompletesNormally);
    }

    private bool IsContractClause(IInvocationOperation invocation)
    {
        return invocation.TargetMethod is
        {
            IsStatic: true,
            Name: ContractApiCatalog.RequiresMethodName or
                ContractApiCatalog.EnsuresMethodName or
                ContractApiCatalog.AssumeMethodName
        } method &&
        _contractApi != null &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, _contractApi.OriginalDefinition) &&
        invocation.Arguments.All(argument => CompletesNormally(argument.Value));
    }

    internal static bool IsHarmlessValue(IOperation operation)
    {
        operation = UnwrapHarmlessWrappers(operation);
        return operation is
            ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or
            IInstanceReferenceOperation or IDefaultValueOperation or ITypeOfOperation or
            INameOfOperation or ISizeOfOperation;
    }

    private static IOperation UnwrapHarmlessWrappers(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion when HarmlessConversion(conversion):
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    internal static bool IsDirectArrayCreationComplete(
        IArrayCreationOperation creation)
    {
        return creation.DimensionSizes.All(static size =>
            size.ConstantValue is { HasValue: true, Value: int length } &&
            length >= 0) &&
        creation.Initializer?.ElementValues.All(IsHarmlessValue) != false;
    }

    internal static IOperation UnwrapHarmlessValue(IOperation operation)
    {
        return UnwrapHarmlessWrappers(operation);
    }

    /// <summary>
    /// Reports whether the value is certainly a string, including a string that
    /// has been converted to a wider reference type. Callers use this to decide
    /// that a cast back to <c>string</c> cannot fail, so both call-site and
    /// effect-precondition analysis must agree on it.
    /// </summary>
    internal static bool IsDefinitelyString(IOperation operation)
    {
        operation = UnwrapHarmlessValue(operation);
        return operation.Type?.SpecialType == SpecialType.System_String ||
            operation is IConversionOperation
            {
                Operand.Type.SpecialType: SpecialType.System_String
            };
    }

    internal static bool IsDefinitelyNonNull(IOperation operation)
    {
        operation = UnwrapSimpleConversions(operation);
        return operation is IInstanceReferenceOperation or IConditionalAccessInstanceOperation or
            IObjectCreationOperation or IArrayCreationOperation or ITypeOfOperation ||
            operation.ConstantValue is { HasValue: true, Value: not null };
    }

    internal static bool IsDefinitelyNull(IOperation operation)
    {
        operation = UnwrapSimpleConversions(operation);
        return operation.ConstantValue is { HasValue: true, Value: null };
    }

    private static IOperation UnwrapSimpleConversions(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation)
        {
            if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else if (operation is IConversionOperation
            {
                OperatorMethod: null,
                IsTryCast: false
            } conversion)
            {
                operation = conversion.Operand;
            }
            else
            {
                break;
            }
        }
        return operation;
    }

    private static bool HarmlessConversion(IConversionOperation conversion)
    {
        return conversion.OperatorMethod == null &&
        !conversion.IsChecked &&
        !conversion.Conversion.IsUserDefined &&
        (conversion.Conversion.IsIdentity ||
         conversion.Conversion.IsImplicit) &&
        conversion.Operand.Type?.TypeKind != TypeKind.Dynamic &&
        conversion.Type?.TypeKind != TypeKind.Dynamic &&
        !(conversion.Operand.Type?.IsValueType is true && conversion.Type?.IsReferenceType is true);
    }

}
