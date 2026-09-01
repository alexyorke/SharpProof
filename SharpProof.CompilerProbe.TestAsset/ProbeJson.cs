namespace SharpProof.CompilerProbe.TestAsset;

internal static class ProbeJson
{
    internal static void PropertyName(
        StringBuilder builder,
        ref bool first,
        string name)
    {
        if (!first)
        {
            builder.Append(',');
        }

        first = false;
        String(builder, name);
        builder.Append(':');
    }

    internal static void StringProperty(
        StringBuilder builder,
        ref bool first,
        string name,
        string value)
    {
        PropertyName(builder, ref first, name);
        String(builder, value);
    }

    internal static void BooleanProperty(
        StringBuilder builder,
        ref bool first,
        string name,
        bool value)
    {
        PropertyName(builder, ref first, name);
        builder.Append(value ? "true" : "false");
    }

    internal static void IntegerProperty(
        StringBuilder builder,
        ref bool first,
        string name,
        int value)
    {
        PropertyName(builder, ref first, name);
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    internal static void StringArrayProperty(
        StringBuilder builder,
        ref bool first,
        string name,
        IEnumerable<string> values)
    {
        PropertyName(builder, ref first, name);
        StringArray(builder, values);
    }

    internal static void RawArrayProperty(
        StringBuilder builder,
        ref bool first,
        string name,
        IEnumerable<string> rows)
    {
        PropertyName(builder, ref first, name);
        builder.Append('[');
        var firstRow = true;
        foreach (var row in rows)
        {
            if (!firstRow)
            {
                builder.Append(',');
            }

            firstRow = false;
            builder.Append(row);
        }
        builder.Append(']');
    }

    internal static void StringArray(
        StringBuilder builder,
        IEnumerable<string> values)
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
            String(builder, value);
        }
        builder.Append(']');
    }

    internal static void String(StringBuilder builder, string value)
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
