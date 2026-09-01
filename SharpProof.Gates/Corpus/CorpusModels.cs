using System.Collections.Immutable;
using SharpProof.Analyzer;

namespace SharpProof.Gates.Corpus;

internal enum CorpusVerdict
{
    Proven,
    Refuted,
    Unknown,
    SilentUnknown
}

internal enum CorpusVariant
{
    Baseline,
    Rename,
    EscapedIdentifiers,
    Trivia,
    Parentheses,
    Temporary,
    IfTrue,
    NamedArguments,
    AlphaRenameContractFormals,
    ReorderIndependentStatements
}

internal enum CorpusOrigin
{
    SyntheticMetamorphic,
    OpenSource
}

internal enum CorpusSupport
{
    Unspecified,
    Supported,
    IntentionallyUnsupported
}

internal sealed record CorpusCase(
    string Id,
    string SeedId,
    CorpusVariant Variant,
    string Mode,
    CorpusVerdict SemanticExpectation,
    CorpusSupport Support,
    string Source,
    CorpusOrigin Origin = CorpusOrigin.SyntheticMetamorphic,
    string? ProvenanceId = null);

internal sealed record CorpusUnknownReasonCount(
    string Reason,
    int Count);

internal sealed record CorpusUnknownReasonRatchet(
    int MinimumSupportedCases,
    int MinimumSupportedOpenSourceMethods,
    int MaximumTotalUnknown,
    ImmutableDictionary<string, int> MaximumByReason);

internal sealed record CorpusObservation(
    string CaseId,
    CorpusVerdict Verdict,
    AnalyzerSemanticOutcome SemanticOutcome,
    ImmutableArray<string> Diagnostics)
{
    public string ToCanonicalLine()
    {
        return $"{CaseId}|{Verdict}|{SemanticOutcome}|{string.Join(",", Diagnostics)}";
    }
}

internal sealed record CorpusSeed(
    string Id,
    string Mode,
    CorpusVerdict ExpectedVerdict,
    CorpusSupport Support,
    string Attributes,
    string Body,
    string AdditionalMembers);

internal sealed record ProvenToUnknownAllowance(
    string CaseId,
    string Reason);

internal sealed record OpenSourceCorpusDocument(
    int SchemaVersion,
    ImmutableArray<OpenSourceCorpusSource> Sources,
    ImmutableArray<OpenSourceCorpusFile> Files,
    ImmutableArray<OpenSourceCorpusMethod> Methods);

internal sealed record OpenSourceCorpusSource(
    string Id,
    string Repository,
    string Commit,
    string LicenseSpdx,
    string LicenseFile,
    string LicenseSha256);

internal sealed record OpenSourceCorpusFile(
    string SourceId,
    string Path,
    string ContentSha256,
    string Content);

internal sealed record OpenSourceCorpusMethod(
    string Id,
    string SourceId,
    string Path,
    int StartLine,
    int EndLine,
    string DeclarationSha256,
    string MethodName,
    string Mode,
    CorpusVerdict ExpectedVerdict,
    CorpusSupport Support);
