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
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
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
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        PropertyName(name);
        AppendString(Builder, value);
    }

    internal void Boolean(string name, bool value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        PropertyName(name);
        Builder.Append(value ? "true" : "false");
    }

    internal void Integer(string name, int value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        PropertyName(name);
        Builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    internal void StringArray(string name, IEnumerable<string> values)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        PropertyName(name);
        AppendStringArray(Builder, values);
    }

    internal void RawArray(string name, IEnumerable<string> rows)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        rows = RepositoryParameterGuard.NotNull(rows, nameof(rows));
        PropertyName(name);
        AppendArray(Builder, rows, static (builder, row) => { builder = RepositoryParameterGuard.NotNull(builder, nameof(builder)); row = RepositoryParameterGuard.NotNull(row, nameof(row)); builder.Append(row); });
    }

    internal void Complete()
    {
        Builder.Append('}');
    }

    private static void AppendStringArray(
        StringBuilder builder,
        IEnumerable<string> values)
    {
        builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        AppendArray(builder, values, AppendString);
    }

    private static void AppendArray(
        StringBuilder builder,
        IEnumerable<string> values,
        Action<StringBuilder, string> append)
    {
        append = RepositoryParameterGuard.NotNull(append, nameof(append));
        builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
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
        builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
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
