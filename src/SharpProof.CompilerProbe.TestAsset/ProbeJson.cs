namespace SharpProof.CompilerProbe.TestAsset;

internal sealed class ProbeJsonObject
{
    private readonly StringBuilder _builder = new();
    private bool _first = true;

    internal ProbeJsonObject()
    {
        _builder.Append('{');
    }

    internal void PropertyName(string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        if (!_first)
        {
            _builder.Append(',');
        }

        _first = false;
        AppendString(name);
        _builder.Append(':');
    }

    internal void String(string name, string value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        PropertyName(name);
        AppendString(value);
    }

    internal void Boolean(string name, bool value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        PropertyName(name);
        _builder.Append(value ? "true" : "false");
    }

    internal void Integer(string name, int value)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        PropertyName(name);
        _builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    internal void StringArray(string name, IEnumerable<string> values)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        PropertyName(name);
        AppendArray(values, AppendString);
    }

    internal void RawArray(string name, IEnumerable<string> rows)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        rows = RepositoryParameterGuard.NotNull(rows, nameof(rows));
        PropertyName(name);
        AppendArray(rows, row => { row = RepositoryParameterGuard.NotNull(row, nameof(row)); _builder.Append(row); });
    }

    internal void Object(string name, Action<ProbeJsonObject> append)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        append = RepositoryParameterGuard.NotNull(append, nameof(append));
        PropertyName(name);
        var child = new ProbeJsonObject();
        append(child);
        _builder.Append(child.Complete());
    }
    internal string Complete()
    {
        _builder.Append('}');
        return _builder.ToString();
    }

    private void AppendArray(
        IEnumerable<string> values,
        Action<string> append)
    {
        append = RepositoryParameterGuard.NotNull(append, nameof(append));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        _builder.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                _builder.Append(',');
            }

            first = false;
            append(value);
        }
        _builder.Append(']');
    }

    private void AppendString(string value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        _builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                case '\b':
                    _builder.Append("\\b");
                    break;
                case '\f':
                    _builder.Append("\\f");
                    break;
                case '\n':
                    _builder.Append("\\n");
                    break;
                case '\r':
                    _builder.Append("\\r");
                    break;
                case '\t':
                    _builder.Append("\\t");
                    break;
                default:
                    if (character < ' ' || character > '\u007f')
                    {
                        _builder.Append("\\u");
                        _builder.Append(
                            ((int)character).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _builder.Append(character);
                    }
                    break;
            }
        }
        _builder.Append('"');
    }
}
