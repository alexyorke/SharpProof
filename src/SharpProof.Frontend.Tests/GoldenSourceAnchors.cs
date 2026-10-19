using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Frontend.Test;

// Snapshot locations by token spelling and occurrence, independent of trivia.
// Keep exact boundary checks so an incorrectly widened/narrowed span still fails.
internal sealed class GoldenSourceAnchors
{
    private readonly string _document;
    private readonly ImmutableArray<SyntaxToken> _tokens;
    private readonly ImmutableDictionary<int, int> _starts;
    private readonly ImmutableDictionary<int, int> _ends;
    private readonly ImmutableDictionary<(int Start, int Length), string>.Builder _anchors = ImmutableDictionary.CreateBuilder<(int Start, int Length), string>();

    internal GoldenSourceAnchors(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _document = tree.FilePath;
        _tokens = tree.GetRoot().DescendantTokens()
            .Where(token => token.Span.Length != 0).ToImmutableArray();
        var starts = ImmutableDictionary.CreateBuilder<int, int>();
        var ends = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < _tokens.Length; index++)
        {
            starts.Add(_tokens[index].SpanStart, index);
            ends.Add(_tokens[index].Span.End, index);
        }
        _starts = starts.ToImmutable();
        _ends = ends.ToImmutable();
    }

    internal string Format(string document, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document, _document, StringComparison.Ordinal))
        {
            throw new ArgumentException("Source span belongs to a different fixture document.", nameof(document));
        }
        if (length <= 0 || !_starts.TryGetValue(start, out var first) ||
            (long)start + length > int.MaxValue ||
            !_ends.TryGetValue(start + length, out var last) || last < first)
        {
            throw new ArgumentException("Source span must cover complete fixture tokens.", nameof(start));
        }
        if (_anchors.TryGetValue((start, length), out var cached))
        {
            return cached;
        }

        var count = last - first + 1;
        var occurrence = 0;
        for (var candidate = 0; candidate <= first; candidate++)
        {
            var matches = true;
            for (var offset = 0; offset < count; offset++)
            {
                if (!string.Equals(_tokens[candidate + offset].Text,
                    _tokens[first + offset].Text, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                occurrence++;
            }
        }

        var spellings = ImmutableArray.CreateBuilder<string>(count);
        for (var offset = 0; offset < count; offset++)
        {
            spellings.Add(SymbolDisplay.FormatLiteral(_tokens[first + offset].Text, quote: true));
        }
        var anchor = document + ":tokens[" + string.Join(",", spellings.ToImmutable()) + "]#" +
            occurrence.ToString(CultureInfo.InvariantCulture);
        _anchors.Add((start, length), anchor);
        return anchor;
    }

    internal string FormatOperationDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var separator = description.LastIndexOf('@');
        if (separator < 1 || !Enum.TryParse<OperationKind>(description[..separator], out _))
        {
            return description;
        }
        // Operation descriptions encode only a starting position, not a span.
        // Preserve that precision by anchoring the token starting there.
        if (!int.TryParse(description.AsSpan(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var start) || !_starts.TryGetValue(start, out var token))
        {
            throw new ArgumentException("Operation description must identify a fixture token.", nameof(description));
        }
        return description[..(separator + 1)] + Format(_document, start, _tokens[token].Span.Length);
    }
}
