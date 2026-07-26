using SharpProof.Ir;

namespace SharpProof.Testing;

public sealed record GeneratedIrCase(
    IrTerm Term,
    IReadOnlyDictionary<IrVarId, IrValue> Variables,
    GeneratedIrCategory Category = GeneratedIrCategory.Arithmetic);

public enum GeneratedIrCategory {
    Arithmetic,
    Boolean,
    String,
    StringLength,
    NullCast,
    ArrayLength,
    ArrayIndex
}

public sealed class WellSortedIrGenerator(IrFactory factory, int seed) {
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

    public GeneratedIrCase Next(int maximumDepth = 4) {
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        var category = (GeneratedIrCategory)_random.Next(7);
        var term = category switch {
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

    public GeneratedIrCase NextArithmeticOrBoolean(int maximumDepth = 4) {
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
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
        GeneratedIrCategory category) {
        var text = _random.Next(4) switch {
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
        var variables = new Dictionary<IrVarId, IrValue> {
            [_left] = _factory.CreateIntegerValue(NextInteger()),
            [_right] = _factory.CreateIntegerValue(NextInteger()),
            [_condition] = _factory.CreateBooleanValue(_random.Next(2) == 0),
            [_text] = text,
            [_reference] = _factory.CreateNullValue(_factory.ObjectType),
            [_values] = sequence
        };
        return new GeneratedIrCase(term, variables, category);
    }

    private IrTerm Integer(int depth) {
        if (depth == 0) {
            return _random.Next(3) switch {
                0 => _factory.Variable(_left),
                1 => _factory.Variable(_right),
                _ => _factory.Integer(NextInteger())
            };
        }

        return _random.Next(5) switch {
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

    private IrTerm Boolean(int depth) {
        if (depth == 0) {
            return _random.Next(3) switch {
                0 => _factory.Variable(_condition),
                1 => _factory.Boolean(false),
                _ => _factory.Boolean(true)
            };
        }

        return _random.Next(5) switch {
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

    private IrTerm String(int depth) {
        if (depth == 0) {
            return _random.Next(5) switch {
                0 => _factory.Variable(_text),
                1 => _factory.Null(_factory.StringType),
                2 => _factory.String(""),
                3 => _factory.String("sharp"),
                _ => _factory.String("proof")
            };
        }

        return _random.Next(3) switch {
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

    private IrBinaryOperator RandomIntegerOperator() => _random.Next(5) switch {
        0 => IrBinaryOperator.Add,
        1 => IrBinaryOperator.Subtract,
        2 => IrBinaryOperator.Multiply,
        3 => IrBinaryOperator.Divide,
        _ => IrBinaryOperator.Remainder
    };

    private IrBinaryOperator RandomComparisonOperator() => _random.Next(6) switch {
        0 => IrBinaryOperator.Equal,
        1 => IrBinaryOperator.NotEqual,
        2 => IrBinaryOperator.LessThan,
        3 => IrBinaryOperator.LessThanOrEqual,
        4 => IrBinaryOperator.GreaterThan,
        _ => IrBinaryOperator.GreaterThanOrEqual
    };

    private long NextInteger() =>
        InterestingIntegers[_random.Next(InterestingIntegers.Length)];
}
