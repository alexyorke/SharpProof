using System.Text;

namespace SharpProof.Analyzer;

// A JSON number exactly as written. OpenAPI bounds are compared only when the
// text is an integer, so the parser keeps the text rather than a rounded value.
internal sealed class JsonNumber
{
    public JsonNumber(string text)
    {
        Text = RepositoryParameterGuard.NotNull(text, nameof(text));
    }

    public string Text { get; }
}

// Parses the JSON an OpenAPI document uses into dictionaries, lists, strings,
// booleans, JsonNumber, and null. Syntax errors, depth beyond the limit, and
// trailing text fail the whole parse, so a damaged document is never compared.
internal static class JsonDocumentParser
{
    private const int MaximumDepth = 64;

    internal static bool TryParse(string text, out object? root)
    {
        text = RepositoryParameterGuard.NotNull(text, nameof(text));
        root = null;
        try
        {
            root = new Reader(text).ReadDocument();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _index;

        internal Reader(string text)
        {
            _text = RepositoryParameterGuard.NotNull(text, nameof(text));
        }

        internal object? ReadDocument()
        {
            var value = ReadValue(0);
            SkipWhitespace();
            return _index == _text.Length ? value : throw new FormatException("Trailing text.");
        }

        private object? ReadValue(int depth)
        {
            if (depth > MaximumDepth)
            {
                throw new FormatException("Nesting is too deep.");
            }

            SkipWhitespace();
            if (_index >= _text.Length)
            {
                throw new FormatException("Unexpected end of document.");
            }

            switch (_text[_index])
            {
                case '{':
                    return ReadObject(depth + 1);
                case '[':
                    return ReadArray(depth + 1);
                case '"':
                    return ReadString();
                case 't':
                    ReadLiteral("true");
                    return true;
                case 'f':
                    ReadLiteral("false");
                    return false;
                case 'n':
                    ReadLiteral("null");
                    return null;
                default:
                    return ReadNumber();
            }
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            var members = new Dictionary<string, object?>(StringComparer.Ordinal);
            Expect('{');
            SkipWhitespace();
            if (TryConsume('}'))
            {
                return members;
            }

            while (true)
            {
                SkipWhitespace();
                var name = ReadString();
                SkipWhitespace();
                Expect(':');
                members[name] = ReadValue(depth);
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return members;
                }

                Expect(',');
            }
        }

        private List<object?> ReadArray(int depth)
        {
            var items = new List<object?>();
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return items;
            }

            while (true)
            {
                items.Add(ReadValue(depth));
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return items;
                }

                Expect(',');
            }
        }

        private string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (true)
            {
                if (_index >= _text.Length)
                {
                    throw new FormatException("Unterminated string.");
                }

                var current = _text[_index++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current < ' ')
                {
                    throw new FormatException("Control character in string.");
                }

                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                builder.Append(ReadEscape());
            }
        }

        private char ReadEscape()
        {
            if (_index >= _text.Length)
            {
                throw new FormatException("Unterminated escape.");
            }

            var escape = _text[_index++];
            switch (escape)
            {
                case '"' or '\\' or '/':
                    return escape;
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'u':
                    if (_index + 4 > _text.Length ||
                        !int.TryParse(_text.Substring(_index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    {
                        throw new FormatException("Invalid unicode escape.");
                    }

                    _index += 4;
                    return (char)code;
                default:
                    throw new FormatException("Invalid escape.");
            }
        }

        private JsonNumber ReadNumber()
        {
            var start = _index;
            TryConsume('-');
            if (!ConsumeDigits())
            {
                throw new FormatException("Expected a value.");
            }

            if (TryConsume('.') && !ConsumeDigits())
            {
                throw new FormatException("Invalid fraction.");
            }

            if (_index < _text.Length && (_text[_index] is 'e' or 'E'))
            {
                _index++;
                if (_index < _text.Length && (_text[_index] is '+' or '-'))
                {
                    _index++;
                }

                if (!ConsumeDigits())
                {
                    throw new FormatException("Invalid exponent.");
                }
            }

            return new JsonNumber(_text.Substring(start, _index - start));
        }

        private bool ConsumeDigits()
        {
            var start = _index;
            while (_index < _text.Length && _text[_index] is >= '0' and <= '9')
            {
                _index++;
            }

            return _index > start;
        }

        private void ReadLiteral(string literal)
        {
            if (string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Invalid literal.");
            }

            _index += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && _text[_index] is ' ' or '\t' or '\n' or '\r')
            {
                _index++;
            }
        }

        private bool TryConsume(char expected)
        {
            if (_index < _text.Length && _text[_index] == expected)
            {
                _index++;
                return true;
            }

            return false;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
            {
                throw new FormatException("Expected '" + expected + "'.");
            }
        }
    }
}
