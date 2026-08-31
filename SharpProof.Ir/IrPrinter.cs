namespace SharpProof.Ir;

public sealed partial class IrPrinter(IrFactory factory)
{
    private const int MaxFormatDepth = 1024;

    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private int _formatDepth;

    public string Print(IrTerm term)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));

        _factory.EnsureTerm(term, nameof(term));
        return FormatChild(term);
    }

    private string FormatChild(IrTerm term)
    {
        if (_formatDepth >= MaxFormatDepth)
        {
            throw new InvalidOperationException(
                "IR term exceeds the printer formatting depth limit.");
        }

        _formatDepth++;
        try
        {
            return Format(term);
        }
        finally
        {
            _formatDepth--;
        }
    }

    private string FormatOpaque(IrOpaqueTerm opaque)
    {
        var prefix = opaque.Purity == IrOpaquePurity.Pure
            ? "pure:"
            : "impure:" + opaque.Operation + ":";
        var arguments = string.Join(
            ", ",
            opaque.Arguments.Select(FormatChild));
        var receiver = opaque.Receiver == null
            ? ""
            : FormatChild(opaque.Receiver) +
                (opaque.Arguments.IsDefaultOrEmpty ? "" : "; ");
        return prefix + opaque.Member + "(" + receiver + arguments + ")";
    }

    private string TypeName(IrTypeId type)
    {
        // Keep display names unambiguous and safe to embed in the diagnostic
        // grammar. The local type id disambiguates distinct semantic types
        // that happen to have the same display name.
        return Quote(_factory.GetString(_factory.GetTypeInfo(type).Name)) +
            "#t" + type.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder().Append('"');
        foreach (var character in value)
        {
            var escape = character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null
            };
            if (escape != null)
            {
                builder.Append(escape);
                continue;
            }
            if (character is < ' ' or > '~')
            {
                builder.Append("\\u").Append(
                    ((int)character).ToString(
                        "X4",
                        CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }
        return builder.Append('"').ToString();
    }
}
