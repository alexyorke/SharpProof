using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Fuzz;

public enum GeneratedExpressionType {
    Boolean,
    Integer,
    String,
    Sequence,
    Reference
}

public enum GeneratedExpressionKind {
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

public sealed class GeneratedCSharpExpression {
    private GeneratedCSharpExpression(
        GeneratedExpressionKind kind,
        GeneratedExpressionType type,
        long integerValue,
        bool booleanValue,
        ImmutableArray<GeneratedCSharpExpression> children,
        string? stringValue = null) {
        Kind = kind;
        Type = type;
        IntegerValue = integerValue;
        BooleanValue = booleanValue;
        StringValue = stringValue;
        Children = children;
        NodeCount = 1 + children.Sum(static child => child.NodeCount);
    }

    public GeneratedExpressionKind Kind { get; }
    public GeneratedExpressionType Type { get; }
    public long IntegerValue { get; }
    public bool BooleanValue { get; }
    public string? StringValue { get; }
    public ImmutableArray<GeneratedCSharpExpression> Children { get; }
    public int NodeCount { get; }

    public static GeneratedCSharpExpression Boolean(bool value) =>
        new(
            GeneratedExpressionKind.BooleanLiteral,
            GeneratedExpressionType.Boolean,
            0,
            value,
            []);

    public static GeneratedCSharpExpression Integer(long value) =>
        new(
            GeneratedExpressionKind.IntegerLiteral,
            GeneratedExpressionType.Integer,
            value,
            false,
            []);

    public static GeneratedCSharpExpression Left() =>
        new(
            GeneratedExpressionKind.LeftParameter,
            GeneratedExpressionType.Integer,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Right() =>
        new(
            GeneratedExpressionKind.RightParameter,
            GeneratedExpressionType.Integer,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Condition() =>
        new(
            GeneratedExpressionKind.ConditionParameter,
            GeneratedExpressionType.Boolean,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Text() =>
        new(
            GeneratedExpressionKind.TextParameter,
            GeneratedExpressionType.String,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Values() =>
        new(
            GeneratedExpressionKind.ValuesParameter,
            GeneratedExpressionType.Sequence,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Reference() =>
        new(
            GeneratedExpressionKind.ReferenceParameter,
            GeneratedExpressionType.Reference,
            0,
            false,
            []);

    public static GeneratedCSharpExpression NullReference() =>
        new(
            GeneratedExpressionKind.NullReference,
            GeneratedExpressionType.Reference,
            0,
            false,
            []);

    public static GeneratedCSharpExpression String(string value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.StringLiteral,
            GeneratedExpressionType.String,
            0,
            false,
            [],
            value);
    }

    public static GeneratedCSharpExpression NullString() =>
        new(
            GeneratedExpressionKind.NullString,
            GeneratedExpressionType.String,
            0,
            false,
            []);

    public static GeneratedCSharpExpression Length(
        GeneratedCSharpExpression value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value.Type is not (
            GeneratedExpressionType.String or GeneratedExpressionType.Sequence))
            throw new ArgumentException(
                "Length requires a generated string or sequence.",
                nameof(value));
        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.Length,
            GeneratedExpressionType.Integer,
            0,
            false,
            [value]);
    }

    public static GeneratedCSharpExpression ArrayIndex(
        GeneratedCSharpExpression sequence,
        GeneratedCSharpExpression index) {
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        if (index == null) throw new ArgumentNullException(nameof(index));
        if (sequence.Type != GeneratedExpressionType.Sequence ||
            index.Type != GeneratedExpressionType.Integer)
            throw new ArgumentException(
                "Array access requires a generated sequence and integer.");
        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.ArrayIndex,
            GeneratedExpressionType.Integer,
            0,
            false,
            [sequence, index]);
    }

    public static GeneratedCSharpExpression CastToString(
        GeneratedCSharpExpression value) {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value.Type != GeneratedExpressionType.Reference)
            throw new ArgumentException(
                "The generated cast requires a reference operand.",
                nameof(value));
        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.CastToString,
            GeneratedExpressionType.String,
            0,
            false,
            [value]);
    }

    public static GeneratedCSharpExpression Unary(
        GeneratedExpressionKind kind,
        GeneratedCSharpExpression operand) {
        if (operand == null) throw new ArgumentNullException(nameof(operand));
        var expected = kind switch {
            GeneratedExpressionKind.Not => GeneratedExpressionType.Boolean,
            GeneratedExpressionKind.Negate => GeneratedExpressionType.Integer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (operand.Type != expected)
            throw new ArgumentException(
                "The unary operand has the wrong generated type.",
                nameof(operand));
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
        GeneratedCSharpExpression right) {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        if (kind is GeneratedExpressionKind.Equal or GeneratedExpressionKind.NotEqual) {
            if (left.Type != right.Type ||
                left.Type is not (
                    GeneratedExpressionType.Integer or
                    GeneratedExpressionType.String or
                    GeneratedExpressionType.Sequence or
                    GeneratedExpressionType.Reference))
                throw new ArgumentException(
                    "Equality operands have incompatible generated types.");
            return new GeneratedCSharpExpression(
                kind,
                GeneratedExpressionType.Boolean,
                0,
                false,
                [left, right]);
        }
        if (kind == GeneratedExpressionKind.StringConcat) {
            if (left.Type != GeneratedExpressionType.String ||
                right.Type != GeneratedExpressionType.String)
                throw new ArgumentException(
                    "String concatenation requires generated strings.");
            return new GeneratedCSharpExpression(
                kind,
                GeneratedExpressionType.String,
                0,
                false,
                [left, right]);
        }
        var (operandType, resultType) = kind switch {
            GeneratedExpressionKind.Add or
            GeneratedExpressionKind.Subtract or
            GeneratedExpressionKind.Multiply or
            GeneratedExpressionKind.Divide or
            GeneratedExpressionKind.Remainder =>
                (GeneratedExpressionType.Integer, GeneratedExpressionType.Integer),
            GeneratedExpressionKind.AndAlso or
            GeneratedExpressionKind.OrElse =>
                (GeneratedExpressionType.Boolean, GeneratedExpressionType.Boolean),
            GeneratedExpressionKind.LessThan or
            GeneratedExpressionKind.LessThanOrEqual or
            GeneratedExpressionKind.GreaterThan or
            GeneratedExpressionKind.GreaterThanOrEqual =>
                (GeneratedExpressionType.Integer, GeneratedExpressionType.Boolean),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (left.Type != operandType || right.Type != operandType)
            throw new ArgumentException("The binary operands have the wrong generated type.");
        return new GeneratedCSharpExpression(
            kind,
            resultType,
            0,
            false,
            [left, right]);
    }

    public static GeneratedCSharpExpression Conditional(
        GeneratedCSharpExpression condition,
        GeneratedCSharpExpression whenTrue,
        GeneratedCSharpExpression whenFalse) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (whenTrue == null) throw new ArgumentNullException(nameof(whenTrue));
        if (whenFalse == null) throw new ArgumentNullException(nameof(whenFalse));
        if (condition.Type != GeneratedExpressionType.Boolean)
            throw new ArgumentException(
                "The conditional guard must be Boolean.",
                nameof(condition));
        if (whenTrue.Type != whenFalse.Type)
            throw new ArgumentException(
                "The conditional branches must have the same type.",
                nameof(whenFalse));
        return new GeneratedCSharpExpression(
            GeneratedExpressionKind.Conditional,
            whenTrue.Type,
            0,
            false,
            [condition, whenTrue, whenFalse]);
    }

    public string Render() {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    internal bool TryEvaluateIntegerConstant(out long value) {
        if (Type != GeneratedExpressionType.Integer) {
            value = 0;
            return false;
        }
        try {
            switch (Kind) {
                case GeneratedExpressionKind.IntegerLiteral:
                    value = IntegerValue;
                    return true;
                case GeneratedExpressionKind.Negate
                    when Children[0].TryEvaluateIntegerConstant(out var operand):
                    value = checked(-operand);
                    return true;
                case GeneratedExpressionKind.Add:
                case GeneratedExpressionKind.Subtract:
                case GeneratedExpressionKind.Multiply:
                case GeneratedExpressionKind.Divide:
                case GeneratedExpressionKind.Remainder:
                    if (!Children[0].TryEvaluateIntegerConstant(out var left) ||
                        !Children[1].TryEvaluateIntegerConstant(out var right))
                        break;
                    value = Kind switch {
                        GeneratedExpressionKind.Add => checked(left + right),
                        GeneratedExpressionKind.Subtract => checked(left - right),
                        GeneratedExpressionKind.Multiply => checked(left * right),
                        GeneratedExpressionKind.Divide => checked(left / right),
                        GeneratedExpressionKind.Remainder => left % right,
                        _ => throw new InvalidOperationException()
                    };
                    return true;
                case GeneratedExpressionKind.Conditional
                    when TryEvaluateBooleanConstant(Children[0], out var condition):
                    return Children[condition ? 1 : 2]
                        .TryEvaluateIntegerConstant(out value);
            }
        }
        catch (ArithmeticException) {
        }
        value = 0;
        return false;
    }

    private static bool TryEvaluateBooleanConstant(
        GeneratedCSharpExpression expression,
        out bool value) {
        switch (expression.Kind) {
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
                    TryEvaluateBooleanConstant(expression.Children[1], out var right)) {
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

    private void AppendTo(StringBuilder builder) {
        switch (Kind) {
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
                builder.Append(")[");
                Children[1].AppendTo(builder);
                builder.Append(']');
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
                builder.Append(BinaryToken(Kind));
                builder.Append(' ');
                Children[1].AppendTo(builder);
                builder.Append(')');
                return;
        }
    }

    private static string BinaryToken(GeneratedExpressionKind kind) => kind switch {
        GeneratedExpressionKind.Add => "+",
        GeneratedExpressionKind.Subtract => "-",
        GeneratedExpressionKind.Multiply => "*",
        GeneratedExpressionKind.Divide => "/",
        GeneratedExpressionKind.Remainder => "%",
        GeneratedExpressionKind.AndAlso => "&&",
        GeneratedExpressionKind.OrElse => "||",
        GeneratedExpressionKind.Equal => "==",
        GeneratedExpressionKind.NotEqual => "!=",
        GeneratedExpressionKind.LessThan => "<",
        GeneratedExpressionKind.LessThanOrEqual => "<=",
        GeneratedExpressionKind.GreaterThan => ">",
        GeneratedExpressionKind.GreaterThanOrEqual => ">=",
        GeneratedExpressionKind.StringConcat => "+",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public sealed record GeneratedCSharpCase(
    GeneratedCSharpExpression Expression,
    long Left,
    long Right,
    bool Condition) {
    public string? Text { get; init; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The value is passed to compiled code as a long[] argument.")]
    public long[]? Values { get; init; }
    public object? Reference { get; init; }

    public string Source =>
        "#nullable enable\n" +
        "public static class SharpProofGeneratedFrontend {\n" +
        "    public static " +
        ReturnType(Expression.Type) +
        " Target(long left, long right, bool condition, string? text, long[]? values, object? reference) => " +
        Expression.Render() +
        ";\n" +
        "}\n";

    private static string ReturnType(GeneratedExpressionType type) => type switch {
        GeneratedExpressionType.Boolean => "bool",
        GeneratedExpressionType.Integer => "long",
        GeneratedExpressionType.String => "string?",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}

public sealed class SmallCSharpCaseGenerator(int seed) {
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

    private static readonly long[] LiteralIntegers = [-3, -1, 0, 1, 2, 3];
    private readonly Random _random = new(seed);

    public GeneratedCSharpCase Next(int maximumDepth = 4) {
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        var values = _random.Next(4) == 0
            ? null
            : Enumerable.Range(0, _random.Next(4))
                .Select(_ => InterestingIntegers[_random.Next(InterestingIntegers.Length)])
                .ToArray();
        var index = InterestingIntegers[_random.Next(InterestingIntegers.Length)];
        if (_random.Next(3) == 0 && values != null)
            index = _random.Next(2) == 0 ? -1 : values.Length;
        var expression = _random.Next(10) switch {
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
                GeneratedCSharpExpression.Reference()),
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
                : InterestingIntegers[_random.Next(InterestingIntegers.Length)],
            InterestingIntegers[_random.Next(InterestingIntegers.Length)],
            _random.Next(2) == 0) {
            Text = _random.Next(4) switch {
                0 => null,
                1 => "",
                2 => "sharp",
                _ => "proof"
            },
            Values = values,
            Reference = _random.Next(3) switch {
                0 => null,
                1 => "sharp",
                _ => new object()
            }
        };
    }

    private GeneratedCSharpExpression String(int depth) {
        if (depth == 0) return StringLeaf();
        return _random.Next(4) switch {
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

    private GeneratedCSharpExpression StringLeaf() => _random.Next(5) switch {
        0 => GeneratedCSharpExpression.Text(),
        1 => GeneratedCSharpExpression.NullString(),
        2 => GeneratedCSharpExpression.String(""),
        3 => GeneratedCSharpExpression.String("sharp"),
        _ => GeneratedCSharpExpression.String("proof")
    };

    private GeneratedCSharpExpression Integer(int depth) {
        if (depth == 0) return IntegerLeaf();
        return _random.Next(6) switch {
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

    private GeneratedCSharpExpression IntegerBinary(int depth) {
        var kind = _random.Next(5) switch {
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
            left = GeneratedCSharpExpression.Left();
        if (kind is GeneratedExpressionKind.Divide or GeneratedExpressionKind.Remainder &&
            right.TryEvaluateIntegerConstant(out var divisor) &&
            divisor == 0)
            right = GeneratedCSharpExpression.Right();
        return GeneratedCSharpExpression.Binary(kind, left, right);
    }

    private GeneratedCSharpExpression IntegerLeaf() => _random.Next(4) switch {
        0 => GeneratedCSharpExpression.Left(),
        1 => GeneratedCSharpExpression.Right(),
        _ => GeneratedCSharpExpression.Integer(
            LiteralIntegers[_random.Next(LiteralIntegers.Length)])
    };

    private GeneratedCSharpExpression Boolean(int depth) {
        if (depth == 0) return BooleanLeaf();
        return _random.Next(6) switch {
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

    private GeneratedCSharpExpression BooleanLeaf() => _random.Next(3) switch {
        0 => GeneratedCSharpExpression.Condition(),
        1 => GeneratedCSharpExpression.Boolean(false),
        _ => GeneratedCSharpExpression.Boolean(true)
    };

    private GeneratedExpressionKind RandomComparison() => _random.Next(6) switch {
        0 => GeneratedExpressionKind.Equal,
        1 => GeneratedExpressionKind.NotEqual,
        2 => GeneratedExpressionKind.LessThan,
        3 => GeneratedExpressionKind.LessThanOrEqual,
        4 => GeneratedExpressionKind.GreaterThan,
        _ => GeneratedExpressionKind.GreaterThanOrEqual
    };
}

public sealed record FrontendDifferentialResult(
    FuzzOracleStatus Status,
    string Detail,
    IrExceptionKind? ExceptionKind = null);

public sealed record FrontendSemanticEdgeCase(
    string ReturnType,
    string Parameters,
    string Expression,
    IReadOnlyList<object?> Arguments,
    FrontendSubsetDecision ExpectedDecision,
    FrontendAbstention ExpectedAbstention);

public sealed record FrontendSemanticEdgeResult(
    FuzzOracleStatus Status,
    FrontendSubsetDecision? ActualDecision,
    FrontendAbstention? ActualAbstention,
    string Detail,
    IrExceptionKind? ExceptionKind = null);

public sealed class FrontendDifferentialOracle {
    private const string SemanticEdgeMethodPrefix = "EdgeTarget";
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    public FrontendDifferentialResult Compare(
        GeneratedCSharpCase generated,
        CancellationToken cancellationToken = default) {
        if (generated == null) throw new ArgumentNullException(nameof(generated));
        return CompareBatch([generated], cancellationToken)[0];
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Oracle methods intentionally share an instance-shaped test API.")]
    public ImmutableArray<FrontendDifferentialResult> CompareBatch(
        IReadOnlyList<GeneratedCSharpCase> generatedCases,
        CancellationToken cancellationToken = default) {
        if (generatedCases == null)
            throw new ArgumentNullException(nameof(generatedCases));
        if (generatedCases.Count == 0) return [];
        if (generatedCases.Any(static generated => generated == null))
            throw new ArgumentException(
                "Generated cases cannot contain null.",
                nameof(generatedCases));
        cancellationToken.ThrowIfCancellationRequested();

        var source = CreateBatchSource(generatedCases);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            "SharpProofFrontendFuzz",
            [syntaxTree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emit.Success) {
            var failure = Mismatch(
                "Generated C# did not compile: " +
                FormatErrors(emit.Diagnostics));
            return [.. Enumerable.Repeat(failure, generatedCases.Count)];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var model = compilation.GetSemanticModel(syntaxTree);
        var methodSyntaxes = syntaxTree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .OrderBy(
                static method => ParseMethodIndex(
                    method.Identifier.ValueText))
            .ToArray();
        if (methodSyntaxes.Length != generatedCases.Count) {
            var failure = Mismatch(
                "Roslyn exposed an unexpected generated method count.");
            return [.. Enumerable.Repeat(failure, generatedCases.Count)];
        }

        image.Position = 0;
        var loadContext = new AssemblyLoadContext(
            "SharpProofFrontendFuzz",
            isCollectible: true);
        try {
            var assembly = loadContext.LoadFromStream(image);
            var runtimeType = assembly.GetType(
                "SharpProofGeneratedFrontend")!;
            var results = ImmutableArray.CreateBuilder<FrontendDifferentialResult>(
                generatedCases.Count);
            for (var index = 0; index < generatedCases.Count; index++) {
                cancellationToken.ThrowIfCancellationRequested();
                var methodSyntax = methodSyntaxes[index];
                var methodSymbol = (IMethodSymbol?)model.GetDeclaredSymbol(
                    methodSyntax,
                    cancellationToken);
                var operation = GetExpressionOperation(
                    model,
                    methodSyntax.ExpressionBody!.Expression,
                    cancellationToken);
                if (methodSymbol == null || operation == null) {
                    results.Add(
                        Mismatch(
                            "Roslyn did not expose the generated method operation."));
                    continue;
                }

                var factory = new IrFactory();
                var lowering = new RoslynOperationLowerer(factory).Lower(operation);
                if (!lowering.IsExact) {
                    results.Add(
                        Mismatch(
                            "Generated supported C# closed the frontend subset: " +
                            lowering.Classification.Abstention +
                            "."));
                    continue;
                }

                var generated = generatedCases[index];
                var environment = CreateEnvironment(
                    factory,
                    methodSymbol,
                    lowering,
                    generated);
                var interpreted = new IrInterpreter(factory).Evaluate(
                    lowering.Term,
                    environment);
                var runtimeMethod = runtimeType.GetMethod(
                    MethodName(index),
                    BindingFlags.Public | BindingFlags.Static)!;
                var actual = InvokeMethod(
                    runtimeMethod,
                    generated,
                    cancellationToken);
                results.Add(CompareOutcomes(interpreted, actual));
            }
            return results.ToImmutable();
        }
        finally {
            loadContext.Unload();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Oracle methods intentionally share an instance-shaped test API.")]
    public ImmutableArray<FrontendSemanticEdgeResult> CompareSemanticEdges(
        IReadOnlyList<FrontendSemanticEdgeCase> cases,
        CancellationToken cancellationToken = default) {
        if (cases == null) throw new ArgumentNullException(nameof(cases));
        if (cases.Count == 0) return [];
        for (var index = 0; index < cases.Count; index++) {
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
                throw new ArgumentException(
                    $"Semantic edge case {index} is incomplete.",
                    nameof(cases));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var source = CreateSemanticEdgeSource(cases);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            "SharpProofFrontendSemanticEdges",
            [syntaxTree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emit.Success)
            return RepeatSemanticFailure(
                cases.Count,
                "Generated semantic-edge C# did not compile: " +
                FormatErrors(emit.Diagnostics));

        var model = compilation.GetSemanticModel(syntaxTree);
        var methods = syntaxTree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText.StartsWith(
                SemanticEdgeMethodPrefix,
                StringComparison.Ordinal))
            .OrderBy(static method => ParseSemanticEdgeMethodIndex(
                method.Identifier.ValueText))
            .ToArray();
        if (methods.Length != cases.Count)
            return RepeatSemanticFailure(
                cases.Count,
                "Roslyn exposed an unexpected semantic-edge method count.");

        image.Position = 0;
        var loadContext = new AssemblyLoadContext(
            "SharpProofFrontendSemanticEdges",
            isCollectible: true);
        try {
            var assembly = loadContext.LoadFromStream(image);
            var runtimeType = assembly.GetType(
                "SharpProofGeneratedFrontendEdges")!;
            var results =
                ImmutableArray.CreateBuilder<FrontendSemanticEdgeResult>(
                    cases.Count);
            for (var index = 0; index < cases.Count; index++) {
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
        }
        finally {
            loadContext.Unload();
        }
    }

    private static Dictionary<IrVarId, IrValue> CreateEnvironment(
        IrFactory factory,
        IMethodSymbol method,
        FrontendLoweringResult lowering,
        GeneratedCSharpCase generated) {
        var environment = new Dictionary<IrVarId, IrValue>();
        foreach (var binding in lowering.Variables) {
            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    method))
                continue;
            var value = parameter.Ordinal switch {
                0 => factory.CreateIntegerValue(generated.Left),
                1 => factory.CreateIntegerValue(generated.Right),
                2 => factory.CreateBooleanValue(generated.Condition),
                3 => generated.Text == null
                    ? factory.CreateNullValue(factory.StringType)
                    : factory.CreateStringValue(generated.Text),
                4 => generated.Values == null
                    ? factory.CreateNullValue(
                        factory.GetVariableInfo(binding.Variable).Type)
                    : factory.CreateSequenceValue(
                        factory.GetVariableInfo(binding.Variable).Type,
                        generated.Values.Select(factory.CreateIntegerValue)),
                5 => generated.Reference == null
                    ? factory.CreateNullValue(factory.ObjectType)
                    : factory.CreateReferenceValue(
                        factory.ObjectType,
                        generated.Reference),
                _ => throw new InvalidOperationException(
                    "The generated method has an unexpected parameter.")
            };
            environment.Add(binding.Variable, value);
        }
        return environment;
    }

    private static FrontendSemanticEdgeResult CompareSemanticEdge(
        FrontendSemanticEdgeCase generated,
        MethodDeclarationSyntax methodSyntax,
        SemanticModel model,
        Type runtimeType,
        int index,
        CancellationToken cancellationToken) {
        var method = (IMethodSymbol?)model.GetDeclaredSymbol(
            methodSyntax,
            cancellationToken);
        var operation = GetExpressionOperation(
            model,
            methodSyntax.ExpressionBody!.Expression,
            cancellationToken);
        if (method == null || operation == null)
            return SemanticFailure(
                null,
                null,
                "Roslyn did not expose the semantic-edge method operation.");
        if (generated.Arguments.Count != method.Parameters.Length)
            return SemanticFailure(
                null,
                null,
                "The semantic-edge runtime argument count is incorrect.");

        var factory = new IrFactory();
        var lowering = new RoslynOperationLowerer(factory).Lower(operation);
        var actual = lowering.Classification;
        if (actual.Decision != generated.ExpectedDecision ||
            actual.Abstention != generated.ExpectedAbstention)
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
        if (!actual.IsExact) {
            if (lowering.Term is not IrOpaqueTerm)
                return SemanticFailure(
                    actual.Decision,
                    actual.Abstention,
                    "A closed abstention did not produce an opaque root term.");
            return SemanticAgreement(actual);
        }

        var environment = CreateSemanticEdgeEnvironment(
            factory,
            method,
            lowering,
            generated.Arguments);
        var interpreted = new IrInterpreter(factory).Evaluate(
            lowering.Term,
            environment);
        var runtimeMethod = runtimeType.GetMethod(
            SemanticEdgeMethodName(index),
            BindingFlags.Public | BindingFlags.Static)!;
        var comparison = CompareOutcomes(
            interpreted,
            InvokeMethod(
                runtimeMethod,
                generated.Arguments,
                cancellationToken));
        return new FrontendSemanticEdgeResult(
            comparison.Status,
            actual.Decision,
            actual.Abstention,
            comparison.Detail,
            comparison.ExceptionKind);
    }

    private static Dictionary<IrVarId, IrValue>
        CreateSemanticEdgeEnvironment(
            IrFactory factory,
            IMethodSymbol method,
            FrontendLoweringResult lowering,
            IReadOnlyList<object?> arguments) {
        var environment = new Dictionary<IrVarId, IrValue>();
        foreach (var binding in lowering.Variables) {
            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    method))
                continue;
            var type = factory.GetVariableInfo(binding.Variable).Type;
            environment.Add(
                binding.Variable,
                CreateSemanticEdgeValue(
                    factory,
                    type,
                    arguments[parameter.Ordinal]));
        }
        return environment;
    }

    private static IrValue CreateSemanticEdgeValue(
        IrFactory factory,
        IrTypeId type,
        object? value) {
        var kind = factory.GetTypeInfo(type).Kind;
        if (value == null) {
            if (kind is IrTypeKind.String or
                IrTypeKind.Reference or
                IrTypeKind.Sequence)
                return factory.CreateNullValue(type);
            throw new InvalidOperationException(
                "A non-nullable semantic-edge variable received null.");
        }
        return kind switch {
            IrTypeKind.Boolean when value is bool boolean =>
                factory.CreateBooleanValue(boolean),
            IrTypeKind.Integer when value is sbyte or byte or short or ushort or
                int or uint or long or char =>
                factory.CreateIntegerValue(Convert.ToInt64(
                    value,
                    CultureInfo.InvariantCulture)),
            IrTypeKind.String when value is string text =>
                factory.CreateStringValue(text),
            IrTypeKind.Reference =>
                factory.CreateReferenceValue(type, value),
            _ => throw new InvalidOperationException(
                "A semantic-edge value is outside the executable IR subset.")
        };
    }

    private static IOperation? GetExpressionOperation(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken) {
        var operation = model.GetOperation(expression, cancellationToken);
        if (operation != null) return operation;
        return expression switch {
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
        CancellationToken cancellationToken) =>
        InvokeMethod(
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

    private static RuntimeOutcome InvokeMethod(
        MethodInfo method,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        try {
            return RuntimeOutcome.Returned(
                method.Invoke(null, [.. arguments]));
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException != null) {
            return RuntimeOutcome.Threw(exception.InnerException);
        }
    }

    private static string CreateBatchSource(
        IReadOnlyList<GeneratedCSharpCase> generatedCases) {
        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        builder.AppendLine("public static class SharpProofGeneratedFrontend {");
        for (var index = 0; index < generatedCases.Count; index++) {
            var generated = generatedCases[index];
            builder.Append("    public static ");
            builder.Append(ReturnType(generated.Expression.Type));
            builder.Append(' ');
            builder.Append(MethodName(index));
            builder.Append(
                "(long left, long right, bool condition, string? text, long[]? values, object? reference) => ");
            builder.Append(generated.Expression.Render());
            builder.AppendLine(";");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string CreateSemanticEdgeSource(
        IReadOnlyList<FrontendSemanticEdgeCase> cases) {
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
        for (var index = 0; index < cases.Count; index++) {
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

    private static string MethodName(int index) =>
        "Target" + index.ToString(CultureInfo.InvariantCulture);

    private static string SemanticEdgeMethodName(int index) =>
        SemanticEdgeMethodPrefix +
        index.ToString(CultureInfo.InvariantCulture);

    private static int ParseMethodIndex(string name) =>
        int.Parse(
            name.AsSpan("Target".Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

    private static int ParseSemanticEdgeMethodIndex(string name) =>
        int.Parse(
            name.AsSpan(SemanticEdgeMethodPrefix.Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

    private static string ReturnType(GeneratedExpressionType type) => type switch {
        GeneratedExpressionType.Boolean => "bool",
        GeneratedExpressionType.Integer => "long",
        GeneratedExpressionType.String => "string?",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static FrontendDifferentialResult CompareOutcomes(
        IrEvaluationResult interpreted,
        RuntimeOutcome actual) {
        if (actual.Exception != null) {
            var kind = actual.Exception switch {
                DivideByZeroException => IrExceptionKind.DivideByZero,
                OverflowException => IrExceptionKind.Overflow,
                NullReferenceException => IrExceptionKind.NullReference,
                IndexOutOfRangeException => IrExceptionKind.IndexOutOfRange,
                InvalidCastException => IrExceptionKind.InvalidCast,
                _ => (IrExceptionKind?)null
            };
            if (interpreted.Status == IrEvaluationStatus.Exception &&
                kind != null &&
                interpreted.Exception!.Kind == kind)
                return Agreement(kind);
            return Mismatch(
                "Compiled C# threw " +
                actual.Exception.GetType().Name +
                " while the lowered IR reported " +
                Describe(interpreted) +
                ".");
        }

        if (interpreted.Status != IrEvaluationStatus.Value)
            return Mismatch(
                "Compiled C# returned normally while the lowered IR reported " +
                Describe(interpreted) +
                ".");
        var agrees = interpreted.Value!.Kind switch {
            IrValueKind.Boolean =>
                actual.Value is bool value &&
                value == interpreted.Value.Boolean,
            IrValueKind.Integer =>
                actual.Value is long value &&
                value == interpreted.Value.Integer,
            IrValueKind.String =>
                actual.Value is string value &&
                string.Equals(
                    value,
                    interpreted.Value.String,
                    StringComparison.Ordinal),
            IrValueKind.Null => actual.Value == null,
            _ => false
        };
        return agrees
            ? Agreement()
            : Mismatch(
                "Compiled C# and the lowered IR produced different values.");
    }

    private static string Describe(IrEvaluationResult result) => result.Status switch {
        IrEvaluationStatus.Value => "a value",
        IrEvaluationStatus.Exception =>
            "exception " + result.Exception!.Kind,
        IrEvaluationStatus.Unsupported =>
            "unsupported " + result.Unsupported!.Reason,
        _ => result.Status.ToString()
    };

    private static string FormatErrors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            " | ",
            diagnostics
                .Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .OrderBy(static diagnostic =>
                    diagnostic.Location.SourceSpan.Start)
                .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .Select(static diagnostic =>
                    diagnostic.Id +
                    ": " +
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)));

    private static ImmutableArray<MetadataReference> CreateReferences() {
        var trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedAssemblies
            .Split(Path.PathSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static FrontendDifferentialResult Agreement(
        IrExceptionKind? exceptionKind = null) =>
        new(FuzzOracleStatus.Agreement, "", exceptionKind);

    private static FrontendDifferentialResult Mismatch(string detail) =>
        new(FuzzOracleStatus.Mismatch, detail);

    private static ImmutableArray<FrontendSemanticEdgeResult>
        RepeatSemanticFailure(
            int count,
            string detail) =>
        [.. Enumerable.Repeat(
            SemanticFailure(null, null, detail),
            count)];

    private static FrontendSemanticEdgeResult SemanticAgreement(
        FrontendSubsetClassification classification) =>
        new(
            FuzzOracleStatus.Agreement,
            classification.Decision,
            classification.Abstention,
            "");

    private static FrontendSemanticEdgeResult SemanticFailure(
        FrontendSubsetDecision? decision,
        FrontendAbstention? abstention,
        string detail) =>
        new(
            FuzzOracleStatus.Mismatch,
            decision,
            abstention,
            detail);

    private sealed record RuntimeOutcome(object? Value, Exception? Exception) {
        internal static RuntimeOutcome Returned(object? value) =>
            new(value, null);

        internal static RuntimeOutcome Threw(Exception exception) =>
            new(null, exception);
    }
}

public static class CSharpStructuralShrinker {
    public static GeneratedCSharpCase Minimize(
        GeneratedCSharpCase generated,
        Func<GeneratedCSharpCase, bool> preservesMismatch,
        CancellationToken cancellationToken = default) {
        if (generated == null) throw new ArgumentNullException(nameof(generated));
        if (preservesMismatch == null)
            throw new ArgumentNullException(nameof(preservesMismatch));

        var current = generated;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            foreach (var candidateExpression in GetCandidates(current.Expression)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidateExpression.NodeCount >= current.Expression.NodeCount)
                    continue;
                var candidate = current with { Expression = candidateExpression };
                if (!preservesMismatch(candidate)) continue;
                current = candidate;
                changed = true;
                break;
            }
            if (!changed) return current;
        }
    }

    public static ImmutableArray<GeneratedCSharpExpression> GetCandidates(
        GeneratedCSharpExpression expression) {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        var candidates = new List<GeneratedCSharpExpression>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(GeneratedCSharpExpression candidate) {
            if (candidate.NodeCount >= expression.NodeCount) return;
            if (seen.Add(candidate.Render())) candidates.Add(candidate);
        }

        foreach (var child in expression.Children)
            if (child.Type == expression.Type)
                Add(child);

        switch (expression.Type) {
            case GeneratedExpressionType.Integer:
                Add(GeneratedCSharpExpression.Integer(0));
                Add(GeneratedCSharpExpression.Integer(1));
                Add(GeneratedCSharpExpression.Left());
                Add(GeneratedCSharpExpression.Right());
                break;
            case GeneratedExpressionType.Boolean:
                Add(GeneratedCSharpExpression.Boolean(false));
                Add(GeneratedCSharpExpression.Boolean(true));
                Add(GeneratedCSharpExpression.Condition());
                break;
            case GeneratedExpressionType.String:
                Add(GeneratedCSharpExpression.NullString());
                Add(GeneratedCSharpExpression.String(""));
                Add(GeneratedCSharpExpression.Text());
                break;
            case GeneratedExpressionType.Sequence:
                Add(GeneratedCSharpExpression.Values());
                break;
            case GeneratedExpressionType.Reference:
                Add(GeneratedCSharpExpression.Reference());
                Add(GeneratedCSharpExpression.NullReference());
                break;
        }

        for (var childIndex = 0;
             childIndex < expression.Children.Length;
             childIndex++) {
            foreach (var childCandidate in GetCandidates(
                         expression.Children[childIndex])) {
                var rebuilt = TryReplaceChild(
                    expression,
                    childIndex,
                    childCandidate);
                if (rebuilt != null) Add(rebuilt);
            }
        }
        return [.. candidates];
    }

    private static GeneratedCSharpExpression? TryReplaceChild(
        GeneratedCSharpExpression expression,
        int childIndex,
        GeneratedCSharpExpression child) {
        try {
            return expression.Kind switch {
                GeneratedExpressionKind.Not or
                GeneratedExpressionKind.Negate =>
                    childIndex == 0
                        ? GeneratedCSharpExpression.Unary(expression.Kind, child)
                        : null,
                GeneratedExpressionKind.Conditional =>
                    GeneratedCSharpExpression.Conditional(
                        childIndex == 0 ? child : expression.Children[0],
                        childIndex == 1 ? child : expression.Children[1],
                        childIndex == 2 ? child : expression.Children[2]),
                GeneratedExpressionKind.Length =>
                    childIndex == 0
                        ? GeneratedCSharpExpression.Length(child)
                        : null,
                GeneratedExpressionKind.ArrayIndex =>
                    GeneratedCSharpExpression.ArrayIndex(
                        childIndex == 0 ? child : expression.Children[0],
                        childIndex == 1 ? child : expression.Children[1]),
                GeneratedExpressionKind.CastToString =>
                    childIndex == 0
                        ? GeneratedCSharpExpression.CastToString(child)
                        : null,
                GeneratedExpressionKind.Add or
                GeneratedExpressionKind.Subtract or
                GeneratedExpressionKind.Multiply or
                GeneratedExpressionKind.Divide or
                GeneratedExpressionKind.Remainder or
                GeneratedExpressionKind.AndAlso or
                GeneratedExpressionKind.OrElse or
                GeneratedExpressionKind.Equal or
                GeneratedExpressionKind.NotEqual or
                GeneratedExpressionKind.LessThan or
                GeneratedExpressionKind.LessThanOrEqual or
                GeneratedExpressionKind.GreaterThan or
                GeneratedExpressionKind.GreaterThanOrEqual or
                GeneratedExpressionKind.StringConcat =>
                    GeneratedCSharpExpression.Binary(
                        expression.Kind,
                        childIndex == 0 ? child : expression.Children[0],
                        childIndex == 1 ? child : expression.Children[1]),
                _ => null
            };
        }
        catch (ArgumentException) {
            return null;
        }
    }
}
