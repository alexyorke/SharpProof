using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

/// <summary>
/// Bounded scalar facts computed by the shared deterministic dataflow engine.
/// Unsupported, over-budget, and cyclic bodies return an explicit incomplete result.
/// </summary>
internal sealed class ManagedAbstractFlow
{
    internal const int MaxAnalyzedBlocks = 256;
    internal const int MaxAnalyzedOperations = 4096;
    private static readonly ConditionalWeakTable<Compilation, ManagedAbstractFlow> Sessions = new();
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly INamedTypeSymbol? _contractApi;
    private readonly INamedTypeSymbol? _inRangeAttribute;
    private readonly INamedTypeSymbol? _notNullAttribute;
    private readonly INamedTypeSymbol? _positiveAttribute;
    private readonly TrustedBoundaryPolicy _trustedBoundaries;

    private ManagedAbstractFlow(Compilation compilation)
        : this(compilation, new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation))
    {
    }

    private ManagedAbstractFlow(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _apiSpecs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
        var contractApi = ContractApiIdentityResolver.ForCompilation(compilation);
        _contractApi = contractApi.Contract;
        _notNullAttribute = contractApi.ResolveAttribute(ContractApiMetadata.NotNull);
        _positiveAttribute = contractApi.ResolveAttribute(ContractApiMetadata.Positive);
        _inRangeAttribute = contractApi.ResolveAttribute(ContractApiMetadata.InRange);
        _trustedBoundaries =
            TrustedBoundaryPolicy.ForCompilation(compilation);
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
            var value = ManagedAbstractValue.TopForType(parameter.Type);
            if (parameter.RefKind != RefKind.Out)
            {
                value = ApplyAttributes(value, parameter.GetAttributes());
            }

            state = state.Set(parameter, value);
        }
        return state;
    }

    internal ManagedFlowAnalysis Analyze(
        IMethodSymbol method, ControlFlowGraph graph, ManagedFlowState? entryState, CancellationToken cancellationToken)
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
        _ = ForwardDataflowAnalysis.Analyze(CreateDataflowGraph(graph, result, cancellationToken),
            FlowDomain.Instance, entryState ?? CreateEntryState(method),
            new ForwardDataflowAnalysisOptions(maxIterations: MaxAnalyzedBlocks * 4));
        return ManagedFlowAnalysis.Complete(result);
    }

    internal ManagedAbstractValue Evaluate(IOperation operation, ManagedFlowState state)
    {
        return EvaluateCore(
            ArgumentNullGuard.NotNull(operation, nameof(operation)),
            ArgumentNullGuard.NotNull(state, nameof(state)));
    }

    internal static ManagedFlowState Refine(
        ManagedFlowState state, ISymbol symbol, BinaryOperatorKind @operator, ManagedAbstractValue value, bool expected)
    {
        return Refine(state, (object)symbol, @operator, value, expected);
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
                blocks.Add(new(edgeBlock, state => expected.HasValue && condition != null
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

        cancellationToken.ThrowIfCancellationRequested();
        switch (operation)
        {
            case IAnonymousFunctionOperation or ILocalFunctionOperation:
                break;
            case IVariableDeclaratorOperation declarator:
                if (declarator.Initializer == null)
                {
                    state = state.Set(declarator.Symbol, ManagedAbstractValue.TopForType(declarator.Symbol.Type));
                }
                else
                {
                    state = Transfer(state, declarator.Initializer.Value, result, cancellationToken);
                    state = state.Set(declarator.Symbol, EvaluateCore(declarator.Initializer.Value, state));
                }
                break;
            case IFlowCaptureOperation capture:
                state = Transfer(state, capture.Value, result, cancellationToken);
                state = state.Set(capture.Id, EvaluateCore(capture.Value, state));
                break;
            case ISimpleAssignmentOperation assignment:
                state = TransferMany(state, assignment.ChildOperations, result, cancellationToken);
                state = SetStorage(state, assignment.Target, EvaluateCore(assignment.Value, state));
                break;
            case ICompoundAssignmentOperation compound:
                state = TransferMany(state, compound.ChildOperations, result, cancellationToken);
                state = SetStorage(state, compound.Target, ManagedAbstractValue.TopForType(compound.Type));
                break;
            case IIncrementOrDecrementOperation increment:
                state = Transfer(state, increment.Target, result, cancellationToken);
                state = SetStorage(state, increment.Target, Increment(increment, state));
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
        var prior = EvaluateCore(operation.Target, state);
        var @operator = operation.Kind == OperationKind.Increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;
        return prior.TryGetInteger(out var interval) && ManagedAbstractValue.TryArithmetic(
                   @operator, interval, IntervalValue.Constant(1), out var updated) &&
               ManagedAbstractValue.FitsType(updated, operation.Type)
            ? ManagedAbstractValue.Integer(updated)
            : ManagedAbstractValue.TopForType(operation.Type);
    }

    private ManagedFlowState Assume(ManagedFlowState state, IOperation condition, bool expected)
    {
        condition = Unwrap(condition);
        if (EvaluateCore(condition, state).TryGetBoolean(out var constant))
        {
            return constant == expected ? state : ManagedFlowState.Bottom;
        }

        return condition switch
        {
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary =>
                Assume(state, unary.Operand, !expected),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } binary when expected =>
                Assume(Assume(state, binary.LeftOperand, true), binary.RightOperand, true),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } binary when !expected =>
                Assume(Assume(state, binary.LeftOperand, false), binary.RightOperand, false),
            IBinaryOperation binary => AssumeComparison(state, binary.LeftOperand, binary.RightOperand,
                binary.OperatorKind, expected),
            IIsNullOperation isNull when TryStorage(isNull.Operand, out var storage) =>
                Refine(state, storage, BinaryOperatorKind.Equals, ManagedAbstractValue.Null, expected),
            IIsPatternOperation pattern => AssumeNullPattern(state, pattern, expected),
            _ when TryStorage(condition, out var storage) =>
                state.Set(storage, ManagedAbstractValue.Boolean(expected)),
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
            ? Refine(state, storage, BinaryOperatorKind.Equals, ManagedAbstractValue.Null, expected != negated)
            : state;
    }

    private static ManagedFlowState Refine(
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
            return state.Set(storage, ManagedAbstractValue.Boolean(
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

        var equals = @operator == BinaryOperatorKind.Equals ? expected
            : @operator == BinaryOperatorKind.NotEquals ? !expected : (bool?)null;
        if (!equals.HasValue)
        {
            return state;
        }

        var refined = equals.Value ? NullnessDomain.Instance.AssumeNull(nullness)
            : NullnessDomain.Instance.AssumeNonNull(nullness);
        return state.Set(storage, ManagedAbstractValue.Reference(refined, current.Cardinality));
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
            ? ManagedAbstractValue.Bottom
            : ManagedAbstractValue.Integer(
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
        operation = Unwrap(operation);
        if (operation.ConstantValue.HasValue)
        {
            return ManagedAbstractValue.FromConstant(operation.ConstantValue.Value, operation.Type);
        }

        return operation switch
        {
            IParameterReferenceOperation parameter => state.Get(parameter.Parameter),
            ILocalReferenceOperation local => state.Get(local.Local),
            IFlowCaptureReferenceOperation capture => state.Get(capture.Id),
            IDefaultValueOperation value => ManagedAbstractValue.DefaultForType(value.Type),
            IInstanceReferenceOperation or IConditionalAccessInstanceOperation or IObjectCreationOperation or
                ITypeOfOperation => ManagedAbstractValue.NonNull,
            IArrayCreationOperation array => EvaluateArray(array, state),
            IPropertyReferenceOperation property => EvaluateProperty(property, state),
            IInvocationOperation invocation => ReturnValue(invocation.TargetMethod, invocation.Type),
            IIsNullOperation isNull => NullTest(isNull, state),
            IConversionOperation conversion => ConvertValue(conversion, state),
            IUnaryOperation unary => EvaluateUnary(unary, state),
            IBinaryOperation binary => ManagedAbstractValue.Binary(binary.OperatorKind,
                EvaluateCore(binary.LeftOperand, state), EvaluateCore(binary.RightOperand, state), binary.Type),
            IConditionalOperation conditional => EvaluateConditional(conditional, state),
            ICoalesceOperation coalesce => EvaluateCoalesce(coalesce, state),
            ISimpleAssignmentOperation assignment => EvaluateCore(assignment.Value, state),
            IFlowCaptureOperation capture => EvaluateCore(capture.Value, state),
            _ => ManagedAbstractValue.TopForType(operation.Type)
        };
    }

    private ManagedAbstractValue EvaluateArray(IArrayCreationOperation array, ManagedFlowState state)
    {
        if (array.DimensionSizes.Length != 1 ||
            !EvaluateCore(array.DimensionSizes[0], state).TryGetInteger(out var size))
        {
            return ManagedAbstractValue.NonNull;
        }

        return ManagedAbstractValue.Reference(NullnessValue.NonNull, IntervalDomain.Instance.AssumeAtLeast(size, 0));
    }

    private ManagedAbstractValue EvaluateProperty(IPropertyReferenceOperation property, ManagedFlowState state)
    {
        if (CompilerIdentityBridge.IsIntrinsicSequenceLength(property))
        {
            var instance = property.Instance!;
            var receiver = EvaluateCore(instance, state);
            if (receiver.TryGetCardinality(out var length))
            {
                return ManagedAbstractValue.Integer(length);
            }

            if (instance.Type is IArrayTypeSymbol ||
                instance.Type?.SpecialType == SpecialType.System_String)
            {
                return ManagedAbstractValue.Integer(IntervalValue.Range(
                    0, property.Type?.SpecialType == SpecialType.System_Int64 ? long.MaxValue : int.MaxValue));
            }
        }
        return ReturnValue(property.Property.GetMethod, property.Type);
    }

    private ManagedAbstractValue ReturnValue(IMethodSymbol? method, ITypeSymbol? type)
    {
        var value = ManagedAbstractValue.TopForType(type);
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
            return ManagedAbstractValue.Null;
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
        return ManagedAbstractValue.Reference(nullness, cardinality);
    }

    private ManagedAbstractValue NullTest(IIsNullOperation operation, ManagedFlowState state)
    {
        return EvaluateCore(operation.Operand, state).TryGetNullness(out var value)
            ? value switch
            {
                NullnessValue.Null => ManagedAbstractValue.Boolean(true),
                NullnessValue.NonNull => ManagedAbstractValue.Boolean(false),
                _ => ManagedAbstractValue.BooleanUnknown
            }
            : ManagedAbstractValue.BooleanUnknown;
    }

    private ManagedAbstractValue ConvertValue(IConversionOperation conversion, ManagedFlowState state)
    {
        var operand = EvaluateCore(conversion.Operand, state);
        if (ValuePreserving(conversion))
        {
            return operand;
        }

        return !conversion.IsTryCast && conversion.OperatorMethod == null && conversion.Conversion.IsReference &&
               operand.TryGetNullness(out var nullness)
            ? ManagedAbstractValue.Reference(nullness, operand.Cardinality)
            : ManagedAbstractValue.TopForType(conversion.Type);
    }

    private ManagedAbstractValue EvaluateUnary(IUnaryOperation unary, ManagedFlowState state)
    {
        var operand = EvaluateCore(unary.Operand, state);
        if (unary.OperatorKind == UnaryOperatorKind.Not)
        {
            return ManagedAbstractValue.NegateBoolean(operand);
        }

        return unary.OperatorKind == UnaryOperatorKind.Minus && operand.TryGetInteger(out var interval) &&
               TryNegate(interval, out var negated)
            ? ManagedAbstractValue.KeepWithinType(negated, unary.Type)
            : ManagedAbstractValue.TopForType(unary.Type);
    }

    private ManagedAbstractValue EvaluateConditional(IConditionalOperation operation, ManagedFlowState state)
    {
        if (EvaluateCore(operation.Condition, state).TryGetBoolean(out var condition))
        {
            return EvaluateCore(condition ? operation.WhenTrue : operation.WhenFalse!, state);
        }

        return operation.WhenFalse == null
            ? ManagedAbstractValue.Unknown
            : ManagedAbstractValue.Join(
                EvaluateCore(operation.WhenTrue, Assume(state, operation.Condition, true)),
                EvaluateCore(operation.WhenFalse, Assume(state, operation.Condition, false)));
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

        return ManagedAbstractValue.Join(value, EvaluateCore(operation.WhenNull, state));
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
                    !ManagedAbstractValue.TryArithmetic(binary.OperatorKind, left, right, out interval))
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
                var @operator = increment.Kind == OperationKind.Increment
                    ? BinaryOperatorKind.Add
                    : BinaryOperatorKind.Subtract;
                if (!EvaluateCore(increment.Target, state).TryGetInteger(out var target) ||
                    !ManagedAbstractValue.TryArithmetic(
                        @operator, target, IntervalValue.Constant(1), out interval))
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
        return ManagedAbstractValue.FitsType(interval, type);
    }

    private ManagedAbstractValue ApplyAttributes(
        ManagedAbstractValue value, ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (Matches(attribute, _notNullAttribute) && value.TryGetNullness(out var nullness))
            {
                value = ManagedAbstractValue.Reference(
                    NullnessDomain.Instance.AssumeNonNull(nullness), value.Cardinality);
            }
            else if (Matches(attribute, _positiveAttribute) && value.TryGetInteger(out var positive))
            {
                value = ManagedAbstractValue.Integer(IntervalDomain.Instance.AssumeAtLeast(positive, 1));
            }
            else if (Matches(attribute, _inRangeAttribute) && value.TryGetInteger(out var range) &&
                     attribute.ConstructorArguments.Length == 2 &&
                     attribute.ConstructorArguments[0].Value is long minimum &&
                     attribute.ConstructorArguments[1].Value is long maximum && minimum <= maximum)
            {
                value = ManagedAbstractValue.Integer(IntervalDomain.Instance.AssumeAtMost(
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
            Name: "Requires",
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
        return method.MethodKind == MethodKind.LocalFunction || method.ContainingType.TypeKind == TypeKind.Delegate
            ? state.Forget()
            : HavocArguments(state, arguments);
    }

    private static ManagedFlowState HavocArguments(
        ManagedFlowState state, ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                TryStorage(argument.Value, out var storage))
            {
                state = state.Set(storage, ManagedAbstractValue.TopForType(argument.Value.Type));
            }
        }

        return state;
    }

    private static ManagedFlowState SetStorage(
        ManagedFlowState state, IOperation operation, ManagedAbstractValue value)
    {
        return TryStorage(operation, out var storage) ? state.Set(storage, value) : state;
    }

    private static bool TryStorage(IOperation operation, out object storage)
    {
        operation = Unwrap(operation);
        storage = operation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            IFlowCaptureReferenceOperation capture => capture.Id,
            _ => new object()
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
        if (conversion.OperatorMethod != null || conversion.Conversion.IsUserDefined || conversion.IsTryCast)
        {
            return false;
        }

        if (conversion.Conversion.IsIdentity ||
            conversion.Conversion.IsImplicit && conversion.Conversion.IsReference)
        {
            return true;
        }

        return IntegerType(conversion.Operand.Type, out var source) &&
               IntegerType(conversion.Type, out var target) &&
               source.Minimum >= target.Minimum && source.Maximum <= target.Maximum;
    }

    internal static bool IsAcyclic(ControlFlowGraph graph)
    {
        var marks = new byte[graph.Blocks.Length];
        bool Visit(BasicBlock block)
        {
            if (marks[block.Ordinal] != 0)
            {
                return marks[block.Ordinal] == 2;
            }

            marks[block.Ordinal] = 1;
            foreach (var (branch, _) in Successors(block))
            {
                if (!Visit(branch.Destination!))
                {
                    return false;
                }
            }

            marks[block.Ordinal] = 2;
            return true;
        }
        return graph.Blocks
            .Where(static block => block.IsReachable)
            .All(Visit);
    }

    private static IEnumerable<(ControlFlowBranch Branch, bool? Expected)> Successors(BasicBlock block)
    {
        bool? expected = block.ConditionKind switch
        {
            ControlFlowConditionKind.WhenTrue => true,
            ControlFlowConditionKind.WhenFalse => false,
            _ => null
        };
        if (Regular(block.FallThroughSuccessor))
        {
            yield return (block.FallThroughSuccessor!, expected.HasValue ? !expected : null);
        }

        if (Regular(block.ConditionalSuccessor))
        {
            yield return (block.ConditionalSuccessor!, expected);
        }
    }

    private static bool Regular(ControlFlowBranch? branch)
    {
        return branch is { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null };
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

    private static bool IntegerType(ITypeSymbol? type, out CSharpIntegerSemantics semantics)
    {
        return CSharpScalarSemantics.TryGetInteger(type?.SpecialType ?? SpecialType.None, out semantics);
    }

    private sealed class FlowDomain : ClosedAbstractDomain<ManagedFlowState>
    {
        internal static FlowDomain Instance { get; } = new();
        public override ManagedFlowState Bottom => ManagedFlowState.Bottom;
        public override ManagedFlowState Top => ManagedFlowState.Empty;
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
    private readonly Dictionary<object, ManagedFlowState> _states = new(ManagedKeyComparer.Instance);

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
        return operation.DescendantsAndSelf().Any(candidate =>
            TryGetState(candidate, out var state) && !state.IsBottom);
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
        if (!HasMutation(value) &&
            (TryGetState(value, out var state) ||
             TryGetState(origin, out state)))
        {
            result = flow.Evaluate(value, state);
            return true;
        }

        var unwrapped =
            DefiniteOperationFacts.UnwrapHarmlessValue(value);
        if (unwrapped is ISimpleAssignmentOperation assignment &&
            !HasMutation(assignment.Value) &&
            TryGetState(assignment.Value, out var valueState))
        {
            result = flow.Evaluate(
                assignment.Value,
                valueState);
            return true;
        }

        result = ManagedAbstractValue.Unknown;
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
        result = ManagedAbstractValue.Unknown;
        return false;
    }

    internal bool ProvesNonNull(IOperation origin, IOperation value)
    {
        return TryEvaluate(origin, value, out var result) && result.IsDefinitelyNonNull;
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
        return !HasMutation(right) &&
        TryEvaluate(origin, left, out var leftValue) &&
        TryEvaluate(origin, right, out var rightValue) &&
        leftValue.TryGetInteger(out var leftInterval) &&
        rightValue.TryGetInteger(out var rightInterval) &&
        (!leftInterval.Contains(minimum) || !rightInterval.Contains(-1));
    }

    internal bool ProvesArrayAccess(IArrayElementReferenceOperation element)
    {
        return element.Indices.Length == 1 &&
        !HasMutation(element.Indices[0]) &&
        TryEvaluate(element, element.ArrayReference, out var array) &&
        TryEvaluate(element, element.Indices[0], out var index) &&
        array.IsDefinitelyNonNull &&
        array.TryGetCardinality(out var length) && length.LowerBound.HasValue &&
        index.TryGetInteger(out var interval) &&
        interval.LowerBound >= 0 && interval.UpperBound < length.LowerBound;
    }

    internal bool ProvesNoOverflow(IOperation operation)
    {
        return operation is not IBinaryOperation binary || !HasMutation(binary.RightOperand)
            ? TryGetState(operation, out var state) && flow.ProvesNoOverflow(operation, state)
            : false;
    }

    private static bool HasMutation(IOperation operation)
    {
        return operation.DescendantsAndSelf().Any(static candidate =>
            candidate is IAssignmentOperation or IIncrementOrDecrementOperation or IDynamicInvocationOperation ||
            candidate is IArgumentOperation { Parameter.RefKind: not RefKind.None } ||
            candidate is IInvocationOperation invocation &&
            (invocation.TargetMethod.MethodKind == MethodKind.LocalFunction ||
             invocation.TargetMethod.ContainingType.TypeKind == TypeKind.Delegate));
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
        return candidate is not null && Key(operation).Equals(Key(candidate));
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

    private ManagedFlowState(ImmutableDictionary<object, ManagedAbstractValue>? values)
    {
        _values = values;
    }

    internal static ManagedFlowState Bottom { get; } = new(null);
    internal static ManagedFlowState Empty { get; } = new(NoValues);
    internal bool IsBottom => _values == null;
    internal ManagedAbstractValue Get(ISymbol symbol)
    {
        return Get((object)symbol);
    }

    internal ManagedAbstractValue Get(CaptureId capture)
    {
        return Get((object)capture);
    }

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
            ? ManagedAbstractValue.TopForType(symbol is IParameterSymbol parameter ? parameter.Type :
                symbol is ILocalSymbol local ? local.Type : null)
            : ManagedAbstractValue.Unknown;
    }

    internal ManagedFlowState Set(ISymbol symbol, ManagedAbstractValue value)
    {
        return Set((object)symbol, value);
    }

    internal ManagedFlowState Set(CaptureId capture, ManagedAbstractValue value)
    {
        return Set((object)capture, value);
    }

    internal ManagedFlowState Set(object storage, ManagedAbstractValue value)
    {
        return _values == null || value.IsBottom ? Bottom : new(_values.SetItem(storage, value));
    }

    internal ManagedFlowState Forget()
    {
        return IsBottom ? this : Empty;
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

        var result = NoValues.ToBuilder();
        foreach (var key in left._values.Keys.Concat(right._values.Keys).Distinct(Comparer))
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

        return left._values.Keys.Concat(right._values.Keys).Distinct(Comparer).All(key =>
            ManagedAbstractValue.Join(left.Get(key), right.Get(key)) == right.Get(key));
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
        if (type?.SpecialType == SpecialType.System_Boolean)
        {
            return BooleanUnknown;
        }

        if (IntegerType(type, out var integer))
        {
            return Integer(IntervalValue.Range(integer.Minimum, integer.Maximum));
        }

        return type?.IsReferenceType == true ? Reference(NullnessValue.MaybeNull) : Unknown;
    }

    internal static ManagedAbstractValue DefaultForType(ITypeSymbol? type)
    {
        if (type?.SpecialType == SpecialType.System_Boolean)
        {
            return Boolean(false);
        }

        if (IntegerType(type, out _))
        {
            return Integer(IntervalValue.Constant(0));
        }

        return type?.IsReferenceType == true ? Null : Unknown;
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
        return value.TryGetBoolean(out var boolean) ? Boolean(!boolean) : Unknown;
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
        BigInteger[]? bounds = @operator switch
        {
            BinaryOperatorKind.Add => [a + c, b + d],
            BinaryOperatorKind.Subtract => [a - d, b - c],
            BinaryOperatorKind.Multiply => [a * c, a * d, b * c, b * d],
            _ => null
        };
        if (bounds == null)
        {
            return false;
        }

        var minimum = bounds.Min();
        var maximum = bounds.Max();
        if (minimum < long.MinValue || maximum > long.MaxValue)
        {
            return false;
        }

        result = IntervalValue.Range((long)minimum, (long)maximum);
        return true;
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
        return equal.HasValue ? Boolean(negate ? !equal.Value : equal.Value) : unknown;
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

    private static bool IntegerType(ITypeSymbol? type, out CSharpIntegerSemantics semantics)
    {
        return CSharpScalarSemantics.TryGetInteger(type?.SpecialType ?? SpecialType.None, out semantics);
    }
}

/// <summary>Fail-closed execution facts shared by analyzer and effect witnesses.</summary>
internal sealed class DefiniteOperationFacts(Compilation compilation, CancellationToken cancellationToken)
{
    private readonly HashSet<IMethodSymbol> _activeMethods = [];
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
            var body = GetBody(normalized.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken));
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
        return operation switch
        {
            ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or
                IInstanceReferenceOperation or IDefaultValueOperation or ITypeOfOperation or
                INameOfOperation or ISizeOfOperation => true,
            IConversionOperation conversion when HarmlessConversion(conversion) =>
                IsHarmlessValue(conversion.Operand),
            IParenthesizedOperation parenthesized => IsHarmlessValue(parenthesized.Operand),
            _ => false
        };
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
        return operation switch
        {
            IConversionOperation conversion when HarmlessConversion(conversion) =>
                UnwrapHarmlessValue(conversion.Operand),
            IParenthesizedOperation parenthesized => UnwrapHarmlessValue(parenthesized.Operand),
            _ => operation
        };
    }

    internal static bool IsDefinitelyNonNull(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation)
        {
            if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion)
            {
                operation = conversion.Operand;
            }
            else
            {
                break;
            }
        }
        return operation is IInstanceReferenceOperation or IConditionalAccessInstanceOperation or
            IObjectCreationOperation or IArrayCreationOperation or ITypeOfOperation ||
            operation.ConstantValue is { HasValue: true, Value: not null };
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
        !(conversion.Operand.Type?.IsValueType == true && conversion.Type?.IsReferenceType == true);
    }

    private static SyntaxNode? GetBody(SyntaxNode declaration)
    {
        return declaration switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
            _ => null
        };
    }
}
