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
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
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
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        if (value is null)
        {
            throw new System.ArgumentNullException(nameof(value));
        }
        PropertyName(name);
        AppendString(Builder, value);
    }

    internal void Boolean(string name, bool value)
    {
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        PropertyName(name);
        Builder.Append(value ? "true" : "false");
    }

    internal void Integer(string name, int value)
    {
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        PropertyName(name);
        Builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    internal void StringArray(string name, IEnumerable<string> values)
    {
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        PropertyName(name);
        AppendStringArray(Builder, values);
    }

    internal void RawArray(string name, IEnumerable<string> rows)
    {
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        if (rows is null)
        {
            throw new System.ArgumentNullException(nameof(rows));
        }
        PropertyName(name);
        AppendArray(Builder, rows, static (builder, row) => { if (builder is null) { throw new System.ArgumentNullException(nameof(builder)); } if (row is null) { throw new System.ArgumentNullException(nameof(row)); } builder.Append(row); });
    }

    internal void Complete()
    {
        Builder.Append('}');
    }

    private static void AppendStringArray(
        StringBuilder builder,
        IEnumerable<string> values)
    {
        if (builder is null)
        {
            throw new System.ArgumentNullException(nameof(builder));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
        AppendArray(builder, values, AppendString);
    }

    private static void AppendArray(
        StringBuilder builder,
        IEnumerable<string> values,
        Action<StringBuilder, string> append)
    {
        if (append is null)
        {
            throw new System.ArgumentNullException(nameof(append));
        }
        if (builder is null)
        {
            throw new System.ArgumentNullException(nameof(builder));
        }
        if (values is null)
        {
            throw new System.ArgumentNullException(nameof(values));
        }
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
        if (builder is null)
        {
            throw new System.ArgumentNullException(nameof(builder));
        }
        if (value is null)
        {
            throw new System.ArgumentNullException(nameof(value));
        }
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
