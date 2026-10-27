using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz;

internal enum GeneratedExpressionType
{
    Boolean,
    Integer,
    String,
    Sequence,
    Reference
}

internal enum GeneratedExpressionKind
{
    BooleanLiteral,
    IntegerLiteral,
    LeftParameter,
    RightParameter,
    ConditionParameter,
    TextParameter,
    ValuesParameter,
    ReferenceParameter,
    NullReference,
    StringLiteral,
    NullString,
    Not,
    Negate,
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    AndAlso,
    OrElse,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    StringConcat,
    Length,
    ArrayIndex,
    CastToString,
    Conditional
}

internal sealed class GeneratedCSharpExpression
{
    private GeneratedCSharpExpression(
        GeneratedExpressionKind kind,
        GeneratedExpressionType type,
        long integerValue,
        bool booleanValue,
        ImmutableArray<GeneratedCSharpExpression> children,
        string? stringValue = null)
    {

        RepositoryParameterGuard.RequireInitialized(children.IsDefault, nameof(children), "Array must be initialized.");
        Kind = kind;
        Type = type;
        IntegerValue = integerValue;
        BooleanValue = booleanValue;
        StringValue = stringValue;
        Children = children;
        NodeCount = 1 + children.Sum(static child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.NodeCount));
    }

    public GeneratedExpressionKind Kind { get; }
    public GeneratedExpressionType Type { get; }
    public long IntegerValue { get; }
    public bool BooleanValue { get; }
    public string? StringValue { get; }
    public ImmutableArray<GeneratedCSharpExpression> Children { get; }
    public int NodeCount { get; }

    public static GeneratedCSharpExpression Boolean(bool value)
    {
        return new(
            GeneratedExpressionKind.BooleanLiteral,
            GeneratedExpressionType.Boolean,
            0,
            value,
            []);
    }

    public static GeneratedCSharpExpression Integer(long value)
    {
        return new(
            GeneratedExpressionKind.IntegerLiteral,
            GeneratedExpressionType.Integer,
            value,
            false,
            []);
    }

    public static GeneratedCSharpExpression Left()
    {
        return Leaf(
            GeneratedExpressionKind.LeftParameter,
            GeneratedExpressionType.Integer);
    }

    public static GeneratedCSharpExpression Right()
    {
        return Leaf(
            GeneratedExpressionKind.RightParameter,
            GeneratedExpressionType.Integer);
    }

    public static GeneratedCSharpExpression Condition()
    {
        return Leaf(
            GeneratedExpressionKind.ConditionParameter,
            GeneratedExpressionType.Boolean);
    }

    public static GeneratedCSharpExpression Text()
    {
        return Leaf(
            GeneratedExpressionKind.TextParameter,
            GeneratedExpressionType.String);
    }

    public static GeneratedCSharpExpression Values()
    {
        return Leaf(
            GeneratedExpressionKind.ValuesParameter,
            GeneratedExpressionType.Sequence);
    }

    public static GeneratedCSharpExpression Reference()
    {
        return Leaf(
            GeneratedExpressionKind.ReferenceParameter,
            GeneratedExpressionType.Reference);
    }

    public static GeneratedCSharpExpression NullReference()
    {
        return Leaf(
            GeneratedExpressionKind.NullReference,
            GeneratedExpressionType.Reference);
    }

    public static GeneratedCSharpExpression String(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.StringLiteral,
            GeneratedExpressionType.String,
            0,
            false,
            [],
            value);
    }

    public static GeneratedCSharpExpression NullString()
    {
        return Leaf(
            GeneratedExpressionKind.NullString,
            GeneratedExpressionType.String);
    }

    private static GeneratedCSharpExpression Leaf(
        GeneratedExpressionKind kind,
        GeneratedExpressionType type)
    {
        return new(kind, type, 0, false, []);
    }

    public static GeneratedCSharpExpression Length(
        GeneratedCSharpExpression value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Type is not (
            GeneratedExpressionType.String or GeneratedExpressionType.Sequence))
        {
            throw new ArgumentException(
                "Length requires a generated string or sequence.",
                nameof(value));
        }

        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.Length,
            GeneratedExpressionType.Integer,
            0,
            false,
            [value]);
    }

    public static GeneratedCSharpExpression ArrayIndex(
        GeneratedCSharpExpression sequence,
        GeneratedCSharpExpression index)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (index == null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        if (sequence.Type != GeneratedExpressionType.Sequence ||
            index.Type != GeneratedExpressionType.Integer)
        {
            throw new ArgumentException(
                "Array access requires a generated sequence and integer.");
        }

        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.ArrayIndex,
            GeneratedExpressionType.Integer,
            0,
            false,
            [sequence, index]);
    }

    public static GeneratedCSharpExpression CastToString(
        GeneratedCSharpExpression value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        // The Total semantics model string as its own value domain, so the
        // cast the verifier can check is the identity conversion.
        if (value.Type != GeneratedExpressionType.String)
        {
            throw new ArgumentException(
                "The generated cast requires a string operand.",
                nameof(value));
        }

        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.CastToString,
            GeneratedExpressionType.String,
            0,
            false,
            [value]);
    }

    public static GeneratedCSharpExpression Unary(
        GeneratedExpressionKind kind,
        GeneratedCSharpExpression operand)
    {
        if (operand == null)
        {
            throw new ArgumentNullException(nameof(operand));
        }

        var expected = kind switch
        {
            GeneratedExpressionKind.Not => GeneratedExpressionType.Boolean,
            GeneratedExpressionKind.Negate => GeneratedExpressionType.Integer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (operand.Type != expected)
        {
            throw new ArgumentException(
                "The unary operand has the wrong generated type.",
                nameof(operand));
        }

        return new GeneratedCSharpExpression(
            kind,
            expected,
            0,
            false,
            [operand]);
    }

    public static GeneratedCSharpExpression Binary(
        GeneratedExpressionKind kind,
        GeneratedCSharpExpression left,
        GeneratedCSharpExpression right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        var operation = GetBinaryOperator(kind);
        if (operation.OperandType == null)
        {
            if (left.Type != right.Type ||
                left.Type is not (
                    GeneratedExpressionType.Integer or
                    GeneratedExpressionType.String or
                    GeneratedExpressionType.Sequence or
                    GeneratedExpressionType.Reference))
            {
                throw new ArgumentException(
                    "Equality operands have incompatible generated types.");
            }
        }
        else if (operation.OperandType == GeneratedExpressionType.String)
        {
            if (left.Type != GeneratedExpressionType.String ||
                right.Type != GeneratedExpressionType.String)
            {
                throw new ArgumentException(
                    "String concatenation requires generated strings.");
            }
        }
        else if (left.Type != operation.OperandType ||
            right.Type != operation.OperandType)
        {
            throw new ArgumentException("The binary operands have the wrong generated type.");
        }

        return new GeneratedCSharpExpression(
            kind,
            operation.ResultType,
            0,
            false,
            [left, right]);
    }

    public static GeneratedCSharpExpression Conditional(
        GeneratedCSharpExpression condition,
        GeneratedCSharpExpression whenTrue,
        GeneratedCSharpExpression whenFalse)
    {
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (whenTrue == null)
        {
            throw new ArgumentNullException(nameof(whenTrue));
        }

        if (whenFalse == null)
        {
            throw new ArgumentNullException(nameof(whenFalse));
        }

        if (condition.Type != GeneratedExpressionType.Boolean)
        {
            throw new ArgumentException(
                "The conditional guard must be Boolean.",
                nameof(condition));
        }

        if (whenTrue.Type != whenFalse.Type)
        {
            throw new ArgumentException(
                "The conditional branches must have the same type.",
                nameof(whenFalse));
        }

        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.Conditional,
            whenTrue.Type,
            0,
            false,
            [condition, whenTrue, whenFalse]);
    }

    public string Render()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    internal bool TryEvaluateIntegerConstant(out long value)
    {
        if (Type != GeneratedExpressionType.Integer)
        {
            value = 0;
            return false;
        }
        try
        {
            switch (Kind)
            {
                case GeneratedExpressionKind.IntegerLiteral:
                    value = IntegerValue;
                    return true;
                case GeneratedExpressionKind.Negate
                    when Children[0].TryEvaluateIntegerConstant(out var operand):
                    value = checked(-operand);
                    return true;
                case GeneratedExpressionKind.Add
                    when TryEvaluateIntegerOperands(out var addLeft, out var addRight):
                    value = checked(addLeft + addRight);
                    return true;
                case GeneratedExpressionKind.Subtract
                    when TryEvaluateIntegerOperands(out var subtractLeft, out var subtractRight):
                    value = checked(subtractLeft - subtractRight);
                    return true;
                case GeneratedExpressionKind.Multiply
                    when TryEvaluateIntegerOperands(out var multiplyLeft, out var multiplyRight):
                    value = checked(multiplyLeft * multiplyRight);
                    return true;
                case GeneratedExpressionKind.Divide
                    when TryEvaluateIntegerOperands(out var divideLeft, out var divideRight):
                    value = checked(divideLeft / divideRight);
                    return true;
                case GeneratedExpressionKind.Remainder
                    when TryEvaluateIntegerOperands(out var remainderLeft, out var remainderRight):
                    value = remainderLeft % remainderRight;
                    return true;
                case GeneratedExpressionKind.Conditional
                    when TryEvaluateBooleanConstant(Children[0], out var condition):
                    return Children[condition ? 1 : 2]
                        .TryEvaluateIntegerConstant(out value);
            }
        }
        catch (ArithmeticException)
        {
        }
        value = 0;
        return false;
    }

    private bool TryEvaluateIntegerOperands(out long left, out long right)
    {
        right = 0;
        return Children[0].TryEvaluateIntegerConstant(out left) &&
            Children[1].TryEvaluateIntegerConstant(out right);
    }

    private static bool TryEvaluateBooleanConstant(
        GeneratedCSharpExpression expression,
        out bool value)
    {
        switch (expression.Kind)
        {
            case GeneratedExpressionKind.BooleanLiteral:
                value = expression.BooleanValue;
                return true;
            case GeneratedExpressionKind.Not
                when TryEvaluateBooleanConstant(expression.Children[0], out var operand):
                value = !operand;
                return true;
            case GeneratedExpressionKind.AndAlso:
            case GeneratedExpressionKind.OrElse:
                if (TryEvaluateBooleanConstant(expression.Children[0], out var left) &&
                    TryEvaluateBooleanConstant(expression.Children[1], out var right))
                {
                    value = expression.Kind == GeneratedExpressionKind.AndAlso
                        ? left && right
                        : left || right;
                    return true;
                }
                break;
            case GeneratedExpressionKind.Conditional
                when TryEvaluateBooleanConstant(expression.Children[0], out var condition):
                return TryEvaluateBooleanConstant(
                    expression.Children[condition ? 1 : 2],
                    out value);
        }
        value = false;
        return false;
    }

    private void AppendTo(StringBuilder builder)
    {
        builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
        switch (Kind)
        {
            case GeneratedExpressionKind.BooleanLiteral:
                builder.Append(BooleanValue ? "true" : "false");
                return;
            case GeneratedExpressionKind.IntegerLiteral:
                builder.Append('(');
                builder.Append(IntegerValue.ToString(CultureInfo.InvariantCulture));
                builder.Append("L)");
                return;
            case GeneratedExpressionKind.LeftParameter:
                builder.Append("left");
                return;
            case GeneratedExpressionKind.RightParameter:
                builder.Append("right");
                return;
            case GeneratedExpressionKind.ConditionParameter:
                builder.Append("condition");
                return;
            case GeneratedExpressionKind.TextParameter:
                builder.Append("text");
                return;
            case GeneratedExpressionKind.ValuesParameter:
                builder.Append("values");
                return;
            case GeneratedExpressionKind.ReferenceParameter:
                builder.Append("reference");
                return;
            case GeneratedExpressionKind.NullReference:
                builder.Append("((object)null!)");
                return;
            case GeneratedExpressionKind.StringLiteral:
                builder.Append(SymbolDisplay.FormatLiteral(StringValue!, quote: true));
                return;
            case GeneratedExpressionKind.NullString:
                builder.Append("((string)null!)");
                return;
            case GeneratedExpressionKind.Not:
            case GeneratedExpressionKind.Negate:
                builder.Append('(');
                builder.Append(Kind == GeneratedExpressionKind.Not ? '!' : '-');
                Children[0].AppendTo(builder);
                builder.Append(')');
                return;
            case GeneratedExpressionKind.Conditional:
                builder.Append('(');
                Children[0].AppendTo(builder);
                builder.Append(" ? ");
                Children[1].AppendTo(builder);
                builder.Append(" : ");
                Children[2].AppendTo(builder);
                builder.Append(')');
                return;
            case GeneratedExpressionKind.Length:
                builder.Append('(');
                Children[0].AppendTo(builder);
                builder.Append(").Length");
                return;
            case GeneratedExpressionKind.ArrayIndex:
                builder.Append('(');
                Children[0].AppendTo(builder);
                builder.Append(")[checked((int)");
                Children[1].AppendTo(builder);
                builder.Append(")]");
                return;
            case GeneratedExpressionKind.CastToString:
                builder.Append("((string)");
                Children[0].AppendTo(builder);
                builder.Append(')');
                return;
            default:
                builder.Append('(');
                Children[0].AppendTo(builder);
                builder.Append(' ');
                builder.Append(GetBinaryOperator(Kind).Token);
                builder.Append(' ');
                Children[1].AppendTo(builder);
                builder.Append(')');
                return;
        }
    }

    private static (
        string Token,
        GeneratedExpressionType? OperandType,
        GeneratedExpressionType ResultType) GetBinaryOperator(
            GeneratedExpressionKind kind)
    {
        // Equality accepts matching integer/string/sequence/reference operands.
        // A null OperandType denotes this policy rather than a fixed sort.
        return kind switch
        {
            GeneratedExpressionKind.Add =>
                ("+", GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.Subtract =>
                ("-", GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.Multiply =>
                ("*", GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.Divide =>
                ("/", GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.Remainder =>
                ("%", GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.AndAlso =>
                ("&&", GeneratedExpressionType.Boolean, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.OrElse =>
                ("||", GeneratedExpressionType.Boolean, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.Equal =>
                ("==", null, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.NotEqual =>
                ("!=", null, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.LessThan =>
                ("<", GeneratedExpressionType.Integer, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.LessThanOrEqual =>
                ("<=", GeneratedExpressionType.Integer, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.GreaterThan =>
                (">", GeneratedExpressionType.Integer, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.GreaterThanOrEqual =>
                (">=", GeneratedExpressionType.Integer, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.StringConcat =>
                ("+", GeneratedExpressionType.String, GeneratedExpressionType.String),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

internal sealed record GeneratedCSharpCase(
    GeneratedCSharpExpression Expression,
    long Left,
    long Right,
    bool Condition)
{
    public string? Text
    {
        get; init;
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The value is passed to compiled code as a long[] argument.")]
    public long[]? Values
    {
        get; init;
    }
    public object? Reference
    {
        get; init;
    }

    public string Source => FrontendDifferentialOracle.CreateBatchSource(
        [this], indexedMethods: false);

    internal static string ReturnType(GeneratedExpressionType type)
    {
        return type switch
        {
            GeneratedExpressionType.Boolean => "bool",
            GeneratedExpressionType.Integer => "long",
            GeneratedExpressionType.String => "string?",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
    public GeneratedCSharpExpression Expression { get; init; } = Expression ?? throw new System.ArgumentNullException(nameof(Expression));
}

internal sealed class SmallCSharpCaseGenerator(int seed)
{
    private static readonly ImmutableArray<long> LiteralIntegers = [-3, -1, 0, 1, 2, 3];
    private readonly Random _random = new(seed);

    private static readonly ImmutableArray<long> IntIndexes = [.. DifferentialIntegerCorpus.InterestingIntegers
        .Where(static value => value is >= int.MinValue and <= int.MaxValue)];

    public GeneratedCSharpCase Next(int maximumDepth = 4)
    {
        if (maximumDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        var values = _random.Next(4) == 0
            ? null
            : Enumerable.Range(0, _random.Next(4))
                .Select(_ => DifferentialIntegerCorpus.InterestingIntegers[
                    _random.Next(DifferentialIntegerCorpus.InterestingIntegers.Length)])
                .ToArray();
        // Element reads convert the index with checked((int)...); an index
        // inside the int range keeps the bounds check observable.
        var index = IntIndexes[_random.Next(IntIndexes.Length)];
        if (_random.Next(3) == 0 && values != null)
        {
            index = _random.Next(2) == 0 ? -1 : values.Length;
        }

        var expression = _random.Next(10) switch
        {
            0 => String(maximumDepth),
            1 => GeneratedCSharpExpression.Length(String(maximumDepth)),
            2 => GeneratedCSharpExpression.Binary(
                _random.Next(2) == 0
                    ? GeneratedExpressionKind.Equal
                    : GeneratedExpressionKind.NotEqual,
                String(maximumDepth),
                _random.Next(2) == 0
                    ? GeneratedCSharpExpression.NullString()
                    : GeneratedCSharpExpression.Text()),
            3 => GeneratedCSharpExpression.CastToString(
                String(maximumDepth)),
            4 => GeneratedCSharpExpression.Length(
                GeneratedCSharpExpression.Values()),
            5 => GeneratedCSharpExpression.ArrayIndex(
                GeneratedCSharpExpression.Values(),
                GeneratedCSharpExpression.Left()),
            _ => _random.Next(2) == 0
                ? Integer(maximumDepth)
                : Boolean(maximumDepth)
        };
        return new GeneratedCSharpCase(
            expression,
            expression.Kind == GeneratedExpressionKind.ArrayIndex
                ? index
                : DifferentialIntegerCorpus.InterestingIntegers[
                    _random.Next(DifferentialIntegerCorpus.InterestingIntegers.Length)],
            DifferentialIntegerCorpus.InterestingIntegers[
                _random.Next(DifferentialIntegerCorpus.InterestingIntegers.Length)],
            _random.Next(2) == 0)
        {
            Text = _random.Next(4) switch
            {
                0 => null,
                1 => "",
                2 => "sharp",
                _ => "proof"
            },
            Values = values,
            Reference = _random.Next(3) switch
            {
                0 => null,
                1 => "sharp",
                _ => new object()
            }
        };
    }

    private GeneratedCSharpExpression String(int depth)
    {
        if (depth == 0)
        {
            return StringLeaf();
        }

        return _random.Next(4) switch
        {
            0 => GeneratedCSharpExpression.Conditional(
                Boolean(depth - 1),
                String(depth - 1),
                String(depth - 1)),
            1 => GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.StringConcat,
                GeneratedCSharpExpression.Text(),
                GeneratedCSharpExpression.String("proof")),
            _ => StringLeaf()
        };
    }

    private GeneratedCSharpExpression StringLeaf()
    {
        return _random.Next(5) switch
        {
            0 => GeneratedCSharpExpression.Text(),
            1 => GeneratedCSharpExpression.NullString(),
            2 => GeneratedCSharpExpression.String(""),
            3 => GeneratedCSharpExpression.String("sharp"),
            _ => GeneratedCSharpExpression.String("proof")
        };
    }

    private GeneratedCSharpExpression Integer(int depth)
    {
        if (depth == 0)
        {
            return IntegerLeaf();
        }

        return _random.Next(6) switch
        {
            0 => GeneratedCSharpExpression.Unary(
                GeneratedExpressionKind.Negate,
                Integer(depth - 1)),
            1 => GeneratedCSharpExpression.Conditional(
                Boolean(depth - 1),
                Integer(depth - 1),
                Integer(depth - 1)),
            _ => IntegerBinary(depth - 1)
        };
    }

    private GeneratedCSharpExpression IntegerBinary(int depth)
    {
        var kind = _random.Next(5) switch
        {
            0 => GeneratedExpressionKind.Add,
            1 => GeneratedExpressionKind.Subtract,
            2 => GeneratedExpressionKind.Multiply,
            3 => GeneratedExpressionKind.Divide,
            _ => GeneratedExpressionKind.Remainder
        };
        var left = Integer(depth);
        var right = Integer(depth);
        if (left.TryEvaluateIntegerConstant(out _) &&
            right.TryEvaluateIntegerConstant(out _))
        {
            left = GeneratedCSharpExpression.Left();
        }

        if (kind is GeneratedExpressionKind.Divide or GeneratedExpressionKind.Remainder &&
            right.TryEvaluateIntegerConstant(out var divisor) &&
            divisor == 0)
        {
            right = GeneratedCSharpExpression.Right();
        }

        return GeneratedCSharpExpression.Binary(kind, left, right);
    }

    private GeneratedCSharpExpression IntegerLeaf()
    {
        return _random.Next(4) switch
        {
            0 => GeneratedCSharpExpression.Left(),
            1 => GeneratedCSharpExpression.Right(),
            _ => GeneratedCSharpExpression.Integer(
                LiteralIntegers[_random.Next(LiteralIntegers.Length)])
        };
    }

    private GeneratedCSharpExpression Boolean(int depth)
    {
        if (depth == 0)
        {
            return BooleanLeaf();
        }

        return _random.Next(6) switch
        {
            0 => GeneratedCSharpExpression.Unary(
                GeneratedExpressionKind.Not,
                Boolean(depth - 1)),
            1 => GeneratedCSharpExpression.Binary(
                _random.Next(2) == 0
                    ? GeneratedExpressionKind.AndAlso
                    : GeneratedExpressionKind.OrElse,
                Boolean(depth - 1),
                Boolean(depth - 1)),
            2 => GeneratedCSharpExpression.Conditional(
                Boolean(depth - 1),
                Boolean(depth - 1),
                Boolean(depth - 1)),
            _ => GeneratedCSharpExpression.Binary(
                RandomComparison(),
                Integer(depth - 1),
                Integer(depth - 1))
        };
    }

    private GeneratedCSharpExpression BooleanLeaf()
    {
        return _random.Next(3) switch
        {
            0 => GeneratedCSharpExpression.Condition(),
            1 => GeneratedCSharpExpression.Boolean(false),
            _ => GeneratedCSharpExpression.Boolean(true)
        };
    }

    private GeneratedExpressionKind RandomComparison()
    {
        return _random.Next(6) switch
        {
            0 => GeneratedExpressionKind.Equal,
            1 => GeneratedExpressionKind.NotEqual,
            2 => GeneratedExpressionKind.LessThan,
            3 => GeneratedExpressionKind.LessThanOrEqual,
            4 => GeneratedExpressionKind.GreaterThan,
            _ => GeneratedExpressionKind.GreaterThanOrEqual
        };
    }
}

internal sealed record FrontendDifferentialResult(
    FuzzOracleStatus Status,
    string Detail,
    IrExceptionKind? ExceptionKind = null)
{
    public string Detail { get; init; } = Detail ?? throw new System.ArgumentNullException(nameof(Detail));
}

internal sealed record FrontendSemanticEdgeCase(
    string ReturnType,
    string Parameters,
    string Expression,
    IReadOnlyList<object?> Arguments,
    FrontendSubsetDecision ExpectedDecision,
    FrontendAbstention ExpectedAbstention)
{
    public string ReturnType { get; init; } = ReturnType ?? throw new System.ArgumentNullException(nameof(ReturnType));
    public string Parameters { get; init; } = Parameters ?? throw new System.ArgumentNullException(nameof(Parameters));
    public string Expression { get; init; } = Expression ?? throw new System.ArgumentNullException(nameof(Expression));
    public IReadOnlyList<object?> Arguments { get; init; } = Arguments ?? throw new System.ArgumentNullException(nameof(Arguments));
}

internal sealed record FrontendSemanticEdgeResult(
    FuzzOracleStatus Status,
    FrontendSubsetDecision? ActualDecision,
    FrontendAbstention? ActualAbstention,
    string Detail,
    IrExceptionKind? ExceptionKind = null)
{
    public string Detail { get; init; } = Detail ?? throw new System.ArgumentNullException(nameof(Detail));
}

internal static class FrontendDifferentialOracle
{
    private const string SemanticEdgeMethodPrefix = "EdgeTarget";
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(
            static () => TestMetadataReferences.SortedDistinctPlatform,
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static FrontendDifferentialResult Compare(
        GeneratedCSharpCase generated,
        CancellationToken cancellationToken)
    {
        if (generated == null)
        {
            throw new ArgumentNullException(nameof(generated));
        }

        return CompareBatch([generated], cancellationToken)[0];
    }

    public static ImmutableArray<FrontendDifferentialResult> CompareBatch(
        ImmutableArray<GeneratedCSharpCase> generatedCases,
        CancellationToken cancellationToken)
    {
        if (!HasValidBatch(generatedCases))
        {
            return [];
        }
        cancellationToken.ThrowIfCancellationRequested();

        var source = CreateBatchSource(generatedCases);
        var (syntaxTree, compilation, image, emit) = CompileGenerated(
            source, "SharpProofFrontendFuzz", cancellationToken);
        using var imageScope = image;
        if (!emit.Success)
        {
            var failure = Mismatch(
                "Generated C# did not compile: " +
                DifferentialFormatting.FormatErrors(
                    emit.Diagnostics,
                    includeIdTieBreak: true));
            if (generatedCases.Length == 1)
            {
                return [failure];
            }

            var midpoint = generatedCases.Length / 2;
            var left = CompareBatch(
                generatedCases.Slice(0, midpoint),
                cancellationToken);
            var right = CompareBatch(
                generatedCases.Slice(midpoint, generatedCases.Length - midpoint),
                cancellationToken);
            return [.. left, .. right];
        }

        return CompareCompiledBatch(generatedCases, syntaxTree, compilation, image, cancellationToken);
    }

    internal static ImmutableArray<FrontendDifferentialResult> CompareSourceBatch(
        string source,
        ImmutableArray<GeneratedCSharpCase> generatedCases,
        CancellationToken cancellationToken)
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        if (!HasValidBatch(generatedCases))
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (syntaxTree, compilation, image, emit) = CompileGenerated(
            source, "SharpProofFrontendFuzz", cancellationToken);
        using var imageScope = image;
        if (!emit.Success)
        {
            var failure = Mismatch(
                "Generated C# did not compile: " +
                DifferentialFormatting.FormatErrors(
                    emit.Diagnostics,
                    includeIdTieBreak: true));
            return [.. Enumerable.Repeat(failure, generatedCases.Length)];
        }

        return CompareCompiledBatch(generatedCases, syntaxTree, compilation, image, cancellationToken);
    }

    private static bool HasValidBatch(ImmutableArray<GeneratedCSharpCase> generatedCases)
    {
        if (generatedCases.IsDefault)
        {
            throw new ArgumentNullException(nameof(generatedCases));
        }

        if (generatedCases.Length == 0)
        {
            return false;
        }

        if (generatedCases.Any(static generated => generated?.Expression == null))
        {
            throw new ArgumentException(
                "Generated cases cannot contain null.",
                nameof(generatedCases));
        }

        return true;
    }

    private static ImmutableArray<FrontendDifferentialResult> CompareCompiledBatch(
        ImmutableArray<GeneratedCSharpCase> generatedCases,
        SyntaxTree syntaxTree,
        CSharpCompilation compilation,
        MemoryStream image,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        image = RepositoryParameterGuard.NotNull(image, nameof(image));
        syntaxTree = RepositoryParameterGuard.NotNull(syntaxTree, nameof(syntaxTree));
        cancellationToken.ThrowIfCancellationRequested();
        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            compilation,
            syntaxTree);
        var methodSyntaxes = syntaxTree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .OrderBy(
                static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (ParseMethodIndex(
                    method.Identifier.ValueText)))
            .ToImmutableArray();
        if (methodSyntaxes.Length != generatedCases.Length)
        {
            var failure = Mismatch(
                "Roslyn exposed an unexpected generated method count.");
            return [.. Enumerable.Repeat(failure, generatedCases.Length)];
        }

        return WithLoadedGeneratedAssembly(
            image,
            "SharpProofFrontendFuzz",
            "SharpProofGeneratedFrontend",
            runtimeType =>
            {
                var results = ImmutableArray.CreateBuilder<FrontendDifferentialResult>(
                    generatedCases.Length);
                for (var index = 0; index < generatedCases.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var methodSyntax = methodSyntaxes[index];
                    var methodSymbol = (IMethodSymbol?)model.GetDeclaredSymbol(
                        methodSyntax,
                        cancellationToken);
                    var operation = GetExpressionOperation(
                        model,
                        methodSyntax.ExpressionBody?.Expression,
                        cancellationToken);
                    if (methodSymbol == null || operation == null)
                    {
                        results.Add(
                            Mismatch(
                                "Roslyn did not expose the generated method operation."));
                        continue;
                    }

                    var body = FrontendTotalBody.Lower(methodSymbol, methodSyntax, model, cancellationToken);
                    var factory = body.Factory;
                    if (!body.IsExact)
                    {
                        results.Add(
                            Mismatch(
                                "Generated supported C# closed the Total body subset: " +
                                body.Classification.Abstention +
                                "."));
                        continue;
                    }

                    var generated = generatedCases[index];
                    var environment = CreateEnvironment(
                        factory,
                        methodSymbol,
                        body.Parameters,
                        generated,
                        body.UnreadParameters);
                    var runtimeMethod = runtimeType.GetMethod(
                        MethodName(index),
                        BindingFlags.Public | BindingFlags.Static) ??
                        throw new InvalidOperationException(
                            "The generated assembly does not define " + MethodName(index) + ".");
                    var actual = InvokeMethod(
                        runtimeMethod,
                        generated,
                        cancellationToken);
                    results.Add(CompareExecution(
                        body.Execute(environment.Variables, cancellationToken),
                        actual,
                        environment.SequenceOrigins));
                }
                return results.ToImmutable();
            });
    }

    public static ImmutableArray<FrontendSemanticEdgeResult> CompareSemanticEdges(
        ImmutableArray<FrontendSemanticEdgeCase> cases,
        CancellationToken cancellationToken)
    {
        if (cases.IsDefault)
        {
            throw new ArgumentNullException(nameof(cases));
        }

        if (cases.Length == 0)
        {
            return [];
        }

        for (var index = 0; index < cases.Length; index++)
        {
            var generated = cases[index] ??
                throw new ArgumentException(
                    "Semantic edge cases cannot contain null.",
                    nameof(cases));
            _ = new FrontendSubsetClassification(
                generated.ExpectedDecision,
                generated.ExpectedAbstention);
            if (string.IsNullOrWhiteSpace(generated.ReturnType) ||
                generated.Parameters == null ||
                string.IsNullOrWhiteSpace(generated.Expression) ||
                generated.Arguments == null)
            {
                throw new ArgumentException(
                    $"Semantic edge case {index} is incomplete.",
                    nameof(cases));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        var source = CreateSemanticEdgeSource(cases);
        var (syntaxTree, compilation, image, emit) = CompileGenerated(
            source, "SharpProofFrontendSemanticEdges", cancellationToken);
        using var imageScope = image;
        if (!emit.Success)
        {
            return IsolateSemanticEdgeFailure(
                cases,
                "Generated semantic-edge C# did not compile: " +
                DifferentialFormatting.FormatErrors(
                    emit.Diagnostics,
                    includeIdTieBreak: true),
                cancellationToken);
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            compilation,
            syntaxTree);
        var compilationUnit = (CompilationUnitSyntax)syntaxTree.GetRoot(
            cancellationToken);
        var generatedTypes = compilationUnit
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.Identifier.ValueText ==
                "SharpProofGeneratedFrontendEdges"))
            .ToImmutableArray();
        if (generatedTypes.Length != 1)
        {
            return IsolateSemanticEdgeFailure(
                cases,
                "Roslyn exposed an unexpected semantic-edge type shape.",
                cancellationToken);
        }
        var generatedType = generatedTypes[0];
        var hasExpectedTopology =
            compilationUnit.AttributeLists.Count == 0 &&
            compilationUnit.Members.Count == 3 &&
            compilationUnit.Members[0] is EnumDeclarationSyntax
            { Identifier.ValueText: "SharpProofGeneratedEdgeEnum" } &&
            compilationUnit.Members[1] is StructDeclarationSyntax
            { Identifier.ValueText: "SharpProofGeneratedConvertible" } &&
            compilationUnit.Members[2] is ClassDeclarationSyntax
            { Identifier.ValueText: "SharpProofGeneratedFrontendEdges" };
        if (!hasExpectedTopology)
        {
            return IsolateSemanticEdgeFailure(
                cases,
                "Roslyn exposed an unexpected semantic-edge file shape.",
                cancellationToken);
        }
        var methods = generatedType.Members
            .OfType<MethodDeclarationSyntax>()
            .ToImmutableArray();
        if (methods.Length != cases.Length ||
            generatedType.Members.Count != cases.Length)
        {
            return IsolateSemanticEdgeFailure(
                cases,
                "Roslyn exposed an unexpected semantic-edge method shape.",
                cancellationToken);
        }
        var methodsByName = ImmutableDictionary.CreateBuilder<string, MethodDeclarationSyntax>(
            StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (!methodsByName.TryAdd(
                    method.Identifier.ValueText,
                    method))
            {
                return IsolateSemanticEdgeFailure(
                    cases,
                    "Roslyn exposed an unexpected semantic-edge method shape.",
                    cancellationToken);
            }
        }

        var namedMethods = methodsByName.ToImmutable();
        var orderedMethods = ImmutableArray.CreateBuilder<MethodDeclarationSyntax>(cases.Length);
        for (var index = 0; index < cases.Length; index++)
        {
            if (!namedMethods.TryGetValue(
                    SemanticEdgeMethodName(index),
                    out var method))
            {
                return IsolateSemanticEdgeFailure(
                    cases,
                    "Roslyn exposed an unexpected semantic-edge method shape.",
                    cancellationToken);
            }

            orderedMethods.Add(method);
        }
        methods = orderedMethods.ToImmutable();

        return WithLoadedGeneratedAssembly(
            image,
            "SharpProofFrontendSemanticEdges",
            "SharpProofGeneratedFrontendEdges",
            runtimeType =>
            {
                runtimeType = RepositoryParameterGuard.NotNull(runtimeType, nameof(runtimeType));
                var results =
                    ImmutableArray.CreateBuilder<FrontendSemanticEdgeResult>(
                        cases.Length);
                for (var index = 0; index < cases.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(CompareSemanticEdge(
                        cases[index],
                        methods[index],
                        model,
                        runtimeType,
                        index,
                        cancellationToken));
                }
                return results.ToImmutable();
            });
    }

    private static TResult WithLoadedGeneratedAssembly<TResult>(
        MemoryStream image,
        string contextName,
        string typeName,
        Func<Type, TResult> callback)
    {
        callback = RepositoryParameterGuard.NotNull(callback, nameof(callback));
        contextName = RepositoryParameterGuard.NotNull(contextName, nameof(contextName));
        image = RepositoryParameterGuard.NotNull(image, nameof(image));
        image.Position = 0;
        var loadContext = new AssemblyLoadContext(
            contextName,
            isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(image);
            var runtimeType = assembly.GetType(typeName) ??
                throw new InvalidOperationException(
                    "The generated assembly does not define " + typeName + ".");
            return callback(runtimeType);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static ImmutableArray<FrontendSemanticEdgeResult>
        IsolateSemanticEdgeFailure(
            ImmutableArray<FrontendSemanticEdgeCase> cases,
            string detail,
            CancellationToken cancellationToken)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        if (cases.Length == 1)
        {
            return RepeatSemanticFailure(1, detail);
        }

        var midpoint = cases.Length / 2;
        var left = CompareSemanticEdges(
            cases.Slice(0, midpoint),
            cancellationToken);
        var right = CompareSemanticEdges(
            cases.Slice(midpoint, cases.Length - midpoint),
            cancellationToken);
        return [.. left, .. right];
    }

    internal static (
        ImmutableDictionary<IrVarId, IrValue> Variables,
        ImmutableDictionary<IrValue, Array> SequenceOrigins) CreateEnvironment(
        IrFactory factory,
        IMethodSymbol method,
        ImmutableArray<FrontendVariableBinding> bindings,
        GeneratedCSharpCase generated,
        ImmutableHashSet<IrVarId>? unread = null /* SPINT001: all values valid - null means the body reads every binding */)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        generated = RepositoryParameterGuard.NotNull(generated, nameof(generated));
        RepositoryParameterGuard.ThrowIfMissing(bindings.IsDefault, nameof(bindings));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        var sequenceOrigins = ImmutableDictionary.CreateBuilder<IrValue, Array>(
            ReferenceEqualityComparer.Instance);
        foreach (var (variable, ordinal) in ParameterBindings(method, bindings))
        {
            if (unread?.Contains(variable) == true)
            {
                environment.AddUnique(variable, NeutralValue(factory, variable));
                continue;
            }
            var value = ordinal switch
            {
                0 => factory.CreateIntegerValue(factory.GetVariableInfo(variable).Type, generated.Left),
                1 => factory.CreateIntegerValue(factory.GetVariableInfo(variable).Type, generated.Right),
                2 => factory.CreateBooleanValue(generated.Condition),
                3 => generated.Text == null
                    ? factory.CreateNullValue(factory.StringType)
                    : factory.CreateStringValue(generated.Text),
                4 => generated.Values == null
                    ? factory.CreateNullValue(
                        factory.GetVariableInfo(variable).Type)
                    : CreateSequenceValue(
                        factory.GetVariableInfo(variable).Type,
                        generated.Values),
                5 => generated.Reference == null
                    ? factory.CreateNullValue(factory.ObjectType)
                    : factory.CreateReferenceValue(
                        factory.ObjectType,
                        generated.Reference),
                _ => throw new InvalidOperationException(
                    "The generated method has an unexpected parameter.")
            };
            environment.AddUnique(variable, value);
        }
        return (environment.ToImmutable(), sequenceOrigins.ToImmutable());

        static IrValue NeutralValue(IrFactory factory, IrVarId variable)
        {
            factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
            RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
            var type = factory.GetVariableInfo(variable).Type;
            return factory.GetTypeInfo(type).Kind switch
            {
                IrTypeKind.Boolean => factory.CreateBooleanValue(false),
                IrTypeKind.Integer => factory.CreateIntegerValue(type, 0L),
                _ => factory.CreateNullValue(type)
            };
        }

        IrValue CreateSequenceValue(IrTypeId type, long[] values)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
            var elementType = factory.GetTypeInfo(type).ElementType ??
                throw new InvalidOperationException("A generated sequence has no element type.");
            var value = factory.CreateSequenceValue(
                type,
                values.Select(element => factory.CreateIntegerValue(elementType, element)));
            sequenceOrigins.AddUnique(value, values);
            return value;
        }
    }

    internal static FrontendSemanticEdgeResult CompareSemanticEdge(
        FrontendSemanticEdgeCase generated,
        MethodDeclarationSyntax methodSyntax,
        SemanticModel model,
        Type runtimeType,
        int index,
        CancellationToken cancellationToken)
    {
        methodSyntax = RepositoryParameterGuard.NotNull(methodSyntax, nameof(methodSyntax));
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        var method = (IMethodSymbol?)model.GetDeclaredSymbol(
            methodSyntax,
            cancellationToken);
        var operation = GetExpressionOperation(
            model,
            methodSyntax.ExpressionBody?.Expression,
            cancellationToken);
        if (method == null || operation == null)
        {
            return SemanticFailure(
                null,
                null,
                "Roslyn did not expose the semantic-edge method operation.");
        }

        if (generated.Arguments.Count != method.Parameters.Length)
        {
            return SemanticFailure(
                null,
                null,
                "The semantic-edge runtime argument count is incorrect.");
        }

        var body = FrontendTotalBody.Lower(method, methodSyntax, model, cancellationToken);
        var factory = body.Factory;
        var actual = body.Classification;
        if (actual.Decision != generated.ExpectedDecision ||
            actual.Abstention != generated.ExpectedAbstention)
        {
            return SemanticFailure(
                actual.Decision,
                actual.Abstention,
                "Expected " +
                generated.ExpectedDecision +
                "/" +
                generated.ExpectedAbstention +
                " but lowering returned " +
                actual.Decision +
                "/" +
                actual.Abstention +
                ".");
        }

        if (!actual.IsExact)
        {

            return SemanticAgreement(actual);
        }

        var arguments = ImmutableArray.CreateBuilder<object?>(method.Parameters.Length);
        for (var argumentIndex = 0; argumentIndex < method.Parameters.Length; argumentIndex++)
        {
            arguments.Add(generated.Arguments[argumentIndex]);
        }
        var environment = CreateSemanticEdgeEnvironment(
            factory,
            method,
            body.Parameters,
            arguments.MoveToImmutable());
        var runtimeMethod = runtimeType.GetMethod(
            SemanticEdgeMethodName(index),
            BindingFlags.Public | BindingFlags.Static) ??
            throw new InvalidOperationException(
                "The generated semantic-edge assembly does not define " + SemanticEdgeMethodName(index) + ".");
        var comparison = CompareExecution(
            body.Execute(environment.Variables, cancellationToken),
            InvokeMethod(
                runtimeMethod,
                generated.Arguments,
                cancellationToken),
            environment.SequenceOrigins);
        return new FrontendSemanticEdgeResult(
            comparison.Status,
            actual.Decision,
            actual.Abstention,
            comparison.Detail,
            comparison.ExceptionKind);
    }

    private static (
        ImmutableDictionary<IrVarId, IrValue> Variables,
        ImmutableDictionary<IrValue, Array> SequenceOrigins)
        CreateSemanticEdgeEnvironment(
            IrFactory factory,
            IMethodSymbol method,
            ImmutableArray<FrontendVariableBinding> bindings,
            ImmutableArray<object?> arguments)
    {
        RepositoryParameterGuard.ThrowIfMissing(arguments.IsDefault, nameof(arguments));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfMissing(bindings.IsDefault, nameof(bindings));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        var sequenceValues = ImmutableDictionary.CreateBuilder<object, ImmutableDictionary<IrTypeId, IrValue>.Builder>(
            ReferenceEqualityComparer.Instance);
        var sequenceOrigins = ImmutableDictionary.CreateBuilder<IrValue, Array>(
            ReferenceEqualityComparer.Instance);
        foreach (var (variable, ordinal) in ParameterBindings(method, bindings))
        {
            var type = factory.GetVariableInfo(variable).Type;
            environment.AddUnique(
                variable,
                CreateSemanticEdgeValue(
                    factory,
                    type,
                    arguments[ordinal],
                    sequenceValues,
                    sequenceOrigins));
        }
        return (environment.ToImmutable(), sequenceOrigins.ToImmutable());
    }

    private static IEnumerable<(IrVarId Variable, int Ordinal)> ParameterBindings(
        IMethodSymbol method,
        ImmutableArray<FrontendVariableBinding> bindings)
    {
        RepositoryParameterGuard.ThrowIfMissing(bindings.IsDefault, nameof(bindings));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        foreach (var binding in bindings)
        {
            if (binding.Symbol is IParameterSymbol parameter &&
                SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    method))
            {
                yield return (binding.Variable, parameter.Ordinal);
            }
        }
    }

    private static IrValue CreateSemanticEdgeValue(
        IrFactory factory,
        IrTypeId type,
        object? value,
        ImmutableDictionary<object, ImmutableDictionary<IrTypeId, IrValue>.Builder>.Builder sequenceValues,
        ImmutableDictionary<IrValue, Array>.Builder sequenceOrigins)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        sequenceOrigins = RepositoryParameterGuard.NotNull(sequenceOrigins, nameof(sequenceOrigins));
        sequenceValues = RepositoryParameterGuard.NotNull(sequenceValues, nameof(sequenceValues));
        var kind = factory.GetTypeInfo(type).Kind;
        if (value == null)
        {
            if (kind is IrTypeKind.String or
                IrTypeKind.Reference or
                IrTypeKind.Sequence)
            {
                return factory.CreateNullValue(type);
            }

            throw new InvalidOperationException(
                "A non-nullable semantic-edge variable received null.");
        }
        return kind switch
        {
            IrTypeKind.Boolean when value is bool boolean =>
                factory.CreateBooleanValue(boolean),
            IrTypeKind.Integer when value is ulong unsigned =>
                factory.CreateIntegerValue(type, unsigned),
            IrTypeKind.Integer when value is sbyte or byte or short or ushort or
                int or uint or long or char =>
                factory.CreateIntegerValue(type, Convert.ToInt64(
                    value,
                    CultureInfo.InvariantCulture)),
            IrTypeKind.String when value is string text =>
                factory.CreateStringValue(text),
            IrTypeKind.Reference =>
                factory.CreateReferenceValue(type, value),
            IrTypeKind.Sequence when value is Array array =>
                CreateSequenceValue(array),
            _ => throw new InvalidOperationException(
                "A semantic-edge value is outside the executable IR subset.")
        };

        IrValue CreateSequenceValue(Array array)
        {
            if (sequenceValues.TryGetValue(array, out var typedValues) &&
                typedValues.TryGetValue(type, out var existing))
            {
                return existing;
            }

            var elementType = factory.GetTypeInfo(type).ElementType ??
                throw new InvalidOperationException(
                    "A semantic-edge sequence has no element type.");
            var created = factory.CreateSequenceValue(
                type,
                array.Cast<object?>().Select(element =>
                    CreateSemanticEdgeValue(
                        factory,
                        elementType,
                        element,
                        sequenceValues,
                        sequenceOrigins)));
            if (!sequenceValues.TryGetValue(array, out typedValues))
            {
                typedValues = ImmutableDictionary.CreateBuilder<IrTypeId, IrValue>();
                sequenceValues.AddUnique(array, typedValues);
            }
            typedValues.AddUnique(type, created);
            sequenceOrigins.AddUnique(created, array);
            return created;
        }
    }

    internal static IOperation? GetExpressionOperation(
        SemanticModel model,
        ExpressionSyntax? expression,
        CancellationToken cancellationToken)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        if (expression == null)
        {
            return null;
        }

        var operation = model.GetOperation(expression, cancellationToken);
        if (operation != null)
        {
            return operation;
        }

        return expression switch
        {
            CheckedExpressionSyntax checkedExpression =>
                GetExpressionOperation(
                    model,
                    checkedExpression.Expression,
                    cancellationToken),
            ParenthesizedExpressionSyntax parenthesized =>
                GetExpressionOperation(
                    model,
                    parenthesized.Expression,
                    cancellationToken),
            _ => null
        };
    }

    private static RuntimeOutcome InvokeMethod(
        MethodInfo method,
        GeneratedCSharpCase generated,
        CancellationToken cancellationToken)
    {
        generated = RepositoryParameterGuard.NotNull(generated, nameof(generated));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return InvokeMethod(
            method,
            [
                generated.Left,
                generated.Right,
                generated.Condition,
                generated.Text,
                generated.Values,
                generated.Reference
            ],
            cancellationToken);
    }

    private static RuntimeOutcome InvokeMethod(
        MethodInfo method,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken)
    {
        arguments = RepositoryParameterGuard.NotNull(arguments, nameof(arguments));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return RuntimeOutcome.Returned(
                method.Invoke(null, [.. arguments]));
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException != null)
        {
            return RuntimeOutcome.Threw(exception.InnerException);
        }
    }

    internal static string CreateBatchSource(
        ImmutableArray<GeneratedCSharpCase> generatedCases,
        bool indexedMethods = true)
    {
        RepositoryParameterGuard.ThrowIfMissing(generatedCases.IsDefault, nameof(generatedCases));
        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        builder.AppendLine("public static class SharpProofGeneratedFrontend {");
        for (var index = 0; index < generatedCases.Length; index++)
        {
            var generated = generatedCases[index];
            builder.Append("    public static ");
            builder.Append(GeneratedCSharpCase.ReturnType(
                generated.Expression.Type));
            builder.Append(' ');
            builder.Append(indexedMethods ? MethodName(index) : "Target");
            builder.Append(
                "(long left, long right, bool condition, string? text, long[]? values, object? reference) => ");
            builder.Append(generated.Expression.Render());
            builder.AppendLine(";");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string CreateSemanticEdgeSource(
        ImmutableArray<FrontendSemanticEdgeCase> cases)
    {
        RepositoryParameterGuard.ThrowIfMissing(cases.IsDefault, nameof(cases));
        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        builder.AppendLine(
            "public enum SharpProofGeneratedEdgeEnum { One = 1 }");
        builder.AppendLine(
            "public readonly struct SharpProofGeneratedConvertible {");
        builder.AppendLine("    private readonly long _value;");
        builder.AppendLine(
            "    public SharpProofGeneratedConvertible(long value) => _value = value;");
        builder.AppendLine(
            "    public static explicit operator long(SharpProofGeneratedConvertible value) => value._value;");
        builder.AppendLine("}");
        builder.AppendLine(
            "public static class SharpProofGeneratedFrontendEdges {");
        for (var index = 0; index < cases.Length; index++)
        {
            var generated = cases[index];
            builder.Append("    public static ");
            builder.Append(generated.ReturnType);
            builder.Append(' ');
            builder.Append(SemanticEdgeMethodName(index));
            builder.Append('(');
            builder.Append(generated.Parameters);
            builder.Append(") => ");
            builder.Append(generated.Expression);
            builder.AppendLine(";");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string MethodName(int index)
    {
        return FormatGeneratedMethodName("Target", index);
    }

    private static string SemanticEdgeMethodName(int index)
    {
        return FormatGeneratedMethodName(SemanticEdgeMethodPrefix, index);
    }

    private static string FormatGeneratedMethodName(string prefix, int index)
    {
        prefix = RepositoryParameterGuard.NotNull(prefix, nameof(prefix));
        return prefix + index.ToString(CultureInfo.InvariantCulture);
    }

    private static int ParseMethodIndex(string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        return int.Parse(
            name.AsSpan("Target".Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }

    private static (
        SyntaxTree SyntaxTree,
        CSharpCompilation Compilation,
        MemoryStream Image,
        EmitResult Emit) CompileGenerated(
            string source,
            string assemblyName,
            CancellationToken cancellationToken)
    {
        assemblyName = RepositoryParameterGuard.NotNull(assemblyName, nameof(assemblyName));
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var (image, emit) = GeneratedCompilationEmitter.Emit(compilation, cancellationToken);
        return (syntaxTree, compilation, image, emit);
    }

    // The program interpreter raises the same exceptions as compiled C#, so
    // outcomes and exception kinds must agree.
    private static FrontendDifferentialResult CompareExecution(
        IrProgramExecutionResult execution,
        RuntimeOutcome actual,
        ImmutableDictionary<IrValue, Array> sequenceOrigins)
    {
        execution = RepositoryParameterGuard.NotNull(execution, nameof(execution));
        actual = RepositoryParameterGuard.NotNull(actual, nameof(actual));
        sequenceOrigins = RepositoryParameterGuard.NotNull(sequenceOrigins, nameof(sequenceOrigins));
        if (actual.Exception != null)
        {
            var kind = IrExceptionKindFacts.FromException(actual.Exception);
            return execution.Status == IrProgramExecutionStatus.Exception &&
                kind != null &&
                execution.Exception!.Kind == kind
                ? Agreement(kind)
                : Mismatch(
                    "Compiled C# threw " +
                    actual.Exception.GetType().Name +
                    " while the lowered program reported " +
                    Describe(execution) +
                    ".");
        }

        if (execution.Status != IrProgramExecutionStatus.Returned || execution.ReturnValue == null)
        {
            return Mismatch(
                "Compiled C# returned normally while the lowered program reported " +
                Describe(execution) +
                ".");
        }

        return SemanticValueEquals(actual.Value, execution.ReturnValue, sequenceOrigins)
            ? Agreement()
            : Mismatch("Compiled C# and the lowered program produced different values.");
    }

    private static string Describe(IrProgramExecutionResult execution)
    {
        execution = RepositoryParameterGuard.NotNull(execution, nameof(execution));
        return execution.Status == IrProgramExecutionStatus.Exception
            ? "exception " + execution.Exception?.Kind
            : execution.Status.ToString();
    }

    private static bool SemanticValueEquals(
        object? actual,
        IrValue interpreted,
        ImmutableDictionary<IrValue, Array> sequenceOrigins)
    {
        interpreted = RepositoryParameterGuard.NotNull(interpreted, nameof(interpreted));
        sequenceOrigins = RepositoryParameterGuard.NotNull(sequenceOrigins, nameof(sequenceOrigins));
        return interpreted.Kind switch
        {
            IrValueKind.Boolean =>
                actual is bool value &&
                value == interpreted.Boolean,
            IrValueKind.Integer =>
                IntegralValueEquals(
                    actual,
                    interpreted.Integer),
            IrValueKind.String =>
                actual is string value &&
                string.Equals(
                    value,
                    interpreted.String,
                    StringComparison.Ordinal),
            IrValueKind.Reference =>
                ReferenceEquals(actual, interpreted.Reference),
            IrValueKind.Sequence =>
                actual is Array array &&
                sequenceOrigins.TryGetValue(interpreted, out var origin) &&
                ReferenceEquals(array, origin),
            _ => actual == null
        };
    }

    internal static bool IntegralValueEquals(object? value, long expected)
    {
        return value switch
        {
            sbyte item => item == expected,
            byte item => item == expected,
            short item => item == expected,
            ushort item => item == expected,
            int item => item == expected,
            uint item => item == expected,
            long item => item == expected,
            ulong item => item <= long.MaxValue && (long)item == expected,
            char item => item == expected,
            _ => false
        };
    }

    private static FrontendDifferentialResult Agreement(
        IrExceptionKind? exceptionKind = null)
    {
        return new(FuzzOracleStatus.Agreement, "", exceptionKind);
    }

    private static FrontendDifferentialResult Mismatch(string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return new(FuzzOracleStatus.Mismatch, detail);
    }

    private static ImmutableArray<FrontendSemanticEdgeResult>
        RepeatSemanticFailure(
            int count,
            string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return [.. Enumerable.Repeat(
            SemanticFailure(null, null, detail),
            count)];
    }

    private static FrontendSemanticEdgeResult SemanticAgreement(
        FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */)
    {
        return new(
            FuzzOracleStatus.Agreement,
            classification.Decision,
            classification.Abstention,
            "");
    }

    private static FrontendSemanticEdgeResult SemanticFailure(
        FrontendSubsetDecision? decision,
        FrontendAbstention? abstention,
        string detail)
    {
        detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
        return new(
            FuzzOracleStatus.Mismatch,
            decision,
            abstention,
            detail);
    }

    private sealed record RuntimeOutcome(object? Value, Exception? Exception)
    {
        internal static RuntimeOutcome Returned(object? value)
        {
            return new(value, null);
        }

        internal static RuntimeOutcome Threw(Exception exception)
        {
            exception = RepositoryParameterGuard.NotNull(exception, nameof(exception));
            return new(null, exception);
        }
    }
}

internal static class CSharpStructuralShrinker
{
    public static GeneratedCSharpCase Minimize(
        GeneratedCSharpCase generated,
        Func<GeneratedCSharpCase, bool> preservesMismatch,
        CancellationToken cancellationToken)
    {
        if (generated == null)
        {
            throw new ArgumentNullException(nameof(generated));
        }

        if (preservesMismatch == null)
        {
            throw new ArgumentNullException(nameof(preservesMismatch));
        }

        var current = generated;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            foreach (var candidateExpression in GetCandidates(current.Expression))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidateExpression.NodeCount >= current.Expression.NodeCount)
                {
                    continue;
                }

                var candidate = current with
                {
                    Expression = candidateExpression
                };
                if (!preservesMismatch(candidate))
                {
                    continue;
                }

                current = candidate;
                changed = true;
                break;
            }
            if (!changed)
            {
                return current;
            }
        }
    }

    public static ImmutableArray<GeneratedCSharpExpression> GetCandidates(
        GeneratedCSharpExpression expression)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return StructuralShrinkCandidates.GetCandidates(
            expression,
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Children),
            static (parent, child) => child is null ? throw new System.ArgumentNullException(nameof(child)) : parent is null ? throw new System.ArgumentNullException(nameof(parent)) : (parent.Type == child.Type),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.NodeCount),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Render()),
            GetDomainCandidates,
            TryReplaceChild,
            StringComparer.Ordinal);
    }

    private static IEnumerable<GeneratedCSharpExpression> GetDomainCandidates(
        GeneratedCSharpExpression expression)
    {
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        return expression.Type switch
        {
            GeneratedExpressionType.Integer => [
                GeneratedCSharpExpression.Integer(0),
                GeneratedCSharpExpression.Integer(1),
                GeneratedCSharpExpression.Left(),
                GeneratedCSharpExpression.Right()],
            GeneratedExpressionType.Boolean => [
                GeneratedCSharpExpression.Boolean(false),
                GeneratedCSharpExpression.Boolean(true),
                GeneratedCSharpExpression.Condition()],
            GeneratedExpressionType.String => [
                GeneratedCSharpExpression.NullString(),
                GeneratedCSharpExpression.String(""),
                GeneratedCSharpExpression.Text()],
            GeneratedExpressionType.Sequence => [
                GeneratedCSharpExpression.Values()],
            _ => [
                GeneratedCSharpExpression.Reference(),
                GeneratedCSharpExpression.NullReference()]
        };
    }

    private static GeneratedCSharpExpression? TryReplaceChild(
        GeneratedCSharpExpression expression,
        int childIndex,
        GeneratedCSharpExpression child)
    {
        child = RepositoryParameterGuard.NotNull(child, nameof(child));
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        return expression.Kind switch
        {
            GeneratedExpressionKind.Not or
            GeneratedExpressionKind.Negate =>
                GeneratedCSharpExpression.Unary(expression.Kind, child),
            GeneratedExpressionKind.Conditional =>
                GeneratedCSharpExpression.Conditional(
                    childIndex == 0 ? child : expression.Children[0],
                    childIndex == 1 ? child : expression.Children[1],
                    childIndex == 2 ? child : expression.Children[2]),
            GeneratedExpressionKind.Length =>
                GeneratedCSharpExpression.Length(child),
            GeneratedExpressionKind.ArrayIndex =>
                GeneratedCSharpExpression.ArrayIndex(
                    childIndex == 0 ? child : expression.Children[0],
                    childIndex == 1 ? child : expression.Children[1]),
            GeneratedExpressionKind.CastToString =>
                GeneratedCSharpExpression.CastToString(child),
            // The callback visits only existing children. After the six
            // non-binary families, every compound producer is a validated binary.
            _ =>
                GeneratedCSharpExpression.Binary(
                    expression.Kind,
                    childIndex == 0 ? child : expression.Children[0],
                    childIndex == 1 ? child : expression.Children[1])
        };
    }
}

internal static class GeneratedCompilationEmitter
{
    internal static (MemoryStream Image, EmitResult Emit) Emit(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var image = new MemoryStream();
        try
        {
            var emit = compilation.Emit(
                image, cancellationToken: cancellationToken);
            return (image, emit);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
