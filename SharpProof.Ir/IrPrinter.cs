namespace SharpProof.Ir;

public sealed partial class IrPrinter(IrFactory factory)
{
    /// <summary>
    /// Caps recursive formatting before it can exhaust the process stack.
    /// The depth is measured iteratively by <see cref="IrTermAnalysis"/>.
    /// </summary>
    private const int MaximumPrintDepth = 256;

    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));

    public string Print(IrTerm term)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));

        _factory.EnsureTerm(term, nameof(term));
        if (IrTermAnalysis.GetDepth(term) > MaximumPrintDepth)
        {
            throw new InvalidOperationException(
                "The IR term exceeds the printer depth budget of " +
                MaximumPrintDepth.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return Format(term);
    }

    private string FormatOpaque(IrOpaqueTerm opaque)
    {
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

    private string TypeName(IrTypeId type)
    {
        return _factory.GetString(_factory.GetTypeInfo(type).Name);
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
