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
    string? ProvenanceId = null)
{
    public string Id { get; init; } = Id ?? throw new System.ArgumentNullException(nameof(Id));
    public string SeedId { get; init; } = SeedId ?? throw new System.ArgumentNullException(nameof(SeedId));
    public string Mode { get; init; } = Mode ?? throw new System.ArgumentNullException(nameof(Mode));
    public string Source { get; init; } = Source ?? throw new System.ArgumentNullException(nameof(Source));
}

internal sealed record CorpusUnknownReasonCount(
    string Reason,
    int Count)
{
    public string Reason { get; init; } = Reason ?? throw new System.ArgumentNullException(nameof(Reason));
}

internal sealed record CorpusUnknownReasonRatchet(
    int MinimumSupportedCases,
    int MinimumSupportedOpenSourceMethods,
    int MaximumTotalUnknown,
    ImmutableDictionary<string, int> MaximumByReason)
{
    public ImmutableDictionary<string, int> MaximumByReason { get; init; } = MaximumByReason ?? throw new System.ArgumentNullException(nameof(MaximumByReason));
}

internal sealed record CorpusObservation(
    string CaseId,
    CorpusVerdict Verdict,
    AnalyzerSemanticOutcome SemanticOutcome,
    ImmutableArray<string> Diagnostics)
{
    // The worker's verdict, folded into Verdict; replays reuse it so they
    // compare only the analyzer.
    internal SharpProof.Worker.Protocol.WorkerClaimOutcome? NativeOutcome { get; init; }

    public string ToCanonicalLine()
    {
        return $"{CaseId}|{Verdict}|{SemanticOutcome}|{string.Join(",", Diagnostics)}";
    }
    public string CaseId { get; init; } = CaseId ?? throw new System.ArgumentNullException(nameof(CaseId));
    public ImmutableArray<string> Diagnostics { get; init; } = Diagnostics.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Diagnostics)) : Diagnostics;
}

internal sealed record CorpusSeed(
    string Id,
    string Mode,
    CorpusVerdict ExpectedVerdict,
    CorpusSupport Support,
    string Attributes,
    string Body,
    string AdditionalMembers)
{
    public string Id { get; init; } = Id ?? throw new System.ArgumentNullException(nameof(Id));
    public string Mode { get; init; } = Mode ?? throw new System.ArgumentNullException(nameof(Mode));
    public string Attributes { get; init; } = Attributes ?? throw new System.ArgumentNullException(nameof(Attributes));
    public string Body { get; init; } = Body ?? throw new System.ArgumentNullException(nameof(Body));
    public string AdditionalMembers { get; init; } = AdditionalMembers ?? throw new System.ArgumentNullException(nameof(AdditionalMembers));
}

internal sealed record ProvenToUnknownAllowance(
    string CaseId,
    string Reason)
{
    public string CaseId { get; init; } = CaseId ?? throw new System.ArgumentNullException(nameof(CaseId));
    public string Reason { get; init; } = Reason ?? throw new System.ArgumentNullException(nameof(Reason));
}

internal sealed record OpenSourceCorpusDocument(
    int SchemaVersion,
    ImmutableArray<OpenSourceCorpusSource> /* SPINT001: all values valid - raw corpus collections are validated by preflight */ Sources,
    ImmutableArray<OpenSourceCorpusFile> /* SPINT001: all values valid - raw corpus collections are validated by preflight */ Files,
    ImmutableArray<OpenSourceCorpusMethod> /* SPINT001: all values valid - raw corpus collections are validated by preflight */ Methods);

internal sealed record OpenSourceCorpusSource(
    string Id,
    string Repository,
    string Commit,
    string LicenseSpdx,
    string LicenseFile,
    string LicenseSha256)
{
    public string Id { get; init; } = Id ?? throw new System.ArgumentNullException(nameof(Id));
    public string Repository { get; init; } = Repository ?? throw new System.ArgumentNullException(nameof(Repository));
    public string Commit { get; init; } = Commit ?? throw new System.ArgumentNullException(nameof(Commit));
    public string LicenseSpdx { get; init; } = LicenseSpdx ?? throw new System.ArgumentNullException(nameof(LicenseSpdx));
    public string LicenseFile { get; init; } = LicenseFile ?? throw new System.ArgumentNullException(nameof(LicenseFile));
    public string LicenseSha256 { get; init; } = LicenseSha256 ?? throw new System.ArgumentNullException(nameof(LicenseSha256));
}

internal sealed record OpenSourceCorpusFile(
    string SourceId,
    string Path,
    string ContentSha256,
    string /* SPINT001: all values valid - raw corpus content is checked during preflight */ Content)
{
    public string SourceId { get; init; } = SourceId ?? throw new System.ArgumentNullException(nameof(SourceId));
    public string Path { get; init; } = Path ?? throw new System.ArgumentNullException(nameof(Path));
    public string ContentSha256 { get; init; } = ContentSha256 ?? throw new System.ArgumentNullException(nameof(ContentSha256));
    public string Content { get; init; } = Content;
}

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
    CorpusSupport Support)
{
    public string Id { get; init; } = Id ?? throw new System.ArgumentNullException(nameof(Id));
    public string SourceId { get; init; } = SourceId ?? throw new System.ArgumentNullException(nameof(SourceId));
    public string Path { get; init; } = Path ?? throw new System.ArgumentNullException(nameof(Path));
    public string DeclarationSha256 { get; init; } = DeclarationSha256 ?? throw new System.ArgumentNullException(nameof(DeclarationSha256));
    public string MethodName { get; init; } = MethodName ?? throw new System.ArgumentNullException(nameof(MethodName));
    public string Mode { get; init; } = Mode ?? throw new System.ArgumentNullException(nameof(Mode));
}
