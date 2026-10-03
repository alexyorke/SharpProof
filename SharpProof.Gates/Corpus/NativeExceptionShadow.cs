using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record NativeExceptionShadowRow(string MethodId, string CallableId,
    WorkerClaimOutcome LegacyOutcome, WorkerClaimReason LegacyReason,
    WorkerClaimOutcome CompilerOutcome, WorkerClaimReason CompilerReason,
    WorkerClaimOutcome NativeOutcome, WorkerClaimReason NativeReason,
    bool HasTotalBody, bool HasBodyAbstraction, string? ExceptionKind, string RuntimeOracle)
{
    public string AllocationIlOracle { get; init; } = "NotRun";
    public int RuntimeChecks { get; init; }
    public long? AllocatedBytes { get; init; }
}

internal sealed record NativeExceptionShadowReport(string UniverseSha256, int UniverseMethodCount,
    int CheckedMethodCount, bool Exhaustive, int LegacyProven, int RetainedProven,
    double RetainedPercent, bool RetentionGatePassed, int DisagreementCount,
    int RuntimeWitnesses, int RuntimeContradictions, double WallSeconds,
    ImmutableArray<NativeExceptionShadowRow> Rows,
    ImmutableDictionary<string, int> NativeUnknownReasons, bool ComparisonPassed)
{
    public string ContractKind { get; init; } = "DoesNotThrow";
    public int ReachableSourceBodyCount { get; init; }
    public int ReachableSourceMayDivergeCount { get; init; }
    public int ReachableSourceUnknownEffectCount { get; init; }
}

// This report measures the replacement; it never publishes worker authority.
internal static class NativeExceptionShadow
{
    internal static async Task<NativeExceptionShadowReport> RunAsync(string root, int maximumMethods = 0,
        bool allocations = false, bool purity = false, bool capabilities = false, CancellationToken cancellationToken = default)
    {
        var wall = Stopwatch.StartNew();
        var document = OpenSourceCorpusCatalog.Load(root);
        if (maximumMethods < 0 || maximumMethods > document.Methods.Length)
        { throw new ArgumentOutOfRangeException(nameof(maximumMethods)); }
        var universeSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            Path.Combine(root, "SharpProof.Gates", "Corpus", "oss-methods.json"), cancellationToken).ConfigureAwait(false)));
        var selected = document with
        {
            Methods = [.. document.Methods.OrderBy(method => method.Id, StringComparer.Ordinal)
                .Take(maximumMethods == 0 ? document.Methods.Length : maximumMethods)]
        };
        var report = await ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(selected, cancellationToken, allocations, purity, capabilities),
            selected.Methods.Select(method => method.Id).ToImmutableArray(), root,
            universeSha256, document.Methods.Length, allocations, purity, capabilities, cancellationToken).ConfigureAwait(false);
        return report with { WallSeconds = wall.Elapsed.TotalSeconds };
    }

    internal static async Task<NativeExceptionShadowReport> ObserveAsync(CSharpCompilation compilation,
        ImmutableArray<string> methodIds, string root, string universeSha256, int universeMethodCount,
        bool allocations = false, bool purity = false, bool capabilities = false, CancellationToken cancellationToken = default)
    {
        var wall = Stopwatch.StartNew();
        if (new[] { allocations, purity, capabilities }.Count(selected => selected) > 1)
        { throw new ArgumentException("A shadow run must select exactly one effect contract.", nameof(purity)); }
        if (methodIds.IsDefaultOrEmpty || methodIds.Any(string.IsNullOrWhiteSpace) || methodIds.Distinct(StringComparer.Ordinal).Count() != methodIds.Length ||
            methodIds.Length > universeMethodCount)
        { throw new ArgumentException("A shadow universe must have unique, nonempty method identities.", nameof(methodIds)); }
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var targets = discovery.Targets.Values.Where(target => OpenSourceCorpusRunner.CorpusMethodId(target.Declaration) != null)
            .ToDictionary(target => OpenSourceCorpusRunner.CorpusMethodId(target.Declaration)!, StringComparer.Ordinal);
        if (!targets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(methodIds))
        { throw new InvalidDataException("The declared exception probes do not equal the selected corpus universe."); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, root, "net9.0", WorkerFeatureSet.Effects,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken);
        var validatedArtifact = CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken);
        var sourceSummaries = validatedArtifact.ReachableSource is { } source
            ? EffectSummaryFixpoint.ComputeValidated(source, cancellationToken)
            : ImmutableSortedDictionary<string, SourceEffectSummary>.Empty;
        var owned = preparations.ToDictionary(preparation => preparation.Entry.CallableId, StringComparer.Ordinal);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var legacy = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, cancellationToken);
        using var oracle = new NativeExceptionWitnessOracle(compilation);
        using var allocationOracle = new NativeAllocationWitnessOracle(compilation);
        var rows = ImmutableArray.CreateBuilder<NativeExceptionShadowRow>(methodIds.Length);
        foreach (var id in methodIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[id];
            var evaluation = EffectContractDiagnostics.Evaluate(target.Method, target.Method.Locations[0], legacy,
                static _ => { }, cancellationToken, includeDiagnosticPayload: false)
                .Single(evaluation => evaluation.Kind == (capabilities ? EffectEvaluationContractKind.AllowedCapabilities
                    : purity ? EffectEvaluationContractKind.EnforcePure : allocations
                    ? EffectEvaluationContractKind.ZeroAllocations : EffectEvaluationContractKind.DoesNotThrow));
            var preparation = owned[target.Entry.CallableId];
            var claim = preparation.EffectClaims.Single(claim => claim.ContractKind == (capabilities ? WorkerEffectContractKind.AllowedCapabilities
                : purity ? WorkerEffectContractKind.EnforcePure : allocations
                ? WorkerEffectContractKind.ZeroAllocations : WorkerEffectContractKind.DoesNotThrow));
            var evidence = capabilities
                ? await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken).ConfigureAwait(false)
                : purity
                ? await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken).ConfigureAwait(false)
                : allocations
                ? await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken).ConfigureAwait(false)
                : (await NativeExceptionEffectVerifier.VerifyClaimsAsync(preparation, new WorkerBudgets(), cancellationToken).ConfigureAwait(false))[claim.ClaimId];
            var outcome = evidence.Outcome switch
            {
                ProvenOutcome => WorkerClaimOutcome.Proven,
                RefutedOutcome => WorkerClaimOutcome.Refuted,
                _ => WorkerClaimOutcome.Unknown
            };
            if (outcome == WorkerClaimOutcome.Refuted && (capabilities ? evidence.LockWitness == null
                : purity ? evidence.WriteWitness == null && evidence.LockWitness == null : allocations ? evidence.AllocationWitness == null : evidence.ExceptionWitness == null))
            { throw new InvalidDataException("A native effect refutation has no replay-validated witness."); }
            var allocationObservation = allocations && outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted
                ? allocationOracle.Check(target.Method, preparation.Total!, evidence, outcome, cancellationToken) : null;
            var runtime = capabilities ? "CapabilityOracleNotRun" : purity ? "PurityOracleNotRun" : allocations ? allocationObservation?.RuntimeOracle ?? "NotRun" : outcome == WorkerClaimOutcome.Refuted
                ? oracle.Check(target.Method, preparation.Total!, evidence.EntryModel, evidence.ExceptionWitness!, cancellationToken)
                : "NotRun";
            rows.Add(new(id, preparation.Entry.CallableId, CompilerEffectEvaluationWireMappings.ToWorker(evaluation.Outcome),
                CompilerEffectEvaluationWireMappings.ToWorker(evaluation.Reason), claim.Outcome, claim.Reason, outcome, evidence.Reason,
                preparation.Total != null, preparation.Total?.IsBodyAbstraction == true,
                evidence.ExceptionWitness?.Kind.ToString(), runtime)
            {
                AllocationIlOracle = allocationObservation?.IlOracle ?? "NotRun",
                RuntimeChecks = allocationObservation?.RuntimeChecks ?? 0,
                AllocatedBytes = allocationObservation?.AllocatedBytes
            });
        }
        return Summarize(universeSha256, universeMethodCount, rows.ToImmutable(), wall.Elapsed.TotalSeconds) with
        {
            ContractKind = capabilities ? "AllowedCapabilities" : purity ? "EnforcePure" : allocations ? "ZeroAllocations" : "DoesNotThrow",
            ReachableSourceBodyCount = sourceSummaries.Count,
            ReachableSourceMayDivergeCount = sourceSummaries.Values.Count(summary => summary.MayDiverge),
            ReachableSourceUnknownEffectCount = sourceSummaries.Values.Count(summary =>
                summary.UnknownEffects != SourceMayEffect.None || summary.UnknownExceptions)
        };
    }

    internal static NativeExceptionShadowReport Summarize(string universeSha256, int universeMethodCount,
        ImmutableArray<NativeExceptionShadowRow> rows, double wallSeconds)
    {
        if (rows.IsDefaultOrEmpty || rows.Length > universeMethodCount || rows.Any(row => string.IsNullOrWhiteSpace(row.MethodId)) ||
            rows.Select(row => row.MethodId).Distinct(StringComparer.Ordinal).Count() != rows.Length)
        { throw new ArgumentException("Shadow rows do not define a unique covered universe.", nameof(rows)); }
        var proven = rows.Count(row => row.LegacyOutcome == WorkerClaimOutcome.Proven);
        var retained = rows.Count(row => row.LegacyOutcome == WorkerClaimOutcome.Proven && row.NativeOutcome == WorkerClaimOutcome.Proven);
        var disagreements = rows.Count(row => row.LegacyOutcome == WorkerClaimOutcome.Proven && row.NativeOutcome == WorkerClaimOutcome.Refuted ||
            row.LegacyOutcome == WorkerClaimOutcome.Refuted && row.NativeOutcome == WorkerClaimOutcome.Proven);
        var runtimeContradictions = rows.Count(row => row.RuntimeOracle == "Contradiction");
        var exhaustive = rows.Length == universeMethodCount;
        return new(universeSha256, universeMethodCount, rows.Length, exhaustive, proven, retained,
            proven == 0 ? 0 : Math.Round(100d * retained / proven, 2),
            exhaustive && proven > 0 && retained * 100L >= proven * 95L,
            disagreements, rows.Count(row => row.RuntimeOracle == "Confirmed"), runtimeContradictions, wallSeconds, rows,
            rows.Where(row => row.NativeOutcome == WorkerClaimOutcome.Unknown).GroupBy(row => row.NativeReason.ToString(), StringComparer.Ordinal)
                .ToImmutableDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            disagreements == 0 && runtimeContradictions == 0);
    }
}
