using System.Numerics;

namespace SharpProof.Analyzer;

// This is a closed structural proof system. Never descend through an unknown
// operation: doing so could omit a call, an allocation, or a nonterminating path.
internal sealed class ResourceBoundsAnalysis
{
    internal sealed record Bound
    {
        internal string Text { get; }
        internal BigInteger? Constant { get; }
        internal Bound(string text, BigInteger? constant = null)
        {
            Text = ArgumentNullGuard.NotNull(text, nameof(text));
            Constant = constant;
        }
        internal static readonly Bound Zero = Number(0);
        internal static readonly Bound One = Number(1);
        internal static Bound Number(BigInteger value)
        {
            return new(value.ToString(CultureInfo.InvariantCulture), value);
        }
        internal static Bound Add(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return left.Constant == 0 ? right : right.Constant == 0 ? left :
                left.Constant is { } a && right.Constant is { } b ? Number(a + b) :
                new($"({left.Text} + {right.Text})");
        }
        internal static Bound Multiply(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return left.Constant == 0 || right.Constant == 0 ? Zero :
                left.Constant == 1 ? right : right.Constant == 1 ? left :
                left.Constant is { } a && right.Constant is { } b ? Number(a * b) :
                new($"({left.Text} * {right.Text})");
        }
        internal static Bound Max(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return left.Text == right.Text ? left :
                left.Constant is { } a && right.Constant is { } b ? Number(BigInteger.Max(a, b)) :
                new($"max({left.Text}, {right.Text})");
        }
    }

    internal sealed record Cost
    {
        internal Bound LoopIterations { get; }
        internal Bound Arrays { get; }
        internal Bound Elements { get; }
        internal Cost(Bound loopIterations, Bound arrays, Bound elements)
        {
            LoopIterations = ArgumentNullGuard.NotNull(loopIterations, nameof(loopIterations));
            Arrays = ArgumentNullGuard.NotNull(arrays, nameof(arrays));
            Elements = ArgumentNullGuard.NotNull(elements, nameof(elements));
        }
        internal static readonly Cost Zero = new(Bound.Zero, Bound.Zero, Bound.Zero);
        internal Cost Add(Cost other)
        {
            other = ArgumentNullGuard.NotNull(other, nameof(other));
            return new(Bound.Add(LoopIterations, other.LoopIterations),
                Bound.Add(Arrays, other.Arrays), Bound.Add(Elements, other.Elements));
        }
        internal Cost Max(Cost other)
        {
            other = ArgumentNullGuard.NotNull(other, nameof(other));
            return new(Bound.Max(LoopIterations, other.LoopIterations),
                Bound.Max(Arrays, other.Arrays), Bound.Max(Elements, other.Elements));
        }
        internal Cost Scale(Bound count)
        {
            count = ArgumentNullGuard.NotNull(count, nameof(count));
            return new(Bound.Multiply(count, LoopIterations),
                Bound.Multiply(count, Arrays), Bound.Multiply(count, Elements));
        }
    }

    internal sealed record Result(Cost? Bounds, string? UnknownReason)
    {
        internal bool Proven => Bounds is not null;
    }

    private readonly CancellationToken _cancellation;
    private readonly HashSet<ISymbol> _counters = new(SymbolEqualityComparer.Default);
    private string? _unknown;
    private int _remaining = 4096;

    private ResourceBoundsAnalysis(CancellationToken cancellation)
    {
        _cancellation = cancellation;
    }

    internal static Result Analyze(IMethodSymbol method, IOperation? body, CancellationToken cancellation)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        cancellation.ThrowIfCancellationRequested();
        if (method.IsAsync || method.IsVirtual || method.IsAbstract || method.IsExtern ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            (!SupportedType(method.ReturnType) && !method.ReturnsVoid) ||
            method.Parameters.Any(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                parameter.RefKind != RefKind.None || !SupportedType(parameter.Type)))
        {
            return new(null, "UnsupportedCallable");
        }
        if (body is null)
        {
            return new(null, "MissingBody");
        }
        var analysis = new ResourceBoundsAnalysis(cancellation);
        var cost = analysis.Visit(body, 0);
        return analysis._unknown is { } reason ? new(null, reason) : new(cost, null);
    }

    private static bool Primitive(ITypeSymbol? type)
    {
        return type?.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
        SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double;
    }

    private static bool SupportedType(ITypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        return Primitive(type) || type is IArrayTypeSymbol { Rank: 1, IsSZArray: true } array && Primitive(array.ElementType);
    }

    private Cost Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        _unknown ??= reason;
        return Cost.Zero;
    }

    private Cost Visit(IOperation operation, int depth)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        _cancellation.ThrowIfCancellationRequested();
        if (_unknown is not null)
        { return Cost.Zero; }
        if (--_remaining < 0 || depth > 128)
        { return Unknown("AnalysisBudgetExceeded"); }
        Cost result;
        switch (operation)
        {
            case IBlockOperation block:
                result = Sequence(block.Operations, depth);
                break;
            case IVariableDeclarationGroupOperation group:
                result = Sequence(group.Declarations, depth);
                break;
            case IVariableDeclarationOperation declaration:
                result = Sequence(declaration.Declarators, depth);
                break;
            case IVariableDeclaratorOperation variable:
                result = variable.Symbol.RefKind != RefKind.None || !SupportedType(variable.Symbol.Type)
                    ? Unknown("UnsupportedLocal")
                    : variable.Initializer is { } variableInitializer ? Visit(variableInitializer, depth + 1) : Cost.Zero;
                break;
            case IVariableInitializerOperation initializer:
                result = Visit(initializer.Value, depth + 1);
                break;
            case IExpressionStatementOperation expression:
                result = Visit(expression.Operation, depth + 1);
                break;
            case IReturnOperation returned:
                result = returned.ReturnedValue is { } value ? Visit(value, depth + 1) : Cost.Zero;
                break;
            case IForLoopOperation loop:
                result = For(loop, depth);
                break;
            case IConditionalOperation conditional when !conditional.IsRef:
                result = Visit(conditional.Condition, depth + 1).Add(
                    Visit(conditional.WhenTrue, depth + 1).Max(
                        conditional.WhenFalse is { } alternative ? Visit(alternative, depth + 1) : Cost.Zero));
                break;
            case IArrayCreationOperation array when array.Type is IArrayTypeSymbol type &&
                SupportedType(type) && array.DimensionSizes.Length == 1 && array.Initializer is null:
                var size = Size(array.DimensionSizes[0]);
                result = size is null ? Unknown("UnsupportedAllocationSize") : new(Bound.Zero, Bound.One, size);
                break;
            case ISimpleAssignmentOperation assignment when !assignment.IsRef:
                result = Writable(assignment.Target)
                    ? Visit(assignment.Target, depth + 1).Add(Visit(assignment.Value, depth + 1))
                    : Unknown("UnstableInputOrCounter");
                break;
            case ICompoundAssignmentOperation assignment when assignment.OperatorMethod is null &&
                Primitive(assignment.Type) && !assignment.InConversion.IsUserDefined && !assignment.OutConversion.IsUserDefined:
                result = Writable(assignment.Target)
                    ? Visit(assignment.Target, depth + 1).Add(Visit(assignment.Value, depth + 1))
                    : Unknown("UnstableInputOrCounter");
                break;
            case IIncrementOrDecrementOperation increment when increment.OperatorMethod is null && Primitive(increment.Type):
                result = Writable(increment.Target) ? Visit(increment.Target, depth + 1) : Unknown("UnstableInputOrCounter");
                break;
            case IBinaryOperation binary when binary.OperatorMethod is null && Primitive(binary.Type):
                result = Visit(binary.LeftOperand, depth + 1).Add(Visit(binary.RightOperand, depth + 1));
                break;
            case IUnaryOperation unary when unary.OperatorMethod is null && Primitive(unary.Type):
                result = Visit(unary.Operand, depth + 1);
                break;
            case IConversionOperation conversion when !conversion.Conversion.IsUserDefined &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsNumeric) &&
                conversion.Type is { } converted && SupportedType(converted):
                result = Visit(conversion.Operand, depth + 1);
                break;
            case IArrayElementReferenceOperation element when Primitive(element.Type):
                result = Visit(element.ArrayReference, depth + 1).Add(Sequence(element.Indices, depth));
                break;
            case IPropertyReferenceOperation property when IsArrayLength(property):
                result = Visit(property.Instance!, depth + 1);
                break;
            case ILocalReferenceOperation local when SupportedType(local.Local.Type):
            case IParameterReferenceOperation parameter when SupportedType(parameter.Parameter.Type):
            case ILiteralOperation literal when literal.Type is { } literalType && SupportedType(literalType):
            case IEmptyOperation:
                result = Cost.Zero;
                break;
            default:
                result = Unknown("UnsupportedOperation:" + operation.Kind);
                break;
        }
        return result.LoopIterations.Text.Length + result.Arrays.Text.Length + result.Elements.Text.Length > 16384
            ? Unknown("AnalysisBudgetExceeded") : result;
    }

    private Cost Sequence(IEnumerable<IOperation> operations, int depth)
    {
        operations = ArgumentNullGuard.NotNull(operations, nameof(operations));
        var result = Cost.Zero;
        foreach (var operation in operations)
        {
            result = result.Add(Visit(operation, depth + 1));
            if (_unknown is not null)
            { break; }
            if (result.LoopIterations.Text.Length + result.Arrays.Text.Length + result.Elements.Text.Length > 16384)
            { return Unknown("AnalysisBudgetExceeded"); }
        }
        return result;
    }

    private Cost For(IForLoopOperation loop, int depth)
    {
        loop = ArgumentNullGuard.NotNull(loop, nameof(loop));
        // i=0; i<b; i++ cannot wrap for any int b: the last increment is
        // (b-1)->b. b is an unchanged entry parameter, immutable array length,
        // or constant. No other counter writes or aliases are admitted.
        if (loop.Before.Length != 1 || loop.Before[0] is not IVariableDeclarationGroupOperation group ||
            group.Declarations.Length != 1 || group.Declarations[0].Declarators.Length != 1 ||
            group.Declarations[0].Declarators[0] is not { } declaration ||
            declaration.Symbol.Type.SpecialType != SpecialType.System_Int32 ||
            declaration.Symbol.RefKind != RefKind.None ||
            declaration.Initializer?.Value.ConstantValue is not { HasValue: true, Value: int start } || start != 0 ||
            loop.Condition is not IBinaryOperation { OperatorKind: BinaryOperatorKind.LessThan, OperatorMethod: null } condition ||
            condition.LeftOperand is not ILocalReferenceOperation counter ||
            !SymbolEqualityComparer.Default.Equals(counter.Local, declaration.Symbol) ||
            loop.AtLoopBottom.Length != 1 ||
            loop.AtLoopBottom[0] is not IExpressionStatementOperation { Operation: IIncrementOrDecrementOperation increment } ||
            increment.Kind != OperationKind.Increment || increment.OperatorMethod is not null ||
            increment.Target is not ILocalReferenceOperation target ||
            !SymbolEqualityComparer.Default.Equals(target.Local, declaration.Symbol))
        { return Unknown("UnsupportedLoop"); }
        var bound = Size(condition.RightOperand);
        if (bound is null)
        { return Unknown("UnsupportedLoopBound"); }
        _counters.Add(declaration.Symbol);
        var body = Visit(loop.Body, depth + 1);
        _counters.Remove(declaration.Symbol);
        return new Cost(Bound.One, Bound.Zero, Bound.Zero).Add(body).Scale(bound);
    }

    private bool Writable(IOperation target)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return target switch
        {
            ILocalReferenceOperation local => local.Local.RefKind == RefKind.None && !_counters.Contains(local.Local),
            IArrayElementReferenceOperation element => Primitive(element.Type),
            _ => false
        };
    }

    private static bool IsArrayLength(IPropertyReferenceOperation property)
    {
        property = ArgumentNullGuard.NotNull(property, nameof(property));
        return property.Property.Name == "Length" && property.Property.ContainingType.SpecialType == SpecialType.System_Array &&
            property.Instance?.Type is IArrayTypeSymbol { Rank: 1, IsSZArray: true };
    }

    private static Bound? Size(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        return operation switch
        {
            ILiteralOperation { ConstantValue: { HasValue: true, Value: int value } } => Bound.Number(Math.Max(0, value)),
            IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Minus, OperatorMethod: null,
                Operand: ILiteralOperation, ConstantValue: { HasValue: true, Value: int value }
            } => Bound.Number(Math.Max(0, value)),
            IParameterReferenceOperation parameter when parameter.Parameter.Type.SpecialType == SpecialType.System_Int32 =>
                new("max(0, " + parameter.Parameter.Name + ")"),
            IPropertyReferenceOperation property when IsArrayLength(property) && property.Instance is IParameterReferenceOperation array =>
                new(array.Parameter.Name + ".Length"),
            _ => null
        };
    }
}
