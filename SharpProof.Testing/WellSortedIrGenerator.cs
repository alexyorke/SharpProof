using System.Diagnostics.CodeAnalysis;
using SharpProof.Ir;

namespace SharpProof.Testing;

public sealed record GeneratedIrCase(
    IrTerm Term,
    IReadOnlyDictionary<IrVarId, IrValue> Variables,
    GeneratedIrCategory Category = GeneratedIrCategory.Arithmetic);

public enum GeneratedIrCategory
{
    Arithmetic,
    Boolean,
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "String is the corresponding IR vocabulary category.")]
    String,
    StringLength,
    NullCast,
    ArrayLength,
    ArrayIndex
}

public static class DifferentialIntegerCorpus
{
    public static IReadOnlyList<long> InterestingIntegers { get; } = [
        long.MinValue,
        -3,
        -1,
        0,
        1,
        2,
        3,
        long.MaxValue
    ];
}

[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "The seeded generator intentionally produces deterministic test cases.")]
public sealed class WellSortedIrGenerator(IrFactory factory, int seed)
{
    private static readonly IrBinaryOperator[] IntegerOperators = [
        IrBinaryOperator.Add, IrBinaryOperator.Subtract, IrBinaryOperator.Multiply,
        IrBinaryOperator.Divide, IrBinaryOperator.Remainder];
    private static readonly IrBinaryOperator[] ComparisonOperators = [
        IrBinaryOperator.Equal, IrBinaryOperator.NotEqual, IrBinaryOperator.LessThan,
        IrBinaryOperator.LessThanOrEqual, IrBinaryOperator.GreaterThan, IrBinaryOperator.GreaterThanOrEqual];

    private readonly IrFactory _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly Random _random = new(seed);
    private readonly IrVarId _left = factory.CreateVariable("left", factory.IntegerType);
    private readonly IrVarId _right = factory.CreateVariable("right", factory.IntegerType);
    private readonly IrVarId _condition = factory.CreateVariable("condition", factory.BooleanType);
    private readonly IrVarId _text = factory.CreateVariable("text", factory.StringType);
    private readonly IrVarId _reference = factory.CreateVariable("reference", factory.ObjectType);
    private readonly IrTypeId _integerSequence = factory.GetOrCreateSequenceType(factory.IntegerType);
    private readonly IrVarId _values = factory.CreateVariable(
        "values",
        factory.GetOrCreateSequenceType(factory.IntegerType));

    public GeneratedIrCase Next(int maximumDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        var category = (GeneratedIrCategory)_random.Next(
            Enum.GetValues<GeneratedIrCategory>().Length);
        var term = category switch
        {
            GeneratedIrCategory.Arithmetic => Integer(maximumDepth),
            GeneratedIrCategory.Boolean => Boolean(maximumDepth),
            GeneratedIrCategory.String => String(maximumDepth),
            GeneratedIrCategory.StringLength => _factory.Length(String(maximumDepth)),
            GeneratedIrCategory.NullCast => _factory.Cast(
                _factory.StringType,
                _factory.Variable(_reference)),
            GeneratedIrCategory.ArrayLength => _factory.Length(_factory.Variable(_values)),
            GeneratedIrCategory.ArrayIndex => _factory.SequenceAccess(
                _factory.Variable(_values),
                Integer(Math.Min(maximumDepth, 1))),
            _ => throw new InvalidOperationException()
        };
        return CreateCase(term, category);
    }

    public GeneratedIrCase NextArithmeticOrBoolean(int maximumDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        var category = _random.Next(2) == 0
            ? GeneratedIrCategory.Arithmetic
            : GeneratedIrCategory.Boolean;
        var term = category == GeneratedIrCategory.Arithmetic
            ? Integer(maximumDepth)
            : Boolean(maximumDepth);
        return CreateCase(term, category);
    }

    private GeneratedIrCase CreateCase(
        IrTerm term,
        GeneratedIrCategory category)
    {
        var referenced = CollectVariables(term);
        var textChoice = _random.Next(4);
        var sequenceIsNull = _random.Next(4) == 0;
        var sequenceLength = sequenceIsNull ? 0 : _random.Next(4);
        long[]? sequenceElements = referenced.Contains(_values) && !sequenceIsNull
            ? new long[sequenceLength]
            : null;
        for (var index = 0; index < sequenceLength; index++)
        {
            var value = NextInteger();
            if (sequenceElements != null)
            {
                sequenceElements[index] = value;
            }
        }

        var leftValue = NextInteger();
        var rightValue = NextInteger();
        var conditionValue = _random.Next(2) == 0;
        var variables = new Dictionary<IrVarId, IrValue>();
        if (referenced.Contains(_left))
        {
            variables[_left] = _factory.CreateIntegerValue(leftValue);
        }
        if (referenced.Contains(_right))
        {
            variables[_right] = _factory.CreateIntegerValue(rightValue);
        }
        if (referenced.Contains(_condition))
        {
            variables[_condition] = _factory.CreateBooleanValue(conditionValue);
        }
        if (referenced.Contains(_text))
        {
            variables[_text] = textChoice switch
            {
                0 => _factory.CreateNullValue(_factory.StringType),
                1 => _factory.CreateStringValue(""),
                2 => _factory.CreateStringValue("sharp"),
                _ => _factory.CreateStringValue("proof")
            };
        }
        if (referenced.Contains(_reference))
        {
            variables[_reference] = _factory.CreateNullValue(_factory.ObjectType);
        }
        if (referenced.Contains(_values))
        {
            variables[_values] = sequenceIsNull
                ? _factory.CreateNullValue(_integerSequence)
                : _factory.CreateSequenceValue(
                    _integerSequence,
                    sequenceElements!.Select(_factory.CreateIntegerValue));
        }
        return new GeneratedIrCase(term, variables, category);
    }

    private static HashSet<IrVarId> CollectVariables(IrTerm root)
    {
        var variables = new HashSet<IrVarId>();
        var visited = new HashSet<IrId>();
        var pending = new Stack<IrTerm>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            if (current is IrVariableTerm variable)
            {
                variables.Add(variable.Variable);
            }

            switch (current)
            {
                case IrOpaqueTerm opaque:
                    if (opaque.Receiver is { } receiver)
                    {
                        pending.Push(receiver);
                    }
                    for (var index = 0; index < opaque.Arguments.Length; index++)
                    {
                        pending.Push(opaque.Arguments[index]);
                    }
                    break;
                case IrUnaryTerm unary:
                    pending.Push(unary.Operand);
                    break;
                case IrBinaryTerm binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case IrConditionalTerm conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.WhenFalse);
                    break;
                case IrCastTerm cast:
                    pending.Push(cast.Operand);
                    break;
                case IrLengthTerm length:
                    pending.Push(length.Value);
                    break;
                case IrSequenceAccessTerm access:
                    pending.Push(access.Sequence);
                    pending.Push(access.Index);
                    break;
            }
        }
        return variables;
    }

    private IrTerm Integer(int depth)
    {
        if (depth == 0)
        {
            return _random.Next(3) switch
            {
                0 => _factory.Variable(_left),
                1 => _factory.Variable(_right),
                _ => _factory.Integer(NextInteger())
            };
        }

        return _random.Next(5) switch
        {
            0 => _factory.Unary(IrUnaryOperator.Negate, Integer(depth - 1)),
            1 => _factory.Conditional(
                Boolean(depth - 1),
                Integer(depth - 1),
                Integer(depth - 1)),
            _ => _factory.Binary(
                RandomIntegerOperator(),
                Integer(depth - 1),
                Integer(depth - 1))
        };
    }

    private IrTerm Boolean(int depth)
    {
        if (depth == 0)
        {
            return _random.Next(3) switch
            {
                0 => _factory.Variable(_condition),
                1 => _factory.Boolean(false),
                _ => _factory.Boolean(true)
            };
        }

        return _random.Next(5) switch
        {
            0 => _factory.Unary(IrUnaryOperator.Not, Boolean(depth - 1)),
            1 => _factory.Binary(
                _random.Next(2) == 0
                    ? IrBinaryOperator.AndAlso
                    : IrBinaryOperator.OrElse,
                Boolean(depth - 1),
                Boolean(depth - 1)),
            2 => _factory.Conditional(
                Boolean(depth - 1),
                Boolean(depth - 1),
                Boolean(depth - 1)),
            _ => _factory.Binary(
                RandomComparisonOperator(),
                Integer(depth - 1),
                Integer(depth - 1))
        };
    }

    private IrTerm String(int depth)
    {
        if (depth == 0)
        {
            return _random.Next(5) switch
            {
                0 => _factory.Variable(_text),
                1 => _factory.Null(_factory.StringType),
                2 => _factory.String(""),
                3 => _factory.String("sharp"),
                _ => _factory.String("proof")
            };
        }

        return _random.Next(3) switch
        {
            0 => _factory.Conditional(
                Boolean(depth - 1),
                String(depth - 1),
                String(depth - 1)),
            1 => _factory.Binary(
                IrBinaryOperator.StringConcat,
                _factory.Variable(_text),
                _factory.String("proof")),
            _ => String(0)
        };
    }

    private IrBinaryOperator RandomIntegerOperator()
    {
        return IntegerOperators[_random.Next(IntegerOperators.Length)];
    }

    private IrBinaryOperator RandomComparisonOperator()
    {
        return ComparisonOperators[_random.Next(ComparisonOperators.Length)];
    }

    private long NextInteger()
    {
        var value = DifferentialIntegerCorpus.InterestingIntegers[
            _random.Next(DifferentialIntegerCorpus.InterestingIntegers.Count)];
        // Total factories use signed 32-bit integers by default. Keep boundary
        // samples at that width instead of constructing invalid 64-bit literals.
        return _factory.Semantics == IrExecutionSemantics.Total
            ? Math.Max(int.MinValue, Math.Min(int.MaxValue, value))
            : value;
    }
}
