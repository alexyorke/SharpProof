namespace SharpProof.Frontend;

internal readonly struct TotalBodyValue(IrTerm value, IrBlockId continuation,
    FrontendSubsetClassification classification, ImmutableArray<IrTerm> concatenationOperands = default)
{
    internal IrTerm Value { get; } = value;
    internal IrBlockId Continuation { get; } = continuation;
    internal FrontendSubsetClassification Classification { get; } = classification;
    internal ImmutableArray<IrTerm> ConcatenationOperands { get; } = concatenationOperands;
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
        if (depth < 256 && operation is IConversionOperation
            { OperatorMethod: null, IsTryCast: false, Operand: ICollectionExpressionOperation converted } collectionConversion &&
            !collectionConversion.Conversion.IsUserDefined && SymbolEqualityComparer.Default.Equals(collectionConversion.Type, converted.Type) &&
            CSharpOperationSemantics.IsSupportedConstantArrayCollection(converted))
        { return LowerBodyValue(converted, block, depth + 1); }
        if (depth < 256 && operation is ICollectionExpressionOperation collection &&
            CSharpOperationSemantics.IsSupportedConstantArrayCollection(collection))
        {
            var initial = collection.Elements.Select(element =>
                CSharpOperationSemantics.Literal(_factory, element.Type, element.ConstantValue.Value)).ToImmutableArray();
            var allocatedType = _context.Type(collection.Type);
            var allocatedTarget = _context.Temporary(allocatedType);
            _builder!.Allocate(block, _context.Site(collection), allocatedType, allocatedTarget, _factory.Integer(initial.Length), initial);
            return new(_factory.Variable(allocatedTarget), block, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && operation is IArrayCreationOperation arrayCreation && CSharpOperationSemantics.IsSupportedArrayCreation(arrayCreation))
        {
            var dimension = LowerBodyValue(arrayCreation.DimensionSizes[0], block, depth + 1);
            if (!dimension.Classification.IsExact)
            { return dimension; }
            var rule = new TotalScalarRule(dimension.Value,
                [new(IrExceptionKind.Overflow, _factory.Binary(IrBinaryOperator.LessThan, dimension.Value, _factory.Integer(0)))],
                FrontendSubsetClassification.Exact);
            block = ApplyRule(operation, rule, dimension.Continuation).Continuation;
            var type = _context.Type(operation.Type);
            var target = _context.Temporary(type);
            var initialValues = arrayCreation.Initializer == null ? ImmutableArray<IrTerm>.Empty
                : arrayCreation.Initializer.ElementValues.Select(element =>
                    CSharpOperationSemantics.Literal(_factory, element.Type, element.ConstantValue.Value)).ToImmutableArray();
            var length = arrayCreation.Initializer == null ? dimension.Value : _factory.Integer(initialValues.Length);
            _builder!.Allocate(block, _context.Site(operation), type, target, length, initialValues);
            return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
        }
        if (depth < 256 && CSharpOperationSemantics.IsStringConcatenation(operation))
        { return LowerStringConcatenation(operation, block, depth); }
        if (depth < 256 && operation is IObjectCreationOperation creation &&
            CSharpOperationSemantics.IsCoreObjectCreation(creation))
        { return AllocateValue(operation, block); }
        if (depth < 256 && operation is IDelegateCreationOperation delegateCreation &&
            CSharpOperationSemantics.IsExplicitDelegateCreation(delegateCreation))
        {
            if (((IMethodReferenceOperation)delegateCreation.Target).Instance is { } instance)
            {
                var receiver = LowerBodyValue(instance, block, depth + 1);
                if (!receiver.Classification.IsExact)
                { return receiver; }
                IrTerm? mayCheckNull = null;
                if (!CSharpOperationSemantics.DelegateValueEscapesDirectly(delegateCreation))
                {
                    // Release emission can erase an unused constructor and its
                    // null check. Preserve both paths; approximation reads may
                    // support universal proofs but never concrete refutations.
                    var choice = _context.Temporary(_factory.BooleanType);
                    _builder!.Havoc(receiver.Continuation, _context.Site(operation), IrHavocKind.Variables, IrHavocOrigin.Approximation, choice);
                    mayCheckNull = _factory.Variable(choice);
                }
                block = ApplyRule(operation, CSharpOperationSemantics.DelegateReceiver(_factory, receiver.Value, mayCheckNull), receiver.Continuation).Continuation;
            }
            return AllocateValue(operation, block);
        }
        if (depth < 256 && operation is IConversionOperation boxing && CSharpOperationSemantics.IsScalarBoxing(boxing))
        {
            var operand = LowerBodyValue(boxing.Operand, block, depth + 1);
            return operand.Classification.IsExact ? AllocateValue(operation, operand.Continuation) : operand;
        }
        if (depth < 256 && operation is IInvocationOperation synchronization &&
            CSharpOperationSemantics.IsMonitorAttempt(synchronization))
        {
            var receiver = LowerBodyValue(synchronization.Arguments[0].Value, block, depth + 1);
            if (receiver.Classification.IsExact)
            { _builder!.Lock(receiver.Continuation, _context.Site(operation), receiver.Value); }
            return new(_factory.Boolean(false), receiver.Continuation, receiver.Classification);
        }
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
            var parts = operation is IFlowCaptureReferenceOperation capture
                ? _context.ConcatenationOperands(capture.Id) : default;
            return Capture(operation, new(leaf!, block, FrontendSubsetClassification.Exact, parts));
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
                        var mutation = _builder!.Assign(right.Continuation, _context.Site(operation), target, right.Value);
                        _builder.Write(right.Continuation, mutation.Operation, IrWriteRegion.Local);
                    }
                    return right;
                }
            case ISimpleAssignmentOperation { IsRef: false, Target: IFieldReferenceOperation field } assignment
                when CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, _context.Target.ContainingAssembly):
                return FieldWrite(assignment, field, block, depth);
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

    private TotalBodyValue AllocateValue(IOperation operation, IrBlockId block)
    {
        var type = _context.Type(operation.Type);
        var target = _context.Temporary(type);
        _builder!.Allocate(block, _context.Site(operation), type, target);
        return new(_factory.Variable(target), block, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue LowerStringConcatenation(IOperation operation, IrBlockId block, int depth)
    {
        var rejected = Reject(operation, depth);
        if (rejected != FrontendAbstention.None)
        { return Approximate(operation, block, rejected); }
        if (operation.ConstantValue is { HasValue: true, Value: string text })
        { return Capture(operation, new(_factory.String(text), block, FrontendSubsetClassification.Exact)); }
        var leaves = new List<IOperation>();
        Collect(operation, depth);
        var operands = ImmutableArray.CreateBuilder<IrTerm>();
        var constant = new System.Text.StringBuilder();
        foreach (var leaf in leaves)
        {
            var value = LowerBodyValue(leaf, block, depth + 1);
            if (!value.Classification.IsExact)
            { return value; }
            block = value.Continuation;
            if (!value.ConcatenationOperands.IsDefault)
            {
                foreach (var part in value.ConcatenationOperands)
                {
                    if (part is IrStringTerm textPart)
                    { constant.Append(_factory.GetString(textPart.Value)); }
                    else
                    {
                        FlushConstant();
                        operands.Add(part);
                    }
                }
            }
            else if (leaf.ConstantValue.HasValue && leaf.ConstantValue.Value is null or string)
            { constant.Append(leaf.ConstantValue.Value as string); }
            else
            {
                FlushConstant();
                operands.Add(value.Value);
            }
        }
        FlushConstant();
        // The compiler merges adjacent constants and drops constant null/empty
        // strings before choosing a fixed overload or creating a params array.
        if (operands.Count > 4)
        { return Approximate(operation, block, FrontendAbstention.UnsupportedOperationKind); }
        if (operands.Count == 0)
        { return Capture(operation, new(_factory.String(""), block, FrontendSubsetClassification.Exact)); }
        if (operands.Count == 1)
        { operands.Insert(0, _factory.String("")); }
        var parent = operation.Syntax.Parent;
        while (parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax)
        { parent = parent.Parent; }
        var deferred = operation.Parent is IFlowCaptureOperation &&
            parent is Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax binaryParent &&
            binaryParent.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression);
        return CaptureStringConcatenation(operation, operands.ToImmutable(), block, deferred);

        void FlushConstant()
        {
            if (constant.Length != 0)
            {
                operands.Add(_factory.String(constant.ToString()));
                constant.Clear();
            }
        }

        void Collect(IOperation current, int nesting)
        {
            Spend?.Invoke();
            if (nesting < 256 && CSharpOperationSemantics.IsStringConcatenation(current) && !current.ConstantValue.HasValue)
            {
                var binary = (IBinaryOperation)current;
                Collect(binary.LeftOperand, nesting + 1);
                Collect(binary.RightOperand, nesting + 1);
            }
            else
            { leaves.Add(current); }
        }
    }

    private TotalBodyValue CaptureStringConcatenation(IOperation operation, ImmutableArray<IrTerm> operands, IrBlockId block,
        bool deferred = false)
    {
        var site = _context.Site(operation);
        var allocation = _builder!.CreateBlock("concat:allocate");
        var join = _builder.CreateBlock("concat:join");
        // A CFG-captured prefix remains part of the emitter's flattened chain.
        // Its value may be carried, but its allocation is deferred to the root.
        _builder.Branch(block, site, deferred ? _factory.Boolean(false) :
            CSharpOperationSemantics.StringConcatenationAllocates(_factory, operands), allocation, join);
        _builder.Allocate(allocation, site, _factory.StringType);
        _builder.Goto(allocation, site, join);
        var result = operands[0];
        foreach (var operand in operands.Skip(1))
        { result = CSharpOperationSemantics.StringConcat(_factory, result, operand).Value; }
        return Capture(operation, new(result, join, FrontendSubsetClassification.Exact,
            deferred ? operands : default), site);
    }

    private TotalBodyValue FieldWrite(ISimpleAssignmentOperation assignment,
        IFieldReferenceOperation field, IrBlockId block, int depth)
    {
        IrTerm? receiver = null;
        var implicitThis = field.Instance is IInstanceReferenceOperation
        { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } &&
            !_context.Target.IsStatic && _context.Target.ContainingType.IsReferenceType;
        if (!field.Field.IsStatic && !implicitThis)
        {
            if (field.Instance == null)
            { return Approximate(assignment, block, FrontendAbstention.UnsupportedOperationKind); }
            var instance = LowerBodyValue(field.Instance, block, depth + 1);
            if (!instance.Classification.IsExact)
            { return Approximate(assignment, instance.Continuation, instance.Classification.Abstention); }
            receiver = instance.Value;
            block = instance.Continuation;
        }
        // C# captures the receiver before evaluating the RHS, but faults on a
        // null receiver only after the RHS has finished evaluating.
        var right = LowerBodyValue(assignment.Value, block, depth + 1);
        if (!right.Classification.IsExact)
        { return right; }
        var result = ApplyRule(assignment,
            CSharpOperationSemantics.FieldWrite(_factory, right.Value, receiver), right.Continuation);
        var region = field.Field.IsStatic ? IrWriteRegion.Static :
            field.Instance is IParameterReferenceOperation ? IrWriteRegion.Parameter : IrWriteRegion.Field;
        _builder!.Write(result.Continuation, _context.Site(assignment), region);
        return result;
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

    internal TotalBodyValue LowerScalarCall(IInvocationOperation invocation, TotalScalarCallModel model, IrBlockId block, int depth)
    {
        var arguments = new IrTerm[model.ParameterCount];
        foreach (var argument in invocation.Arguments)
        {
            var lowered = LowerBodyValue(argument.Value, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return lowered; }
            arguments[argument.Parameter!.Ordinal] = lowered.Value;
            block = lowered.Continuation;
        }
        var rule = model.Apply([.. arguments]);
        if (model.StringConcatenation && rule.Classification.IsExact)
        { return CaptureStringConcatenation(invocation, [.. arguments], block); }
        return rule.Classification.IsExact ? ApplyRule(invocation, rule, block)
            : Approximate(invocation, block, rule.Classification.Abstention);
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
        var mutation = _builder!.Assign(next.Continuation, _context.Site(operation), target, next.Value);
        _builder.Write(next.Continuation, mutation.Operation, IrWriteRegion.Local);
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
        var mutation = _builder!.Assign(result.Continuation, _context.Site(operation), target, result.Value);
        _builder.Write(result.Continuation, mutation.Operation, IrWriteRegion.Local);
        return result;
    }

    private TotalBodyValue Capture(IOperation operation, TotalBodyValue value, OperationId? site = null)
    {
        if (value.Value is IrNullTerm)
        { return value; }
        var target = _context.Temporary(value.Value.Type);
        _builder!.Assign(value.Continuation, site ?? _context.Site(operation), target, value.Value);
        return new(_factory.Variable(target), value.Continuation, value.Classification, value.ConcatenationOperands);
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

    private FrontendAbstention Reject(IOperation operation, int depth)
    {
        if (depth >= 256)
        { return FrontendAbstention.ExpressionDepthLimit; }
        if (operation is IParameterReferenceOperation parameter && !_context.OwnsParameter(parameter.Parameter))
        { return FrontendAbstention.UnsupportedOperationKind; }
        if (!CSharpOperationSemantics.Operations.TryGetValue(operation.Kind, out var decision))
        { return FrontendAbstention.UnknownOperationKind; }
        var admission = decision.Admission;
        if (admission == TotalOperationAdmission.Invalid)
        { return FrontendAbstention.InvalidOperation; }
        if (admission == TotalOperationAdmission.Incomplete)
        {
            if (operation is not IPropertyReferenceOperation property || !CSharpOperationSemantics.IsLength(property))
            { return FrontendAbstention.UnsupportedOperationKind; }
        }
        if (operation.ConstantValue is { HasValue: true, Value: string text } && !Utf16WellFormedness.IsWellFormed(text))
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
