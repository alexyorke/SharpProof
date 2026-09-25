using System.Text;
using System.Text.RegularExpressions;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// The worker has no source text, so hydration can only check that a location
// authority's span has valid geometry and plausible structure; a resealed
// artifact can still point a claim at another span inside its callable.  The
// launcher runs beside the build's sources: re-read each owning tree, require
// the captured content, and require every owner span to have the syntactic
// shape the collector produces for that owner.  Trees with no file on disk
// (in-memory source-generator output) cannot be reread and keep only the
// hydration checks.
internal static class CompilerSourceRebinding
{
    private const string EnsuresMethodName = "Ensures";

    internal static void Validate(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));
        var evidence = new Dictionary<string, WorkerClaimEvidence>(
            StringComparer.Ordinal);
        foreach (var claim in artifact.Manifest.Claims)
        {
            evidence[claim.ClaimId] = claim.Evidence;
        }

        var texts = new Dictionary<int, string?>();
        foreach (var authority in artifact.LocationAuthorities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CompilerSourceLocationAuthority.IsNone(authority.Location))
            {
                continue;
            }

            var tree = artifact.Compilation.SyntaxTrees[
                authority.SourceTreeOrdinal];
            if (!texts.TryGetValue(authority.SourceTreeOrdinal, out var text))
            {
                text = File.Exists(tree.Path)
                    ? ReadTree(tree, cancellationToken)
                    : null;
                texts.Add(authority.SourceTreeOrdinal, text);
            }

            if (text == null)
            {
                continue;
            }

            var span = text.Substring(
                authority.Location.Start,
                authority.Location.Length);
            var bound = authority.OwnerKind ==
                CompilerSourceLocationOwnerKind.Callable
                // A callable without a source declaration borrows its first
                // postcondition's location.
                ? IsDeclaration(span) || IsEnsuresInvocation(span)
                : evidence.TryGetValue(authority.OwnerId, out var kind) &&
                    kind switch
                    {
                        WorkerClaimEvidence.DirectClause or
                        WorkerClaimEvidence.CompanionClause =>
                            IsEnsuresInvocation(span),
                        WorkerClaimEvidence.ReturnAttribute =>
                            IsAttribute(
                                span,
                                text,
                                authority.Location,
                                tree),
                        WorkerClaimEvidence.Attribute => IsAttribute(span),
                        _ => false
                    };
            if (!bound)
            {
                throw new InvalidDataException(
                    "A compiler source location is not bound to its owner's syntax.");
            }
        }
    }

    private static string ReadTree(
        CompilerSyntaxTreeSnapshot tree,
        CancellationToken cancellationToken)
    {
        // Inspect the entry before opening it so a FIFO cannot block the read.
        var info = new FileInfo(tree.Path);
        var length = info.Length;
        if (length > 4L * tree.TextLength + 4)
        {
            throw new InvalidDataException(
                "A compiler source tree no longer matches its captured content.");
        }

        byte[] bytes;
        using (var stream = new FileStream(
                   tree.Path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            if (stream.Length != length)
            {
                throw new InvalidDataException(
                    "A compiler source tree changed while it was opened.");
            }

            bytes = CompilerManifestArtifactFile.ReadExact(
                stream,
                checked((int)length),
                "A compiler source tree changed while it was read.",
                cancellationToken);
        }

        var text = Decode(bytes, tree.Encoding);
        if (text.Length != tree.TextLength ||
            !string.Equals(
                WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(text)),
                tree.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A compiler source tree no longer matches its captured content.");
        }

        return text;
    }

    // Mirror Roslyn's file decoding: a byte-order mark wins, otherwise the
    // encoding the compiler recorded (strict UTF-8 when none was recorded).
    private static string Decode(byte[] bytes, string encodingName)
    {
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00) || HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            return new UTF32Encoding(bytes[0] == 0, false, true).GetString(
                bytes, 4, bytes.Length - 4);
        }
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return new UTF8Encoding(false, true).GetString(
                bytes, 3, bytes.Length - 3);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return new UnicodeEncoding(false, false, true).GetString(
                bytes, 2, bytes.Length - 2);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return new UnicodeEncoding(true, false, true).GetString(
                bytes, 2, bytes.Length - 2);
        }

        var encoding = string.IsNullOrEmpty(encodingName) ||
            string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(false, true)
            : Encoding.GetEncoding(encodingName);
        return encoding.GetString(bytes);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEnsuresInvocation(string span)
    {
        if (span.Length == 0 || span[span.Length - 1] != ')')
        {
            return false;
        }
        var identifier = new StringBuilder();
        for (var index = 0; index < span.Length; index++)
        {
            var character = span[index];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }
            if (character == '/' && index + 1 < span.Length)
            {
                var next = span[index + 1];
                var end = next == '*'
                    ? span.IndexOf("*/", index + 2, StringComparison.Ordinal)
                    : next == '/' ? span.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029'], index + 2) : -1;
                if (end < 0)
                {
                    return false;
                }
                index = next == '*' ? end + 1 : end;
                continue;
            }
            if (character == '(')
            {
                return identifier.ToString() == EnsuresMethodName;
            }
            if (character is '.' or ':')
            {
                identifier.Clear();
                continue;
            }
            if (character == '@' && identifier.Length == 0)
            {
                continue;
            }
            if (character == '\\' && index + 1 < span.Length)
            {
                var digits = span[index + 1] == 'u' ? 4 : span[index + 1] == 'U' ? 8 : 0;
                if (digits == 0 || index + 2 + digits > span.Length ||
                    !uint.TryParse(span.Substring(index + 2, digits),
                        System.Globalization.NumberStyles.AllowHexSpecifier,
                        System.Globalization.CultureInfo.InvariantCulture, out var code) || code > char.MaxValue)
                {
                    return false;
                }
                character = (char)code;
                index += digits + 1;
            }
            var category = char.GetUnicodeCategory(character);
            if (category == System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }
            if (!IsIdentifierPart(character) && category is not (
                    System.Globalization.UnicodeCategory.NonSpacingMark or
                    System.Globalization.UnicodeCategory.SpacingCombiningMark or
                    System.Globalization.UnicodeCategory.ConnectorPunctuation or
                    System.Globalization.UnicodeCategory.LetterNumber))
            {
                return false;
            }
            identifier.Append(character);
        }
        return false;
    }

    private static bool IsAttribute(
        string span,
        string? source = null,
        WorkerSourceLocation? location = null,
        CompilerSyntaxTreeSnapshot? tree = null)
    {
        var validShape = span.Length != 0 &&
            (char.IsLetter(span[0]) || span[0] is '_' or '@') &&
            (IsIdentifierPart(span[span.Length - 1]) ||
                span[span.Length - 1] == ')');
        if (!validShape)
        {
            return false;
        }
        if (source == null)
        {
            return location == null;
        }
        if (location == null)
        {
            return false;
        }

        var locationStart = location.Start;
        if (locationStart < 0 || location.Length <= 0 ||
            location.Length > source.Length - locationStart ||
            !source.AsSpan(locationStart, location.Length)
                .SequenceEqual(span.AsSpan()))
        {
            return false;
        }
        var locationEnd = locationStart + location.Length;

        const string nonCodePattern =
            @"(?<raw>""{3,})[\s\S]*?\k<raw>|" +
            @"//[^\r\n\u0085\u2028\u2029]*|/\*[\s\S]*?\*/|" +
            @"(?:\$@|@\$|@|\$)?""(?:""""|\\.|[^""\\])*""|" +
            @"'(?:\\.|[^'\\])*'";
        const string attributeName =
            @"@?[\p{L}_][\p{L}\p{Nd}_]*(?:\s*(?:\.|::)\s*@?[\p{L}_][\p{L}\p{Nd}_]*)*";
        const string arguments =
            @"\((?:[^()]|(?<nested>\()|(?<-nested>\)))*(?(nested)(?!))\)";
        var attribute = attributeName + @"(?:\s*" + arguments + @")?";
        var precedingAttributes =
            @"\[\s*return\b\s*:\s*(?:" + attribute + @"\s*,\s*)*$";
        var followingAttribute =
            @"^\s*(?:" + arguments + @")?\s*(?:,|\])";
        const RegexOptions options =
            RegexOptions.CultureInvariant | RegexOptions.Singleline;
        var timeout = TimeSpan.FromSeconds(1);

        try
        {
            var maskedRanges = new List<(int Start, int End)>();
            var code = Regex.Replace(
                source,
                nonCodePattern,
                match =>
                {
                    maskedRanges.Add((
                        match.Index,
                        match.Index + match.Length));
                    return new string(' ', match.Length);
                },
                options,
                timeout);
            return tree != null &&
                IsActiveSourceSpan(
                    source,
                    code,
                    maskedRanges,
                    locationStart,
                    locationEnd,
                    tree) &&
                Regex.IsMatch(
                    code.Substring(0, locationStart),
                    precedingAttributes,
                    options,
                    timeout) &&
                Regex.IsMatch(
                    code.Substring(locationEnd),
                    followingAttribute,
                    options,
                    timeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsActiveSourceSpan(
        string source,
        string code,
        List<(int Start, int End)> maskedRanges,
        int locationStart,
        int locationEnd,
        CompilerSyntaxTreeSnapshot tree)
    {
        if (tree.PreprocessorSymbols is null ||
            tree.EffectivePreprocessorSymbols is null)
        {
            return false;
        }

        var symbols = new HashSet<string>(
            tree.PreprocessorSymbols,
            StringComparer.Ordinal);
        var conditions = new List<(
            bool ParentActive,
            bool BranchTaken,
            bool SeenElse)>();
        var active = true;
        var foundActiveSource = false;
        var activeSegmentStart = 0;
        var lineStart = 0;
        while (lineStart < source.Length && lineStart < locationEnd)
        {
            var lineEnd = lineStart;
            while (lineEnd < source.Length &&
                source[lineEnd] is not (
                    '\r' or '\n' or '\u0085' or '\u2028' or '\u2029'))
            {
                lineEnd++;
            }

            var nextLine = lineEnd;
            if (nextLine < source.Length)
            {
                var firstTerminator = source[nextLine++];
                if (firstTerminator == '\r' && nextLine < source.Length &&
                    source[nextLine] == '\n')
                {
                    nextLine++;
                }
            }

            if (nextLine == lineStart)
            {
                return false;
            }

            var rawLine = source.Substring(lineStart, lineEnd - lineStart);
            var codeLine = code.Substring(lineStart, lineEnd - lineStart);
            var hasRawDirective = TryGetDirective(
                rawLine,
                out var rawName,
                out _);
            var hasCodeDirective = TryGetDirective(
                codeLine,
                out var codeName,
                out var body);

            if (active && hasRawDirective && !hasCodeDirective &&
                IsControlDirective(rawName))
            {
                var hashOffset = rawLine.IndexOf('#');
                if (hashOffset < 0 || !IsMaskedAt(
                    maskedRanges,
                    lineStart + hashOffset,
                    activeSegmentStart))
                {
                    // A directive-shaped line hidden outside a lexically
                    // masked active-source span is ambiguous. It may be real
                    // text masked across a disabled branch.
                    return false;
                }
            }

            var isDirective = active
                ? hasCodeDirective
                : hasRawDirective;
            var directiveName = active ? codeName : rawName;
            var targetOverlapsLine = locationStart < nextLine &&
                locationEnd > lineStart;
            if (targetOverlapsLine)
            {
                if (isDirective || !active)
                {
                    return false;
                }

                foundActiveSource = true;
            }

            var wasActive = active;
            if (isDirective &&
                !ApplyDirective(
                    directiveName,
                    body,
                    wasActive,
                    symbols,
                    conditions,
                    ref active))
            {
                return false;
            }

            if (isDirective && IsControlDirective(directiveName))
            {
                activeSegmentStart = nextLine;
            }

            lineStart = nextLine;
        }

        return foundActiveSource &&
            symbols.SetEquals(tree.EffectivePreprocessorSymbols);
    }

    private static bool ApplyDirective(
        string name,
        string body,
        bool wasActive,
        HashSet<string> symbols,
        List<(bool ParentActive, bool BranchTaken, bool SeenElse)> conditions,
        ref bool active)
    {
        switch (name)
        {
            case "if":
                {
                    var condition = false;
                    if (wasActive && !TryEvaluatePreprocessorExpression(
                        body,
                        symbols,
                        out condition))
                    {
                        return false;
                    }

                conditions.Add((wasActive, condition, SeenElse: false));
                    active = wasActive && condition;
                    return true;
                }
            case "elif":
                {
                    if (conditions.Count == 0)
                    {
                        return false;
                    }

                    var index = conditions.Count - 1;
                    var condition = conditions[index];
                    if (condition.SeenElse)
                    {
                        return false;
                    }

                    var selected = false;
                    if (condition.ParentActive && !condition.BranchTaken &&
                        !TryEvaluatePreprocessorExpression(
                            body,
                            symbols,
                            out selected))
                    {
                        return false;
                    }

                    condition.BranchTaken |= selected;
                    conditions[index] = condition;
                    active = condition.ParentActive && selected;
                    return true;
                }
            case "else":
                {
                    if (conditions.Count == 0)
                    {
                        return false;
                    }

                    var index = conditions.Count - 1;
                    var condition = conditions[index];
                    if (condition.SeenElse)
                    {
                        return false;
                    }

                    active = condition.ParentActive &&
                        !condition.BranchTaken;
                    condition.BranchTaken = true;
                    condition.SeenElse = true;
                    conditions[index] = condition;
                    return true;
                }
            case "endif":
                if (conditions.Count == 0)
                {
                    return false;
                }

                active = conditions[conditions.Count - 1].ParentActive;
                conditions.RemoveAt(conditions.Count - 1);
                return true;
            case "define":
                return !wasActive || TryUpdateSymbol(body, symbols, add: true);
            case "undef":
                return !wasActive || TryUpdateSymbol(body, symbols, add: false);
            case "region":
            case "endregion":
            case "error":
            case "warning":
            case "line":
            case "pragma":
            case "nullable":
            case "r":
            case "load":
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetDirective(
        string line,
        out string name,
        out string body)
    {
        name = string.Empty;
        body = string.Empty;
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        if (index == line.Length || line[index++] != '#')
        {
            return false;
        }

        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        var nameStart = index;
        while (index < line.Length &&
            (char.IsLetter(line[index]) || line[index] == '_'))
        {
            index++;
        }

        if (index == nameStart)
        {
            return false;
        }

        name = line.Substring(nameStart, index - nameStart);
        body = line.Substring(index).Trim();
        return true;
    }

    private static bool IsControlDirective(string name)
    {
        return name is "if" or "elif" or "else" or "endif" or
            "define" or "undef";
    }

    private static bool IsMaskedAt(
        List<(int Start, int End)> maskedRanges,
        int position,
        int activeSegmentStart)
    {
        foreach (var (start, end) in maskedRanges)
        {
            if (start > position)
            {
                return false;
            }
            if (position < end)
            {
                return start >= activeSegmentStart;
            }
        }

        return false;
    }

    private static bool TryUpdateSymbol(
        string body,
        HashSet<string> symbols,
        bool add)
    {
        if (!TryReadPreprocessorIdentifier(body, out var symbol, out var end))
        {
            return false;
        }

        while (end < body.Length && char.IsWhiteSpace(body[end]))
        {
            end++;
        }
        if (end != body.Length)
        {
            return false;
        }

        if (add)
        {
            symbols.Add(symbol);
        }
        else
        {
            symbols.Remove(symbol);
        }

        return true;
    }

    private static bool TryReadPreprocessorIdentifier(
        string source,
        out string identifier,
        out int end)
    {
        identifier = string.Empty;
        end = 0;
        while (end < source.Length && char.IsWhiteSpace(source[end]))
        {
            end++;
        }

        var start = end;
        if (end >= source.Length ||
            !(char.IsLetter(source[end]) || source[end] == '_'))
        {
            return false;
        }

        end++;
        while (end < source.Length &&
            (char.IsLetterOrDigit(source[end]) || source[end] == '_'))
        {
            end++;
        }

        identifier = source.Substring(start, end - start);
        return true;
    }

    private static bool TryEvaluatePreprocessorExpression(
        string expression,
        HashSet<string> symbols,
        out bool value)
    {
        var parser = new PreprocessorExpressionParser(expression, symbols);
        return parser.TryParse(out value);
    }

    private struct PreprocessorCondition
    {
        internal PreprocessorCondition(
            bool parentActive,
            bool branchTaken,
            bool seenElse)
        {
            ParentActive = parentActive;
            BranchTaken = branchTaken;
            SeenElse = seenElse;
        }

        internal bool ParentActive { get; }
        internal bool BranchTaken { get; set; }
        internal bool SeenElse { get; set; }
    }

    private readonly struct MaskedSourceRange
    {
        internal MaskedSourceRange(int start, int length)
        {
            Start = start;
            End = start + length;
        }

        internal int Start { get; }
        internal int End { get; }
    }

    private ref struct PreprocessorExpressionParser
    {
        private readonly ReadOnlySpan<char> _expression;
        private readonly HashSet<string> _symbols;
        private int _position;
        private int _depth;

        internal PreprocessorExpressionParser(
            string expression,
            HashSet<string> symbols)
        {
            _expression = expression.AsSpan();
            _symbols = symbols;
            _position = 0;
            _depth = 0;
        }

        internal bool TryParse(out bool value)
        {
            value = false;
            if (!ParseOr(out value))
            {
                return false;
            }

            SkipWhitespace();
            return _position == _expression.Length;
        }

        private bool ParseOr(out bool value)
        {
            if (!ParseAnd(out value))
            {
                return false;
            }

            while (TryConsume("||"))
            {
                if (!ParseAnd(out var right))
                {
                    return false;
                }
                value |= right;
            }
            return true;
        }

        private bool ParseAnd(out bool value)
        {
            if (!ParseEquality(out value))
            {
                return false;
            }

            while (TryConsume("&&"))
            {
                if (!ParseEquality(out var right))
                {
                    return false;
                }
                value &= right;
            }
            return true;
        }

        private bool ParseEquality(out bool value)
        {
            if (!ParseUnary(out value))
            {
                return false;
            }

            while (true)
            {
                if (TryConsume("=="))
                {
                    if (!ParseUnary(out var right))
                    {
                        return false;
                    }
                    value = value == right;
                }
                else if (TryConsume("!="))
                {
                    if (!ParseUnary(out var right))
                    {
                        return false;
                    }
                    value = value != right;
                }
                else
                {
                    return true;
                }
            }
        }

        private bool ParseUnary(out bool value)
        {
            SkipWhitespace();
            if (++_depth > 64)
            {
                value = false;
                return false;
            }

            try
            {
                if (TryConsume("!"))
                {
                    if (!ParseUnary(out value))
                    {
                        return false;
                    }
                    value = !value;
                    return true;
                }

                if (TryConsume("("))
                {
                    if (!ParseOr(out value) || !TryConsume(")"))
                    {
                        return false;
                    }
                    return true;
                }

                if (!TryReadPreprocessorIdentifier(
                    _expression,
                    ref _position,
                    out var identifier))
                {
                    value = false;
                    return false;
                }

                value = identifier switch
                {
                    "true" => true,
                    "false" => false,
                    _ => _symbols.Contains(identifier)
                };
                return true;
            }
            finally
            {
                _depth--;
            }
        }

        private bool TryConsume(string token)
        {
            SkipWhitespace();
            if (_expression.Slice(_position).StartsWith(
                token.AsSpan(),
                StringComparison.Ordinal))
            {
                _position += token.Length;
                return true;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_position < _expression.Length &&
                char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }

        private static bool TryReadPreprocessorIdentifier(
            ReadOnlySpan<char> source,
            ref int position,
            out string identifier)
        {
            identifier = string.Empty;
            if (position >= source.Length ||
                !(char.IsLetter(source[position]) || source[position] == '_'))
            {
                return false;
            }

            var start = position++;
            while (position < source.Length &&
                (char.IsLetterOrDigit(source[position]) ||
                 source[position] == '_'))
            {
                position++;
            }

            identifier = source.Slice(start, position - start).ToString();
            return true;
        }
    }

    private static bool IsDeclaration(string span)
    {
        return span.Length != 0 && span[span.Length - 1] is '}' or ';';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
