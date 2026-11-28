using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Analyzer;

// The review status a person gave a claim a local model proposed. Only Accepted
// claims become claims of their parameter. Proposed and Ambiguous entries are
// reported as awaiting review, and Rejected entries are ignored.
internal enum ReviewStatus
{
    Proposed,
    Accepted,
    Rejected,
    Ambiguous
}

// One review entry: the parameter, the normalized documentation text it was
// proposed from, and the claim, which exists only for an accepted interval or
// non-null entry.
internal sealed class ReviewedEntry
{
    public ReviewedEntry(ReviewStatus status, string sourceText, string reason, DocumentedClaim? claim)
    {
        Status = status;
        SourceText = RepositoryParameterGuard.NotNull(sourceText, nameof(sourceText));
        Reason = RepositoryParameterGuard.NotNull(reason, nameof(reason));
        Claim = claim;
    }

    public ReviewStatus Status { get; }

    public string SourceText { get; }

    public string Reason { get; }

    public DocumentedClaim? Claim { get; }
}

// Reads the review sidecars among a build's additional files. A sidecar is a JSON
// array of entries written by the extractor and reviewed by a person. An entry is
// used only when its key equals the SHA-256 of its own parameter and text, so a
// stale or hand-edited entry is ignored instead of applied to other text. Keys are
// computed the same way by the extractor, so the two sides agree on which
// documentation text a claim belongs to.
internal sealed class ClaimReviewSidecar
{
    internal const string FileSuffix = ".sharpproof-claims.json";

    private readonly ImmutableDictionary<string, ImmutableArray<ReviewedEntry>> _entries;

    private ClaimReviewSidecar(ImmutableDictionary<string, ImmutableArray<ReviewedEntry>> entries)
    {
        _entries = RepositoryParameterGuard.NotNull(entries, nameof(entries));
    }

    internal static ClaimReviewSidecar Empty { get; } =
        new(ImmutableDictionary<string, ImmutableArray<ReviewedEntry>>.Empty.WithComparers(StringComparer.Ordinal));

    internal bool IsEmpty => _entries.IsEmpty;

    internal static ClaimReviewSidecar Read(ImmutableArray<AdditionalText> files, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(files.IsDefault, nameof(files), "Array must be initialized.");
        var entries = ImmutableDictionary.CreateBuilder<string, ImmutableArray<ReviewedEntry>>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(static file => file.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.Path.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase) ||
                file.GetText(cancellationToken)?.ToString() is not { } text ||
                !JsonDocumentParser.TryParse(text, out var root) ||
                root is not List<object?> items)
            {
                continue;
            }

            foreach (var item in items)
            {
                if (ReadEntry(item) is not { } entry)
                {
                    continue;
                }

                var key = KeyFor(entry.Parameter, entry.Text);
                entries[key] = entries.TryGetValue(key, out var existing)
                    ? existing.Add(entry.Reviewed)
                    : ImmutableArray.Create(entry.Reviewed);
            }
        }

        return new ClaimReviewSidecar(entries.ToImmutable());
    }

    internal ImmutableArray<ReviewedEntry> For(string parameter, string text)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        text = RepositoryParameterGuard.NotNull(text, nameof(text));
        return _entries.TryGetValue(KeyFor(parameter, text), out var found)
            ? found
            : ImmutableArray<ReviewedEntry>.Empty;
    }

    // The SHA-256 of the parameter name, a newline, and the normalized documentation
    // text, as lowercase hexadecimal. The extractor computes the same value.
    internal static string KeyFor(string parameter, string text)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        text = RepositoryParameterGuard.NotNull(text, nameof(text));
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(parameter + "\n" + text));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
    }

    private sealed class ParsedEntry
    {
        public ParsedEntry(string parameter, string text, ReviewedEntry reviewed)
        {
            parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
            text = RepositoryParameterGuard.NotNull(text, nameof(text));
            reviewed = RepositoryParameterGuard.NotNull(reviewed, nameof(reviewed));
            Parameter = parameter;
            Text = text;
            Reviewed = reviewed;
        }

        public string Parameter { get; }

        public string Text { get; }

        public ReviewedEntry Reviewed { get; }
    }

    private static ParsedEntry? ReadEntry(object? item)
    {
        if (item is not Dictionary<string, object?> fields ||
            ReadString(fields, "key") is not { } key ||
            ReadString(fields, "param") is not { } parameter ||
            ReadString(fields, "text") is not { } text ||
            ReadString(fields, "status") is not { } statusText ||
            ReadStatus(statusText) is not { } status ||
            !string.Equals(KeyFor(parameter, text), key, StringComparison.Ordinal))
        {
            return null;
        }

        var sourceText = ReadString(fields, "source_text") ?? string.Empty;
        var reason = ReadString(fields, "reason") ?? string.Empty;
        return new ParsedEntry(parameter, text, new ReviewedEntry(status, sourceText, reason, ReadClaim(fields, status, sourceText)));
    }

    private static DocumentedClaim? ReadClaim(Dictionary<string, object?> fields, ReviewStatus status, string sourceText)
    {
        if (status != ReviewStatus.Accepted)
        {
            return null;
        }

        var kind = ReadString(fields, "kind");
        var lower = ReadBound(fields, "lower");
        var upper = ReadBound(fields, "upper");
        if (kind == "nonNull" && lower is null && upper is null)
        {
            return new DocumentedClaim(DocumentedClaimKind.NonNull, sourceText);
        }

        return kind == "interval" && (lower is not null || upper is not null) && (lower is null || upper is null || lower <= upper)
            ? new DocumentedClaim(DocumentedClaimKind.Interval, sourceText, lower, upper)
            : null;
    }

    private static ReviewStatus? ReadStatus(string text)
    {
        return text switch
        {
            "proposed" => ReviewStatus.Proposed,
            "accepted" => ReviewStatus.Accepted,
            "rejected" => ReviewStatus.Rejected,
            "ambiguous" => ReviewStatus.Ambiguous,
            _ => null
        };
    }

    private static string? ReadString(Dictionary<string, object?> fields, string name)
    {
        return fields.TryGetValue(name, out var value) && value is string text ? text : null;
    }

    // A JSON null bound is absent. A number must be an integer that fits in long.
    private static long? ReadBound(Dictionary<string, object?> fields, string name)
    {
        return fields.TryGetValue(name, out var value) &&
            value is JsonNumber number &&
            long.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bound)
                ? bound
                : null;
    }
}
