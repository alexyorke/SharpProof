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

[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "The seeded generator intentionally produces deterministic test cases.")]
public sealed class WellSortedIrGenerator(IrFactory factory, int seed)
{
    private const int DefaultMaximumNodes = 4096;

    private static readonly long[] InterestingIntegers = [
        long.MinValue,
        -3,
        -1,
        0,
        1,
        2,
        3,
        long.MaxValue
    ];

    private readonly IrFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly DeterministicRandom _random = new(seed);
    private readonly IrVarId _left = factory.CreateVariable("left", factory.IntegerType);
    private readonly IrVarId _right = factory.CreateVariable("right", factory.IntegerType);
    private readonly IrVarId _condition = factory.CreateVariable("condition", factory.BooleanType);
    private readonly IrVarId _text = factory.CreateVariable("text", factory.StringType);
    private readonly IrVarId _reference = factory.CreateVariable("reference", factory.ObjectType);
    private readonly IrTypeId _integerSequence = factory.GetOrCreateSequenceType(factory.IntegerType);
    private readonly IrVarId _values = factory.CreateVariable(
        "values",
        factory.GetOrCreateSequenceType(factory.IntegerType));
    private int _remainingNodes;

    public GeneratedIrCase Next(
        int maximumDepth = 4,
        int maximumNodes = DefaultMaximumNodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumNodes, 3);
        _remainingNodes = maximumNodes;
        var category = (GeneratedIrCategory)_random.Next(7);
        var term = category switch
        {
            GeneratedIrCategory.Arithmetic => Integer(maximumDepth),
            GeneratedIrCategory.Boolean => Boolean(maximumDepth),
            GeneratedIrCategory.String => String(maximumDepth),
            GeneratedIrCategory.StringLength => CreateStringLength(maximumDepth),
            GeneratedIrCategory.NullCast => CreateNullCast(),
            GeneratedIrCategory.ArrayLength => CreateArrayLength(),
            GeneratedIrCategory.ArrayIndex => CreateArrayIndex(maximumDepth),
            _ => throw new InvalidOperationException()
        };
        return CreateCase(term, category);
    }

    public GeneratedIrCase NextArithmeticOrBoolean(
        int maximumDepth = 4,
        int maximumNodes = DefaultMaximumNodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumNodes, 1);
        _remainingNodes = maximumNodes;
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
        var text = _random.Next(4) switch
        {
            0 => (IrValue)_factory.CreateNullValue(_factory.StringType),
            1 => _factory.CreateStringValue(""),
            2 => _factory.CreateStringValue("sharp"),
            _ => _factory.CreateStringValue("proof")
        };
        var sequence = _random.Next(4) == 0
            ? _factory.CreateNullValue(_integerSequence)
            : _factory.CreateSequenceValue(
                _integerSequence,
                Enumerable.Range(0, _random.Next(4))
                    .Select(_ => _factory.CreateIntegerValue(NextInteger())));
        var reference = _random.Next(3) switch
        {
            0 => _factory.CreateNullValue(_factory.ObjectType),
            1 => _factory.CreateReferenceValue(_factory.ObjectType, "sharp"),
            _ => _factory.CreateReferenceValue(_factory.ObjectType, new object())
        };
        var variables = new Dictionary<IrVarId, IrValue>
        {
            [_left] = _factory.CreateIntegerValue(NextInteger()),
            [_right] = _factory.CreateIntegerValue(NextInteger()),
            [_condition] = _factory.CreateBooleanValue(_random.Next(2) == 0),
            [_text] = text,
            [_reference] = reference,
            [_values] = sequence
        };
        return new GeneratedIrCase(term, variables, category);
    }

    private IrTerm Integer(int depth)
    {
        if (depth == 0)
        {
            return IntegerLeaf();
        }

        var choice = _random.Next(5);
        var childCount = choice switch
        {
            0 => 1,
            1 => 3,
            _ => 2
        };
        if (!ReserveExpansion(childCount))
        {
            return IntegerLeaf();
        }

        return choice switch
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
            return BooleanLeaf();
        }

        var choice = _random.Next(5);
        var childCount = choice switch
        {
            0 => 1,
            1 or 2 => 3,
            _ => 2
        };
        if (!ReserveExpansion(childCount))
        {
            return BooleanLeaf();
        }

        return choice switch
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
            return StringLeaf();
        }

        var choice = _random.Next(3);
        var childCount = choice == 0 ? 3 : choice == 1 ? 2 : 1;
        if (!ReserveExpansion(childCount))
        {
            return StringLeaf();
        }

        return choice switch
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

    private IrTerm CreateStringLength(int depth)
    {
        ConsumeNodes(1);
        return _factory.Length(String(depth));
    }

    private IrTerm CreateNullCast()
    {
        ConsumeNodes(2);
        return _factory.Cast(
            _factory.StringType,
            _factory.Variable(_reference));
    }

    private IrTerm CreateArrayLength()
    {
        ConsumeNodes(2);
        return _factory.Length(_factory.Variable(_values));
    }

    private IrTerm CreateArrayIndex(int depth)
    {
        ConsumeNodes(2);
        return _factory.SequenceAccess(
            _factory.Variable(_values),
            Integer(Math.Min(depth, 1)));
    }

    private bool ReserveExpansion(int childCount)
    {
        // Reserve the parent and the minimum one node for every child. Keep a
        // small slack slot for factory canonicalization and mixed-type
        // branches so the public budget remains a hard cap on the resulting
        // graph, not just on recursive calls.
        if (_remainingNodes < childCount + 2 || _random.Next(5) == 0)
        {
            return false;
        }

        _remainingNodes -= 2;
        return true;
    }

    private void ConsumeNodes(int count)
    {
        if (count < 0 || _remainingNodes < count)
        {
            throw new InvalidOperationException(
                "The generated IR node budget cannot represent this category.");
        }

        _remainingNodes -= count;
    }

    private IrTerm IntegerLeaf()
    {
        ConsumeLeaf();
        return _random.Next(3) switch
        {
            0 => _factory.Variable(_left),
            1 => _factory.Variable(_right),
            _ => _factory.Integer(NextInteger())
        };
    }

    private IrTerm BooleanLeaf()
    {
        ConsumeLeaf();
        return _random.Next(3) switch
        {
            0 => _factory.Variable(_condition),
            1 => _factory.Boolean(false),
            _ => _factory.Boolean(true)
        };
    }

    private IrTerm StringLeaf()
    {
        ConsumeLeaf();
        return _random.Next(5) switch
        {
            0 => _factory.Variable(_text),
            1 => _factory.Null(_factory.StringType),
            2 => _factory.String(""),
            3 => _factory.String("sharp"),
            _ => _factory.String("proof")
        };
    }

    private void ConsumeLeaf()
    {
        if (_remainingNodes > 0)
        {
            _remainingNodes--;
        }
    }

    private IrBinaryOperator RandomIntegerOperator()
    {
        return _random.Next(5) switch
        {
            0 => IrBinaryOperator.Add,
            1 => IrBinaryOperator.Subtract,
            2 => IrBinaryOperator.Multiply,
            3 => IrBinaryOperator.Divide,
            _ => IrBinaryOperator.Remainder
        };
    }

    private IrBinaryOperator RandomComparisonOperator()
    {
        return _random.Next(6) switch
        {
            0 => IrBinaryOperator.Equal,
            1 => IrBinaryOperator.NotEqual,
            2 => IrBinaryOperator.LessThan,
            3 => IrBinaryOperator.LessThanOrEqual,
            4 => IrBinaryOperator.GreaterThan,
            _ => IrBinaryOperator.GreaterThanOrEqual
        };
    }

    private long NextInteger()
    {
        return InterestingIntegers[_random.Next(InterestingIntegers.Length)];
    }
}
