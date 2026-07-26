namespace SharpProof.Ir;

public sealed class IrPrinter(IrFactory factory) {
    private readonly IrFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public string Print(IrTerm term) {
        if (term == null) throw new ArgumentNullException(nameof(term));
        _factory.EnsureTerm(term, nameof(term));
        var builder = new StringBuilder();
        Append(builder, term);
        return builder.ToString();
    }

    private void Append(StringBuilder builder, IrTerm term) {
        switch (term) {
            case IrBooleanTerm boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            case IrIntegerTerm integer:
                builder.Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case IrStringTerm text:
                AppendQuoted(builder, _factory.GetString(text.Value));
                break;
            case IrNullTerm:
                builder.Append("((").Append(GetTypeName(term.Type)).Append(")null)");
                break;
            case IrVariableTerm variable:
                builder.Append('v').Append(variable.Variable.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case IrOpaqueTerm opaque:
                AppendOpaque(builder, opaque);
                break;
            case IrUnaryTerm unary:
                builder.Append('(').Append(unary.Operator == IrUnaryOperator.Not ? '!' : '-');
                Append(builder, unary.Operand);
                builder.Append(')');
                break;
            case IrBinaryTerm binary:
                builder.Append('(');
                Append(builder, binary.Left);
                builder.Append(' ').Append(GetBinaryToken(binary.Operator)).Append(' ');
                Append(builder, binary.Right);
                builder.Append(')');
                break;
            case IrConditionalTerm conditional:
                builder.Append('(');
                Append(builder, conditional.Condition);
                builder.Append(" ? ");
                Append(builder, conditional.WhenTrue);
                builder.Append(" : ");
                Append(builder, conditional.WhenFalse);
                builder.Append(')');
                break;
            case IrCastTerm cast:
                builder.Append("((").Append(GetTypeName(cast.Type)).Append(')');
                Append(builder, cast.Operand);
                builder.Append(')');
                break;
            case IrLengthTerm length:
                builder.Append("len(");
                Append(builder, length.Value);
                builder.Append(')');
                break;
            case IrSequenceAccessTerm access:
                Append(builder, access.Sequence);
                builder.Append('[');
                Append(builder, access.Index);
                builder.Append(']');
                break;
            default:
                throw new InvalidOperationException("Unknown IR term kind: " + term.Kind + ".");
        }
    }

    private void AppendOpaque(StringBuilder builder, IrOpaqueTerm opaque) {
        if (opaque.Purity == IrOpaquePurity.Pure) builder.Append("pure:");
        else {
            builder.Append("impure:").Append(opaque.Operation).Append(':');
        }
        builder.Append(opaque.Member).Append('(');
        if (opaque.Receiver != null) {
            Append(builder, opaque.Receiver);
            if (!opaque.Arguments.IsDefaultOrEmpty) builder.Append("; ");
        }
        for (var index = 0; index < opaque.Arguments.Length; index++) {
            if (index != 0) builder.Append(", ");
            Append(builder, opaque.Arguments[index]);
        }
        builder.Append(')');
    }

    private string GetTypeName(IrTypeId type) {
        var info = _factory.GetTypeInfo(type);
        return _factory.GetString(info.Name);
    }

    private static string GetBinaryToken(IrBinaryOperator @operator) => @operator switch {
        IrBinaryOperator.Add => "+",
        IrBinaryOperator.Subtract => "-",
        IrBinaryOperator.Multiply => "*",
        IrBinaryOperator.Divide => "/",
        IrBinaryOperator.Remainder => "%",
        IrBinaryOperator.AndAlso => "&&",
        IrBinaryOperator.OrElse => "||",
        IrBinaryOperator.Equal => "==",
        IrBinaryOperator.NotEqual => "!=",
        IrBinaryOperator.LessThan => "<",
        IrBinaryOperator.LessThanOrEqual => "<=",
        IrBinaryOperator.GreaterThan => ">",
        IrBinaryOperator.GreaterThanOrEqual => ">=",
        IrBinaryOperator.StringConcat => "++",
        _ => throw new ArgumentOutOfRangeException(nameof(@operator))
    };

    private static void AppendQuoted(StringBuilder builder, string value) {
        builder.Append('"');
        foreach (var character in value) {
            var escape = character switch {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null
            };
            if (escape != null) {
                builder.Append(escape);
                continue;
            }
            if (character is < ' ' or > '~')
                builder.Append("\\u").Append(
                    ((int)character).ToString("X4", CultureInfo.InvariantCulture));
            else builder.Append(character);
        }
        builder.Append('"');
    }
}
