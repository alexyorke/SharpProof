namespace SharpProof.Ir;

public sealed class IrPrinter(IrFactory factory) {
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    public string Print(IrTerm term) {
        if (term == null)
            throw new ArgumentNullException(nameof(term));
        _factory.EnsureTerm(term, nameof(term));
        return Format(term);
    }

    private string Format(IrTerm term) =>
        term switch {
            IrBooleanTerm value => value.Value ? "true" : "false",
            IrIntegerTerm value =>
                value.Value.ToString(CultureInfo.InvariantCulture),
            IrStringTerm value => Quote(_factory.GetString(value.Value)),
            IrNullTerm => "((" + TypeName(term.Type) + ")null)",
            IrVariableTerm value => "v" +
                value.Variable.Value.ToString(CultureInfo.InvariantCulture),
            IrOpaqueTerm value => FormatOpaque(value),
            IrUnaryTerm value => "(" +
                IrOperatorCatalog.Get(value.Operator).Token +
                Format(value.Operand) + ")",
            IrBinaryTerm value => "(" + Format(value.Left) + " " +
                IrOperatorCatalog.Get(value.Operator).Token + " " +
                Format(value.Right) + ")",
            IrConditionalTerm value => "(" + Format(value.Condition) + " ? " +
                Format(value.WhenTrue) + " : " + Format(value.WhenFalse) + ")",
            IrCastTerm value =>
                "((" + TypeName(value.Type) + ")" + Format(value.Operand) + ")",
            IrLengthTerm value => "len(" + Format(value.Value) + ")",
            IrSequenceAccessTerm value =>
                Format(value.Sequence) + "[" + Format(value.Index) + "]",
            _ => throw new InvalidOperationException(
                "Unknown IR term kind: " + term.Kind + ".")
        };

    private string FormatOpaque(IrOpaqueTerm opaque) {
        var prefix = opaque.Purity == IrOpaquePurity.Pure
            ? "pure:"
            : "impure:" + opaque.Operation + ":";
        var arguments = string.Join(
            ", ",
            opaque.Arguments.Select(Format));
        var receiver = opaque.Receiver == null
            ? ""
            : Format(opaque.Receiver) +
                (opaque.Arguments.IsDefaultOrEmpty ? "" : "; ");
        return prefix + opaque.Member + "(" + receiver + arguments + ")";
    }

    private string TypeName(IrTypeId type) =>
        _factory.GetString(_factory.GetTypeInfo(type).Name);

    private static string Quote(string value) {
        var builder = new StringBuilder().Append('"');
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
                    ((int)character).ToString(
                        "X4",
                        CultureInfo.InvariantCulture));
            else
                builder.Append(character);
        }
        return builder.Append('"').ToString();
    }
}
