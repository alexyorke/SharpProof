namespace SharpProof.Frontend;

internal readonly struct TotalBodyValue(IrTerm value, IrBlockId continuation,
    FrontendSubsetClassification classification)
{
    internal IrTerm Value { get; } = value;
    internal IrBlockId Continuation { get; } = continuation;
    internal FrontendSubsetClassification Classification { get; } = classification;
}

// Pure clauses compose safety; body evaluation emits the same local faults as
// ordered edges, materializing operands before later source effects.
internal sealed class RoslynTotalExpressionLowerer(TotalLoweringContext context,
    IrProgramBuilder? builder = null, IrBlockId exceptionalExit = default)
{
    private readonly TotalLoweringContext _context = context;
    private readonly IrFactory _factory = context.Factory;
    private readonly IrProgramBuilder? _builder = builder;
    private readonly IrBlockId _exceptionalExit = exceptionalExit;
    internal Func<IInvocationOperation, TotalParameterState, GuardedExpression?>? Intrinsic { get; set; }
    internal Func<IrExceptionKind, OperationId, IrBlockId>? ExceptionTarget { get; set; }
    internal Action? Spend { get; set; }
    internal Func<IInvocationOperation, IrBlockId, int, TotalBodyValue?>? SourceCall { get; set; }

    internal GuardedExpression LowerClause(IOperation operation,
        TotalParameterState state = TotalParameterState.Current, int depth = 0)
    {
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        {
            return Failed(operation, rejected);
        }
        if (TryLeaf(operation, state, out var leaf))
        {
            return Exact(leaf!);
        }
        switch (operation)
        {
            case IParenthesizedOperation parenthesized:
                return LowerClause(parenthesized.Operand, state, depth + 1);
            case IInvocationOperation invocation:
                return Intrinsic?.Invoke(invocation, state) ?? Failed(operation, FrontendAbstention.UnsupportedInvocationShape);
            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                {
                    var condition = LowerClause(conditional.Condition, state, depth + 1);
                    var whenTrue = LowerClause(conditional.WhenTrue, state, depth + 1);
                    var otherwise = LowerClause(whenFalse, state, depth + 1);
                    var classification = First(condition.Classification, whenTrue.Classification, otherwise.Classification);
                    if (!classification.IsExact)
                    {
                        return Failed(operation, classification.Abstention);
                    }
                    return new(_factory.Conditional(condition.Value, whenTrue.Value, otherwise.Value),
                        And(condition.SafeCondition, _factory.Conditional(condition.Value, whenTrue.SafeCondition, otherwise.SafeCondition)),
                        classification);
                }
            case IConversionOperation conversion:
                return Compose(operation, [LowerClause(conversion.Operand, state, depth + 1)]);
            case IPropertyReferenceOperation { Instance: { } receiver }:
                return Compose(operation, [LowerClause(receiver, state, depth + 1)]);
            case IArrayElementReferenceOperation { Indices.Length: 1 } access:
                return Compose(operation, [LowerClause(access.ArrayReference, state, depth + 1), LowerClause(access.Indices[0], state, depth + 1)]);
            case IUnaryOperation unary:
                return Compose(operation, [LowerClause(unary.Operand, state, depth + 1)]);
            case IBinaryOperation binary:
                {
                    var operands = CSharpOperationSemantics.EqualityOperands(binary);
                    var left = LowerClause(operands.Left, state, depth + 1);
                    var right = LowerClause(operands.Right, state, depth + 1);
                    var result = Compose(operation, [left, right]);
                    if (result.Classification.IsExact && binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
                    {
                        var taken = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? left.Value : Not(left.Value);
                        return new(result.Value, And(left.SafeCondition, Or(Not(taken), right.SafeCondition)), result.Classification);
                    }
                    return result;
                }
            default:
                return Failed(operation, FrontendAbstention.UnsupportedOperationKind);
        }
    }

    internal TotalBodyValue LowerBodyValue(IOperation operation, IrBlockId block, int depth = 0)
    {
        Spend?.Invoke();
        if (depth < 256 && operation is IInvocationOperation invocation &&
            SourceCall?.Invoke(invocation, block, depth) is { } called)
        { return called; }
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        {
            return Approximate(operation, block, rejected);
        }
        if (TryLeaf(operation, TotalParameterState.Current, out var leaf))
        {
            return Capture(operation, new(leaf!, block, FrontendSubsetClassification.Exact));
        }
        switch (operation)
        {
            case IParenthesizedOperation parenthesized:
                return LowerBodyValue(parenthesized.Operand, block, depth + 1);
            case ISimpleAssignmentOperation { IsRef: false } assignment when TryStorage(assignment.Target, out var target):
                {
                    var right = LowerBodyValue(assignment.Value, block, depth + 1);
                    if (right.Classification.IsExact)
                    {
                        _builder!.Assign(right.Continuation, _context.Site(operation), target, right.Value);
                    }
                    return right;
                }
            case IIncrementOrDecrementOperation increment when TryStorage(increment.Target, out var target):
                return Increment(increment, target, block);
            case ICompoundAssignmentOperation compound when TryStorage(compound.Target, out var target):
                return Compound(compound, target, block, depth);
            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                return Conditional(operation, conditional.Condition, conditional.WhenTrue, whenFalse, block, depth);
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } binary:
                return Lazy(binary, block, depth);
            case IConversionOperation conversion:
                return ApplyBody(operation, [LowerBodyValue(conversion.Operand, block, depth + 1)]);
            case IPropertyReferenceOperation { Instance: { } receiver }:
                return ApplyBody(operation, [LowerBodyValue(receiver, block, depth + 1)]);
            case IArrayElementReferenceOperation { Indices.Length: 1 } access:
                {
                    var array = LowerBodyValue(access.ArrayReference, block, depth + 1);
                    var index = LowerBodyValue(access.Indices[0], array.Continuation, depth + 1);
                    return ApplyBody(operation, [array, index]);
                }
            case IUnaryOperation unary:
                return ApplyBody(operation, [LowerBodyValue(unary.Operand, block, depth + 1)]);
            case IBinaryOperation binary:
                {
                    var operands = CSharpOperationSemantics.EqualityOperands(binary);
                    var left = LowerBodyValue(operands.Left, block, depth + 1);
                    var right = LowerBodyValue(operands.Right, left.Continuation, depth + 1);
                    return ApplyBody(operation, [left, right]);
                }
            default:
                return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind);
        }
    }

    private GuardedExpression Compose(IOperation operation, ImmutableArray<GuardedExpression> children)
    {
        var classification = First([.. children.Select(value => value.Classification)]);
        if (!classification.IsExact)
        {
            return Failed(operation, classification.Abstention);
        }
        var rule = CSharpOperationSemantics.Apply(_factory, operation, [.. children.Select(value => value.Value)]);
        if (!rule.Classification.IsExact)
        {
            return Failed(operation, rule.Classification.Abstention);
        }
        IrTerm safe = _factory.Boolean(true);
        foreach (var child in children)
        {
            safe = And(safe, child.SafeCondition);
        }
        foreach (var fault in rule.Throws)
        {
            safe = And(safe, Not(fault.Condition));
        }
        return new(rule.Value, safe, rule.Classification);
    }

    private TotalBodyValue ApplyBody(IOperation operation, ImmutableArray<TotalBodyValue> children)
    {
        var block = children[children.Length - 1].Continuation;
        var classification = First([.. children.Select(value => value.Classification)]);
        if (!classification.IsExact)
        {
            return Approximate(operation, block, classification.Abstention);
        }
        var rule = CSharpOperationSemantics.Apply(_factory, operation, [.. children.Select(value => value.Value)]);
        if (!rule.Classification.IsExact)
        {
            return Approximate(operation, block, rule.Classification.Abstention);
        }
        return ApplyRule(operation, rule, block);
    }

    internal TotalBodyValue LowerInt32MathAbs(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        var argument = LowerBodyValue(invocation.Arguments[0].Value, block, depth + 1);
        if (!argument.Classification.IsExact)
        { return argument; }
        return ApplyRule(invocation, CSharpOperationSemantics.Int32MathAbs(_factory, argument.Value), argument.Continuation);
    }

    private TotalBodyValue ApplyRule(IOperation operation, TotalScalarRule rule, IrBlockId block)
    {
        foreach (var fault in rule.Throws)
        {
            var thrown = _builder!.CreateBlock("throw");
            var normal = _builder.CreateBlock("normal");
            var site = _context.Site(operation);
            _builder.Branch(block, site, fault.Condition, thrown, normal);
            _builder.Throw(thrown, site, fault.Kind, ExceptionTarget?.Invoke(fault.Kind, site) ?? _exceptionalExit);
            block = normal;
        }
        return Capture(operation, new(rule.Value, block, rule.Classification));
    }

    private TotalBodyValue Lazy(IBinaryOperation operation, IrBlockId block, int depth)
    {
        if (operation.OperatorMethod != null || operation.IsLifted)
        {
            return Approximate(operation, block, FrontendAbstention.UserDefinedOperator);
        }
        var left = LowerBodyValue(operation.LeftOperand, block, depth + 1);
        if (!left.Classification.IsExact)
        {
            return left;
        }
        var rightBlock = _builder!.CreateBlock("lazy:right");
        var skipped = _builder.CreateBlock("lazy:skip");
        var join = _builder.CreateBlock("lazy:join");
        var target = _context.Temporary(_factory.BooleanType);
        var site = _context.Site(operation);
        var isAnd = operation.OperatorKind == BinaryOperatorKind.ConditionalAnd;
        _builder.Branch(left.Continuation, site, left.Value, isAnd ? rightBlock : skipped, isAnd ? skipped : rightBlock);
        _builder.Assign(skipped, site, target, _factory.Boolean(!isAnd));
        _builder.Goto(skipped, site, join);
        var right = LowerBodyValue(operation.RightOperand, rightBlock, depth + 1);
        _builder.Assign(right.Continuation, site, target, right.Value);
        _builder.Goto(right.Continuation, site, join);
        return new(_factory.Variable(target), join, right.Classification);
    }

    private TotalBodyValue Conditional(IOperation operation, IOperation conditionOperation, IOperation whenTrue,
        IOperation whenFalse, IrBlockId block, int depth)
    {
        var condition = LowerBodyValue(conditionOperation, block, depth + 1);
        if (!condition.Classification.IsExact)
        {
            return Approximate(operation, condition.Continuation, condition.Classification.Abstention);
        }
        var trueBlock = _builder!.CreateBlock("conditional:true");
        var falseBlock = _builder.CreateBlock("conditional:false");
        var join = _builder.CreateBlock("conditional:join");
        var target = _context.Temporary(_context.Type(operation.Type));
        var site = _context.Site(operation);
        _builder.Branch(condition.Continuation, site, condition.Value, trueBlock, falseBlock);
        var first = LowerBodyValue(whenTrue, trueBlock, depth + 1);
        _builder.Assign(first.Continuation, site, target, first.Value);
        _builder.Goto(first.Continuation, site, join);
        var second = LowerBodyValue(whenFalse, falseBlock, depth + 1);
        _builder.Assign(second.Continuation, site, target, second.Value);
        _builder.Goto(second.Continuation, site, join);
        return new(_factory.Variable(target), join, First(first.Classification, second.Classification));
    }

    private TotalBodyValue Increment(IIncrementOrDecrementOperation operation, IrVarId target, IrBlockId block)
    {
        var old = Capture(operation, new(_factory.Variable(target), block, FrontendSubsetClassification.Exact));
        var rule = CSharpOperationSemantics.Increment(_factory, operation, old.Value);
        if (!rule.Classification.IsExact)
        { return Approximate(operation, block, rule.Classification.Abstention); }
        var next = ApplyRule(operation, rule, old.Continuation);
        _builder!.Assign(next.Continuation, _context.Site(operation), target, next.Value);
        return operation.IsPostfix ? new(old.Value, next.Continuation, next.Classification) : next;
    }

    private TotalBodyValue Compound(ICompoundAssignmentOperation operation, IrVarId target, IrBlockId block, int depth)
    {
        var old = Capture(operation, new(_factory.Variable(target), block, FrontendSubsetClassification.Exact));
        var right = LowerBodyValue(operation.Value, old.Continuation, depth + 1);
        if (!right.Classification.IsExact)
        {
            return Approximate(operation, right.Continuation, right.Classification.Abstention);
        }
        var rule = CSharpOperationSemantics.Compound(_factory, operation, old.Value, right.Value);
        if (!rule.Classification.IsExact)
        { return Approximate(operation, right.Continuation, rule.Classification.Abstention); }
        var result = ApplyRule(operation, rule, right.Continuation);
        _builder!.Assign(result.Continuation, _context.Site(operation), target, result.Value);
        return result;
    }

    private TotalBodyValue Capture(IOperation operation, TotalBodyValue value)
    {
        if (value.Value is IrNullTerm)
        { return value; }
        var target = _context.Temporary(value.Value.Type);
        _builder!.Assign(value.Continuation, _context.Site(operation), target, value.Value);
        return new(_factory.Variable(target), value.Continuation, value.Classification);
    }

    private bool TryLeaf(IOperation operation, TotalParameterState state, out IrTerm? value)
    {
        value = operation switch
        {
            ILiteralOperation literal => CSharpOperationSemantics.Literal(_factory, literal.Type!, literal.ConstantValue.Value),
            IDefaultValueOperation => CSharpOperationSemantics.Literal(_factory, operation.Type!, null),
            IParameterReferenceOperation parameter => _factory.Variable(_context.Variable(parameter.Parameter, state)),
            ILocalReferenceOperation local when local.Local.HasConstantValue => CSharpOperationSemantics.Literal(_factory, local.Type!, local.Local.ConstantValue),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None => _factory.Variable(_context.Variable(local.Local)),
            IFlowCaptureReferenceOperation capture => _factory.Variable(_context.Capture(capture.Id, capture.Type)),
            IFieldReferenceOperation field when field.Field.HasConstantValue => CSharpOperationSemantics.Literal(_factory, field.Type!, field.Field.ConstantValue),
            _ => null
        };
        return value != null;
    }

    private bool TryStorage(IOperation operation, out IrVarId target)
    {
        target = operation switch
        {
            IParameterReferenceOperation parameter => _context.Variable(parameter.Parameter),
            ILocalReferenceOperation local when local.Local.RefKind == RefKind.None => _context.Variable(local.Local),
            _ => default
        };
        return !target.IsDefault;
    }

    private static FrontendAbstention Reject(IOperation operation, int depth)
    {
        if (depth >= 256)
        { return FrontendAbstention.ExpressionDepthLimit; }
        if (!CSharpOperationSemantics.Operations.TryGetValue(operation.Kind, out var admission))
        { return FrontendAbstention.UnknownOperationKind; }
        if (admission == TotalOperationAdmission.Invalid)
        { return FrontendAbstention.InvalidOperation; }
        if (admission == TotalOperationAdmission.Incomplete)
        {
            if (operation is not IPropertyReferenceOperation property || !CSharpOperationSemantics.IsLength(property))
            { return FrontendAbstention.UnsupportedOperationKind; }
        }
        if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string)
        { return FrontendAbstention.UnsupportedOperationKind; }
        return CSharpOperationSemantics.IsValueDomain(operation.Type) ||
            operation is ILiteralOperation { ConstantValue.HasValue: true, ConstantValue.Value: null }
            ? FrontendAbstention.None : FrontendAbstention.UnsupportedType;
    }

    private GuardedExpression Failed(IOperation operation, FrontendAbstention reason)
    {
        return new(Default(operation.Type), _factory.Boolean(false), FrontendSubsetClassification.Abstain(reason));
    }
    private GuardedExpression Exact(IrTerm value)
    {
        return new(value, _factory.Boolean(true), FrontendSubsetClassification.Exact);
    }
    private TotalBodyValue Approximate(IOperation operation, IrBlockId block, FrontendAbstention reason)
    {
        var target = _context.Temporary(_context.Type(operation.Type));
        _builder!.Havoc(block, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, target);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Abstain(reason));
    }
    private IrTerm Default(ITypeSymbol? type)
    {
        return CSharpOperationSemantics.IsValueDomain(type) ? CSharpOperationSemantics.Literal(_factory, type, null) : _factory.Boolean(false);
    }
    private IrTerm And(IrTerm first, IrTerm second)
    {
        return _factory.Binary(IrBinaryOperator.AndAlso, first, second);
    }
    private IrTerm Or(IrTerm first, IrTerm second)
    {
        return _factory.Binary(IrBinaryOperator.OrElse, first, second);
    }
    private IrTerm Not(IrTerm value)
    {
        return _factory.Unary(IrUnaryOperator.Not, value);
    }
    private static FrontendSubsetClassification First(params FrontendSubsetClassification[] classifications)
    {
        return classifications.FirstOrDefault(value => !value.IsExact).Decision == FrontendSubsetDecision.ClosedAbstention
            ? classifications.First(value => !value.IsExact) : FrontendSubsetClassification.Exact;
    }
}
