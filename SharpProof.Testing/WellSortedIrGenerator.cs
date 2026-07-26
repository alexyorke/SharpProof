using SharpProof.Ir;

namespace SharpProof.Testing;

public sealed record GeneratedIrCase(
    IrTerm Term,
    IReadOnlyDictionary<IrVarId, IrValue> Variables);

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

    public GeneratedIrCase Next(int maximumDepth = 4) {
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        var term = _random.Next(2) == 0
            ? Integer(maximumDepth)
            : Boolean(maximumDepth);
        var variables = new Dictionary<IrVarId, IrValue> {
            [_left] = _factory.CreateIntegerValue(NextInteger()),
            [_right] = _factory.CreateIntegerValue(NextInteger()),
            [_condition] = _factory.CreateBooleanValue(_random.Next(2) == 0)
        };
        return new GeneratedIrCase(term, variables);
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
