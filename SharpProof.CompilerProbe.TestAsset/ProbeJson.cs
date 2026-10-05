namespace SharpProof.CompilerProbe.TestAsset;

internal sealed class ProbeJsonObject
{
    private bool _first = true;

    internal ProbeJsonObject(StringBuilder? builder = null)
    {
        Builder = builder ?? new StringBuilder();
        Builder.Append('{');
    }

    internal StringBuilder Builder { get; }

    internal void PropertyName(string name)
    {
        if (!_first)
        {
            Builder.Append(',');
        }

        _first = false;
        AppendString(Builder, name);
        Builder.Append(':');
    }

    internal void String(string name, string value)
    {
        PropertyName(name);
        AppendString(Builder, value);
    }

    internal void Boolean(string name, bool value)
    {
        PropertyName(name);
        Builder.Append(value ? "true" : "false");
    }

    internal void Integer(string name, int value)
    {
        PropertyName(name);
        Builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    internal void StringArray(string name, IEnumerable<string> values)
    {
        PropertyName(name);
        AppendStringArray(Builder, values);
    }

    internal void RawArray(string name, IEnumerable<string> rows)
    {
        PropertyName(name);
        AppendArray(Builder, rows, static (builder, row) => builder.Append(row));
    }

    internal void Complete()
    {
        Builder.Append('}');
    }

    private static void AppendStringArray(
        StringBuilder builder,
        IEnumerable<string> values)
    {
        AppendArray(builder, values, AppendString);
    }

    private static void AppendArray(
        StringBuilder builder,
        IEnumerable<string> values,
        Action<StringBuilder, string> append)
    {
        builder.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            append(builder, value);
        }
        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ' || character > '\u007f')
                    {
                        builder.Append("\\u");
                        builder.Append(
                            ((int)character).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }
}
