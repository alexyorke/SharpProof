using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// The review sidecar: a claim a local or hosted model proposed from documentation is
// used only after a person accepts it, a proposal awaiting review is reported, and an
// entry whose key does not match its own parameter text is ignored. The documentation
// here is prose the claim grammar does not read, which is the only text a proposal
// can be about.
[TestFixture]
public sealed class DocumentedValidationReviewTests
{
    private const string MismatchRule = "SP0061";
    private const string ReviewRule = "SP0064";

    private const string DocumentedText = "Page number, zero through one hundred.";

    // SHA-256 of "page", a newline, and DocumentedText, computed by the extractor's
    // PowerShell rule. A match here means the two sides agree on the key.
    private const string PageKey = "81982AE61ED9937E932C81083F71821B8638CDA22E08B5FD29D493613FDE1239";

    private const string Source = """
        using System;
        public static class Fixture {
            /// <param name="page">Page number, zero through one hundred.</param>
            public static int Get(int page) {
                if (page < 0 || page > 90) throw new ArgumentOutOfRangeException(nameof(page));
                return page;
            }
        }
        """;

    [Test]
    public async Task AnAcceptedClaimIsComparedWithTheRuntimeGuards()
    {
        var diagnostics = await AnalyzeWithFile(Source, Entry("accepted", "interval", 0, 100, "zero through one hundred"));
        AnalyzerTestHost.AssertIds(diagnostics, [MismatchRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "Parameter 'page' is documented as 'zero through one hundred', but its declarations and runtime guards admit 0 through 90");
    }

    [Test]
    public async Task AProposalAwaitingReviewIsReportedAndNotChecked()
    {
        var diagnostics = await AnalyzeWithFile(Source, Entry("proposed", "interval", 0, 100, "zero through one hundred"));
        AnalyzerTestHost.AssertIds(diagnostics, [ReviewRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "Parameter 'page' has a validation claim proposed from its documentation that awaits review: 'zero through one hundred'");
    }

    [Test]
    public async Task ARejectedEntryIsIgnored()
    {
        var diagnostics = await AnalyzeWithFile(Source, Entry("rejected", "interval", 0, 100, "zero through one hundred"));
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task AnEntryWhoseKeyDoesNotMatchItsTextIsIgnored()
    {
        const string stale = """
            [{"key":"0000000000000000000000000000000000000000000000000000000000000000","param":"page","text":"Page number, zero through one hundred.","status":"accepted","kind":"interval","lower":0,"upper":100,"source_text":"zero through one hundred","reason":"","model":"reviewer"}]
            """;
        var diagnostics = await AnalyzeWithFile(Source, stale);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public void TheKeyMatchesTheExtractorsSha256Rule()
    {
        Assert.That(ClaimReviewSidecar.KeyFor("page", DocumentedText), Is.EqualTo(PageKey));
    }

    private static string Entry(string status, string kind, long lower, long upper, string sourceText)
    {
        return "[{\"key\":\"" + PageKey + "\",\"param\":\"page\",\"text\":\"" + DocumentedText + "\",\"status\":\"" + status +
            "\",\"kind\":\"" + kind + "\",\"lower\":" + lower + ",\"upper\":" + upper +
            ",\"source_text\":\"" + sourceText + "\",\"reason\":\"\",\"model\":\"reviewer\"}]";
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeWithFile(string source, string sidecarJson)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, [MismatchRule, ReviewRule]);
        var values = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            [
                KeyValuePair.Create("build_property.SharpProofProfile", "strict"),
                KeyValuePair.Create("build_property.SharpProofFeatures", "contracts")
            ]);
        return AnalyzerTestHost.AnalyzeAsync(
            compilation,
            values,
            ImmutableArray.Create<AdditionalText>(new MemoryAdditionalText("page.sharpproof-claims.json", sidecarJson)));
    }

    private sealed class MemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            return SourceText.From(text);
        }
    }
}
