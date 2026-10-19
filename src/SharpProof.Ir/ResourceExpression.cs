using System.Numerics;
using System.Globalization;

namespace SharpProof.Ir;

internal enum ResourceInputKind { NonNegativeScalar, ArrayLength, StringLength }
internal enum ResourceExpressionKind { Constant, Input, Add, Multiply, Maximum, PositiveDifference, CeilingDivide, Digits, CeilingLog, Align }

internal sealed record ResourceInput
{
    internal int Ordinal { get; }
    internal ResourceInputKind Kind { get; }
    internal string DisplayName { get; }
    internal BigInteger Maximum { get; }

    internal ResourceInput(int ordinal, ResourceInputKind kind, string displayName, BigInteger maximum)
    {
        displayName = ArgumentNullGuard.NotNull(displayName, nameof(displayName));
        if (ordinal < 0)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
        if (!Enum.IsDefined(typeof(ResourceInputKind), kind))
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        if (maximum < 0)
        { throw new ArgumentOutOfRangeException(nameof(maximum)); }
        if (maximum.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        if (displayName.Length == 0 || displayName.Any(static value => char.IsControl(value)))
        { throw new ArgumentException("An input display name must be nonempty and contain no control characters.", nameof(displayName)); }
        if (displayName.Length > ResourceExpression.MaximumTextLength)
        { throw new ResourceExpressionLimitException(); }
        Ordinal = ordinal;
        Kind = kind;
        DisplayName = displayName;
        Maximum = maximum;
    }

    public bool Equals(ResourceInput? other)
    {
        return other is not null && Ordinal == other.Ordinal && Kind == other.Kind && Maximum == other.Maximum;
    }

    public override int GetHashCode()
    {
        unchecked
        { return ((Ordinal * 397) ^ (int)Kind) * 397 ^ Maximum.GetHashCode(); }
    }
}

public sealed class ResourceExpressionLimitException : Exception
{
    public ResourceExpressionLimitException() : base("Resource expression construction budget exceeded.") { }
    public ResourceExpressionLimitException(string? message) : base(message) { }
    public ResourceExpressionLimitException(string? message, Exception? innerException) : base(message, innerException) { }
}

// Nonnegative mathematical arithmetic, independent of Roslyn and CLR wrapping.
// Input ordinals/kinds are semantic identity; display names never establish it.
// Native consumers must additionally bind inputs to the owned callable's types.
internal sealed class ResourceExpression : IEquatable<ResourceExpression>
{
    internal const int ModelVersion = 1;
    internal const int MaximumNodes = 4096;
    internal const int MaximumDepth = 128;
    internal const int MaximumTextLength = 16384;
    internal ResourceExpressionKind Kind { get; }
    internal BigInteger Number { get; }
    internal ResourceInput? Input { get; }
    internal ImmutableArray<ResourceExpression> Operands { get; }
    internal int NodeCount { get; }
    internal int Depth { get; }
    internal BigInteger? Maximum { get; }
    internal BigInteger? Constant => Kind == ResourceExpressionKind.Constant ? Number : null;
    internal static ResourceExpression Zero { get; } = Literal(0);
    internal static ResourceExpression One { get; } = Literal(1);
    private string? _display;

    private ResourceExpression(ResourceExpressionKind kind, BigInteger number, ResourceInput? input,
        ImmutableArray<ResourceExpression> operands, BigInteger? maximum)
    {
        if (operands.IsDefault)
        { throw new ArgumentException("Operands must be initialized.", nameof(operands)); }
        Kind = kind;
        if (number.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        Number = number;
        Input = input;
        Operands = operands;
        Maximum = maximum;
        if (maximum is { } limit && limit.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        int nodes = 1, depth = 1;
        foreach (var operand in operands)
        {
            if (operand is null)
            { throw new ArgumentException("Operands must not contain null.", nameof(operands)); }
            nodes += operand.NodeCount;
            depth = Math.Max(depth, 1 + operand.Depth);
        }
        if (nodes > MaximumNodes || depth > MaximumDepth)
        { throw new ResourceExpressionLimitException(); }
        NodeCount = nodes;
        Depth = depth;
    }

    internal static ResourceExpression Literal(BigInteger value)
    {
        if (value < 0)
        { throw new ArgumentOutOfRangeException(nameof(value)); }
        if (value.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        return new(ResourceExpressionKind.Constant, value, null, [], value);
    }

    internal static ResourceExpression InputSize(ResourceInput input)
    {
        input = ArgumentNullGuard.NotNull(input, nameof(input));
        return new(ResourceExpressionKind.Input, 0, input, [], input.Maximum);
    }

    internal static ResourceExpression Add(ResourceExpression left, ResourceExpression right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (left.Constant == 0)
        { return right; }
        if (right.Constant == 0)
        { return left; }
        if (left.Constant is { } a && right.Constant is { } b)
        { return Literal(a + b); }
        return new(ResourceExpressionKind.Add, 0, null, [left, right],
            left.Maximum is { } x && right.Maximum is { } y ? x + y : null);
    }

    internal static ResourceExpression Multiply(ResourceExpression left, ResourceExpression right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (left.Constant == 0 || right.Constant == 0)
        { return Zero; }
        if (left.Constant == 1)
        { return right; }
        if (right.Constant == 1)
        { return left; }
        if (left.Constant is { } a && right.Constant is { } b)
        { return Literal(a * b); }
        return new(ResourceExpressionKind.Multiply, 0, null, [left, right],
            left.Maximum is { } x && right.Maximum is { } y ? x * y : null);
    }

    internal static ResourceExpression Max(ResourceExpression left, ResourceExpression right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        if (left.Equals(right))
        { return left; }
        if (left.Constant is { } a && right.Constant is { } b)
        { return Literal(BigInteger.Max(a, b)); }
        return new(ResourceExpressionKind.Maximum, 0, null, [left, right],
            left.Maximum is { } x && right.Maximum is { } y ? BigInteger.Max(x, y) : null);
    }

    internal static ResourceExpression Difference(ResourceExpression value, BigInteger lower)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        if (lower < 0)
        { throw new ArgumentOutOfRangeException(nameof(lower)); }
        if (lower == 0)
        { return value; }
        if (value.Constant is { } number)
        { return Literal(BigInteger.Max(0, number - lower)); }
        return new(ResourceExpressionKind.PositiveDifference, lower, null, [value],
            value.Maximum is { } maximum ? BigInteger.Max(0, maximum - lower) : null);
    }

    internal static ResourceExpression DivideCeiling(ResourceExpression value, BigInteger denominator)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        if (denominator <= 0)
        { throw new ArgumentOutOfRangeException(nameof(denominator)); }
        if (denominator == 1)
        { return value; }
        if (value.Constant is { } number)
        { return Literal((number + denominator - 1) / denominator); }
        return new(ResourceExpressionKind.CeilingDivide, denominator, null, [value],
            value.Maximum is { } maximum ? (maximum + denominator - 1) / denominator : null);
    }

    internal static ResourceExpression Geometric(ResourceExpression value, BigInteger factor, bool descending, CancellationToken cancellation)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        cancellation.ThrowIfCancellationRequested();
        if (factor <= 1)
        { throw new ArgumentOutOfRangeException(nameof(factor)); }
        if (value.Constant is { } number)
        { return Literal(CountGeometric(number, factor, descending, cancellation)); }
        return new(descending ? ResourceExpressionKind.Digits : ResourceExpressionKind.CeilingLog, factor, null, [value],
            value.Maximum is { } maximum ? CountGeometric(maximum, factor, descending, cancellation) : null);
    }

    internal static ResourceExpression Aligned(ResourceExpression value, BigInteger alignment)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        if (alignment <= 0)
        { throw new ArgumentOutOfRangeException(nameof(alignment)); }
        if (alignment == 1)
        { return value; }
        if (value.Constant is { } number)
        { return Literal(((number + alignment - 1) / alignment) * alignment); }
        return new(ResourceExpressionKind.Align, alignment, null, [value],
            value.Maximum is { } maximum ? ((maximum + alignment - 1) / alignment) * alignment : null);
    }

    private static int CountGeometric(BigInteger value, BigInteger factor, bool descending, CancellationToken cancellation)
    {
        int count = 0;
        if (descending)
        {
            while (value > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++count > MaximumNodes)
                { throw new ResourceExpressionLimitException(); }
                value /= factor;
            }
        }
        else
        {
            var progress = BigInteger.One;
            while (progress < value)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++count > MaximumNodes)
                { throw new ResourceExpressionLimitException(); }
                progress *= factor;
            }
        }
        return count;
    }

    internal BigInteger Evaluate(Func<ResourceInput, BigInteger> inputs, CancellationToken cancellation)
    {
        inputs = ArgumentNullGuard.NotNull(inputs, nameof(inputs));
        cancellation.ThrowIfCancellationRequested();
        if (Kind == ResourceExpressionKind.Constant)
        { return Number; }
        if (Kind == ResourceExpressionKind.Input)
        {
            var input = Input!;
            var value = inputs(input);
            if (value > input.Maximum || value < 0 && input.Kind != ResourceInputKind.NonNegativeScalar)
            { throw new ArgumentOutOfRangeException(nameof(inputs), "Input is outside its declared size domain."); }
            return BigInteger.Max(0, value);
        }
        var left = Operands[0].Evaluate(inputs, cancellation);
        var right = Operands.Length > 1 ? Operands[1].Evaluate(inputs, cancellation) : BigInteger.Zero;
        return Kind switch
        {
            ResourceExpressionKind.Add => left + right,
            ResourceExpressionKind.Multiply => left * right,
            ResourceExpressionKind.Maximum => BigInteger.Max(left, right),
            ResourceExpressionKind.PositiveDifference => BigInteger.Max(0, left - Number),
            ResourceExpressionKind.CeilingDivide => (left + Number - 1) / Number,
            ResourceExpressionKind.Digits => CountGeometric(left, Number, true, cancellation),
            ResourceExpressionKind.CeilingLog => CountGeometric(left, Number, false, cancellation),
            ResourceExpressionKind.Align => ((left + Number - 1) / Number) * Number,
            _ => throw new InvalidOperationException("Unsupported resource expression kind.")
        };
    }

    internal string ToDisplayString()
    {
        if (_display is { } cached)
        { return cached; }
        var number = Number.ToString(CultureInfo.InvariantCulture);
        if (Kind == ResourceExpressionKind.Constant)
        { return SetDisplay(number); }
        if (Kind == ResourceExpressionKind.Input)
        { return SetDisplay(Input!.Kind == ResourceInputKind.NonNegativeScalar ? $"max(0, {Input.DisplayName})" : Input.DisplayName + ".Length"); }
        var left = Operands[0].ToDisplayString();
        var right = Operands.Length > 1 ? Operands[1].ToDisplayString() : string.Empty;
        var result = Kind switch
        {
            ResourceExpressionKind.Add => $"({left} + {right})",
            ResourceExpressionKind.Multiply => $"({left} * {right})",
            ResourceExpressionKind.Maximum => $"max({left}, {right})",
            ResourceExpressionKind.PositiveDifference => $"max(0, ({left} - {number}))",
            ResourceExpressionKind.CeilingDivide => $"ceil({left} / {number})",
            ResourceExpressionKind.Digits => $"digits({left}, {number})",
            ResourceExpressionKind.CeilingLog => $"ceil_log({left}, {number})",
            ResourceExpressionKind.Align => $"align({left}, {number})",
            _ => throw new InvalidOperationException("Unsupported resource expression kind.")
        };
        return SetDisplay(result);
    }

    private string SetDisplay(string text)
    {
        text = ArgumentNullGuard.NotNull(text, nameof(text));
        if (text.Length > MaximumTextLength)
        { throw new ResourceExpressionLimitException(); }
        _display = text;
        return text;
    }

    public bool Equals(ResourceExpression? other)
    {
        if (ReferenceEquals(this, other))
        { return true; }
        if (other is null || Kind != other.Kind || Number != other.Number || Input != other.Input || Operands.Length != other.Operands.Length)
        { return false; }
        for (var index = 0; index < Operands.Length; index++)
        { if (!Operands[index].Equals(other.Operands[index])) { return false; } }
        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourceExpression expression && Equals(expression);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = ((int)Kind * 397) ^ Number.GetHashCode();
            hash = (hash * 397) ^ (Input?.GetHashCode() ?? 0);
            foreach (var operand in Operands)
            { hash = (hash * 397) ^ operand.GetHashCode(); }
            return hash;
        }
    }
}
