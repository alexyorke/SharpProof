using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

// A reviewed contract on one pinned OSS corpus method. The companion is a
// [ContractFor] class compiled beside the unchanged source; its clauses are
// the method's contract. A type has at most one companion, so a method of a
// type that already has one names that entry in SharedWith instead. The
// expected verdict was checked against the body.
internal sealed record OssContract(string MethodId, string? Companion, string? SharedWith, WorkerClaimOutcome ExpectedVerdict,
    string Review)
{
    public string MethodId { get; init; } = MethodId ?? throw new ArgumentNullException(nameof(MethodId));
    public string Review { get; init; } = Review ?? throw new ArgumentNullException(nameof(Review));
}

internal sealed record OssContractsDocument(int SchemaVersion,
    ImmutableArray<OssContract> Contracts /* SPINT001: all values valid - Load rejects a default or empty array */);

internal sealed record OssContractRow(string MethodId, string SourceId, string MethodName, string Expected, string Actual, string Reason)
{
    public string Reason { get; init; } = Reason ?? throw new ArgumentNullException(nameof(Reason));
    public string MethodId { get; init; } = MethodId ?? throw new ArgumentNullException(nameof(MethodId));
    public string SourceId { get; init; } = SourceId ?? throw new ArgumentNullException(nameof(SourceId));
    public string MethodName { get; init; } = MethodName ?? throw new ArgumentNullException(nameof(MethodName));
    public string Expected { get; init; } = Expected ?? throw new ArgumentNullException(nameof(Expected));
    public string Actual { get; init; } = Actual ?? throw new ArgumentNullException(nameof(Actual));
}

internal sealed record OssContractGateResult(int Contracts, int Proven, int Refuted, int Unknown, bool Passed,
    ImmutableArray<OssContractRow> Mismatches)
{
    public ImmutableArray<OssContractRow> Mismatches { get; init; } =
        Mismatches.IsDefault ? throw new ArgumentNullException(nameof(Mismatches)) : Mismatches;
}

// Contracts on real code: each reviewed companion contract must get exactly
// its expected verdict when its source compiles with no effect attribute.
internal static class OssContractGate
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static OssContractsDocument Load(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var path = Path.Combine(OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot), "oss-contracts.json");
        var document = JsonSerializer.Deserialize<OssContractsDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The OSS contracts file is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Contracts.IsDefaultOrEmpty)
        { throw new InvalidDataException("The OSS contracts file has an unsupported schema or no contracts."); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in document.Contracts)
        {
            if (contract is null || !ids.Add(contract.MethodId) ||
                string.IsNullOrWhiteSpace(contract.Companion) == (contract.SharedWith is null) ||
                string.IsNullOrWhiteSpace(contract.Review) ||
                contract.ExpectedVerdict is not (WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted or WorkerClaimOutcome.Unknown))
            { throw new InvalidDataException("Each OSS contract needs a distinct method, a companion or a shared one, a review and a verdict."); }
        }
        if (document.Contracts.Any(contract => contract.SharedWith is { } shared &&
            !document.Contracts.Any(other => other.MethodId == shared && other.Companion is not null)))
        { throw new InvalidDataException("A shared OSS contract must name an entry with its own companion."); }
        return document;
    }

    internal static async Task<OssContractGateResult> RunAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var corpus = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var contracts = Load(repositoryRoot).Contracts;
        var methods = corpus.Methods.ToImmutableDictionary(static method => method.Id, StringComparer.Ordinal);
        var unknownIds = contracts.Where(contract => !methods.ContainsKey(contract.MethodId)).Select(static contract => contract.MethodId).ToImmutableArray();
        if (!unknownIds.IsEmpty)
        { throw new InvalidDataException("OSS contracts name methods outside the corpus: " + string.Join(", ", unknownIds)); }
        var rows = ImmutableArray.CreateBuilder<OssContractRow>(contracts.Length);
        foreach (var group in contracts.GroupBy(contract => methods[contract.MethodId].SourceId, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = group.Select(contract => contract.MethodId).ToImmutableHashSet(StringComparer.Ordinal);
            var source = OpenSourceCorpusRunner.SplitBySource(corpus).Single(document => document.Methods.Any(method => method.SourceId == group.Key));
            source = source with { Methods = [.. source.Methods.Where(method => selected.Contains(method.Id))] };
            var compilation = OpenSourceCorpusRunner.PrepareContracts(source, [.. group.Select(static contract => contract.Companion).OfType<string>()],
                cancellationToken, loaded: corpus);
            RejectInvalidCompanions(compilation, group.Key, cancellationToken);
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
            var verdicts = (await NativeCorpusVerifier.VerifyAsync(compilation, cancellationToken, observeResponse: response =>
                {
                    ArgumentNullException.ThrowIfNull(response);
                    var results = response.ClaimResults.ToDictionary(static result => result.ClaimId, StringComparer.Ordinal);
                    foreach (var claims in response.Manifest.Claims.GroupBy(static claim => claim.CallableId, StringComparer.Ordinal))
                    {
                        reasons[claims.Key] = string.Join(",", claims.Select(claim => results.TryGetValue(claim.ClaimId, out var result)
                            ? result.Reason.ToString() : "Missing").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                    }
                }).ConfigureAwait(false))
                .Select(verdict => (Id: OpenSourceCorpusRunner.CorpusMethodId(verdict.Declaration), verdict.Outcome,
                    Reason: Reason(reasons, verdict.Method.GetDocumentationCommentId())))
                .Where(verdict => verdict.Id is not null && selected.Contains(verdict.Id))
                .ToImmutableDictionary(static verdict => verdict.Id!, static verdict => (verdict.Outcome, verdict.Reason), StringComparer.Ordinal);
            foreach (var contract in group)
            {
                var method = methods[contract.MethodId];
                // A contract the collector never bound to the method declares no claim.
                var actual = verdicts.TryGetValue(contract.MethodId, out var verdict) ? verdict.Outcome.ToString() : "NoClaim";
                rows.Add(new(contract.MethodId, method.SourceId, method.MethodName, contract.ExpectedVerdict.ToString(), actual,
                    verdict.Reason ?? string.Empty));
            }
        }
        var all = rows.ToImmutable();
        var mismatches = all.Where(static row => row.Expected != row.Actual).ToImmutableArray();
        return new(all.Length, all.Count(static row => row.Actual == nameof(WorkerClaimOutcome.Proven)),
            all.Count(static row => row.Actual == nameof(WorkerClaimOutcome.Refuted)),
            all.Count(static row => row.Actual is not (nameof(WorkerClaimOutcome.Proven) or nameof(WorkerClaimOutcome.Refuted))),
            mismatches.IsEmpty, mismatches);
    }

    // A companion the ContractFor rules reject declares no claim, so its
    // contracts would read as Unknown or NoClaim instead of failing; a
    // reviewed companion must be valid. A reviewed companion covers only the
    // members it has contracts for, so SPCF0004 (a target member without a
    // companion member) is expected.
    private static void RejectInvalidCompanions(Compilation compilation, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        static bool IsCompanion(SyntaxTree tree)
        { return Path.GetFileName(tree.FilePath).StartsWith("__SharpProofOssContracts", StringComparison.Ordinal); }
        var errors = SharpProof.Analyzer.ContractForValidationEngine.Validate(compilation,
                SharpProof.Analyzer.ContractForValidationEngine.FindCandidates(compilation, IsCompanion, cancellationToken),
                cancellationToken, IsCompanion)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "SPCF0004")
            .Select(static diagnostic => diagnostic.ToString())
            .ToImmutableArray();
        if (!errors.IsEmpty)
        { throw new InvalidDataException("The OSS contract companions for " + sourceId + " are invalid: " + string.Join("; ", errors)); }
    }

    // A manifest callable ID is the documentation ID with a return-type suffix.
    private static string Reason(Dictionary<string, string> reasons, string? documentationId)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        return documentationId is null ? string.Empty : reasons.FirstOrDefault(pair =>
            pair.Key == documentationId || pair.Key.StartsWith(documentationId + "~", StringComparison.Ordinal)).Value ?? string.Empty;
    }
}
