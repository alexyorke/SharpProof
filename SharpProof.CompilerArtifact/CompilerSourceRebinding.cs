using System.Text;
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

            if (!texts.TryGetValue(authority.SourceTreeOrdinal, out var text))
            {
                var tree =
                    artifact.Compilation.SyntaxTrees[authority.SourceTreeOrdinal];
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
                        WorkerClaimEvidence.ReturnAttribute or
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

    private static bool IsAttribute(string span)
    {
        return span.Length != 0 &&
            (char.IsLetter(span[0]) || span[0] is '_' or '@') &&
            (IsIdentifierPart(span[span.Length - 1]) ||
                span[span.Length - 1] == ')');
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
