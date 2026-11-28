using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SharpProof.Analyzer;

// A documented claim is one sentence of a <param> or <returns> element that
// states a validation this analyzer can compare with a declared contract.
// The grammar is closed. Any other sentence is not a claim: it is skipped and
// never reported as a mismatch, so prose the analyzer cannot read stays silent.
internal enum DocumentedClaimKind
{
    NonNull,
    Interval
}

// For Interval claims, a null bound is open on that side.
internal sealed class DocumentedClaim
{
    public DocumentedClaim(
        DocumentedClaimKind kind,
        string sentence,
        long? lower = null,
        long? upper = null)
    {
        Kind = kind;
        Sentence = RepositoryParameterGuard.NotNull(sentence, nameof(sentence));
        Lower = lower;
        Upper = upper;
    }

    public DocumentedClaimKind Kind { get; }

    public string Sentence { get; }

    public long? Lower { get; }

    public long? Upper { get; }
}

internal static class DocumentedClaimParser
{
    // Key under which the <returns> claims are stored. A parameter name can
    // never equal it, since it is not a valid C# identifier.
    internal const string ReturnKey = "<return>";

    private static readonly char[] SentenceSeparators = ['.'];

    private static readonly ImmutableHashSet<string> NonNullSentences =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "must not be null",
            "must be non-null",
            "must be non null",
            "cannot be null");

    // Positive is the integral interval [1, maximum], so it is an Interval claim.
    private static readonly ImmutableHashSet<string> PositiveSentences =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "must be positive",
            "must be greater than zero",
            "must be greater than 0");

    private static readonly Regex BetweenSentence = new(
        @"^must be between (-?\d+) and (-?\d+)( inclusive)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AtLeastSentence = new(
        @"^must be at least (-?\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AtMostSentence = new(
        @"^must be at most (-?\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Returns the recognized claims keyed by parameter name, plus the return
    // value under ReturnKey. Malformed XML yields no claims.
    internal static ImmutableDictionary<string, ImmutableArray<DocumentedClaim>> Parse(string documentationXml)
    {
        documentationXml = RepositoryParameterGuard.NotNull(documentationXml, nameof(documentationXml));
        var claims = ImmutableDictionary.CreateBuilder<string, ImmutableArray<DocumentedClaim>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(documentationXml))
        {
            return claims.ToImmutable();
        }

        XElement member;
        try
        {
            member = XElement.Parse(documentationXml);
        }
        catch (XmlException)
        {
            return claims.ToImmutable();
        }

        foreach (var parameter in member.Elements("param"))
        {
            var name = (string?)parameter.Attribute("name");
            if (name is null || name.Length == 0)
            {
                continue;
            }

            AddClaims(claims, name, Recognize(parameter));
        }

        foreach (var returns in member.Elements("returns"))
        {
            AddClaims(claims, ReturnKey, Recognize(returns));
        }

        return claims.ToImmutable();
    }

    private static void AddClaims(
        ImmutableDictionary<string, ImmutableArray<DocumentedClaim>>.Builder claims,
        string key,
        ImmutableArray<DocumentedClaim> added)
    {
        if (added.IsEmpty)
        {
            return;
        }

        claims[key] = claims.TryGetValue(key, out var existing)
            ? existing.AddRange(added)
            : added;
    }

    private static ImmutableArray<DocumentedClaim> Recognize(XElement element)
    {
        // XElement.Value flattens nested markup such as <c> and <see/> into text.
        var text = string.Join(" ", element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var claims = ImmutableArray.CreateBuilder<DocumentedClaim>();
        foreach (var fragment in text.Split(SentenceSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var sentence = fragment.Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            if (Recognize(sentence) is { } claim)
            {
                claims.Add(claim);
            }
        }

        return claims.ToImmutable();
    }

    private static DocumentedClaim? Recognize(string sentence)
    {
        if (NonNullSentences.Contains(sentence))
        {
            return new DocumentedClaim(DocumentedClaimKind.NonNull, sentence);
        }

        if (PositiveSentences.Contains(sentence))
        {
            return new DocumentedClaim(DocumentedClaimKind.Interval, sentence, lower: 1);
        }

        var between = BetweenSentence.Match(sentence);
        if (between.Success &&
            TryParseBound(between.Groups[1].Value, out var lower) &&
            TryParseBound(between.Groups[2].Value, out var upper))
        {
            return new DocumentedClaim(DocumentedClaimKind.Interval, sentence, lower, upper);
        }

        var atLeast = AtLeastSentence.Match(sentence);
        if (atLeast.Success && TryParseBound(atLeast.Groups[1].Value, out var minimum))
        {
            return new DocumentedClaim(DocumentedClaimKind.Interval, sentence, lower: minimum);
        }

        var atMost = AtMostSentence.Match(sentence);
        if (atMost.Success && TryParseBound(atMost.Groups[1].Value, out var maximum))
        {
            return new DocumentedClaim(DocumentedClaimKind.Interval, sentence, upper: maximum);
        }

        return null;
    }

    private static bool TryParseBound(string text, out long value)
    {
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
