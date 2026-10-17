using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record NativeEffectOracleRow(string MethodId, string CallableId,
    WorkerClaimOutcome NativeOutcome, WorkerClaimReason NativeReason,
    bool HasTotalBody, bool HasBodyAbstraction, string? ExceptionKind, string RuntimeOracle)
{
    public string AllocationIlOracle { get; init; } = "NotRun";
    public int RuntimeChecks { get; init; }
    public long? AllocatedBytes { get; init; }
    public string MethodId { get; init; } = MethodId ?? throw new System.ArgumentNullException(nameof(MethodId));
    public string CallableId { get; init; } = CallableId ?? throw new System.ArgumentNullException(nameof(CallableId));
    public string RuntimeOracle { get; init; } = RuntimeOracle ?? throw new System.ArgumentNullException(nameof(RuntimeOracle));
}

internal sealed record NativeEffectOracleReport(string UniverseSha256, int UniverseMethodCount,
    int CheckedMethodCount, bool Exhaustive, int NativeProven, int NativeRefuted,
    int RuntimeWitnesses, int RuntimeContradictions, double WallSeconds,
    ImmutableArray<NativeEffectOracleRow> Rows,
    ImmutableDictionary<string, int> NativeUnknownReasons, bool Passed)
{
    public string ContractKind { get; init; } = "DoesNotThrow";
    public int ReachableSourceBodyCount { get; init; }
    public int ReachableSourceMayDivergeCount { get; init; }
    public int ReachableSourceUnknownEffectCount { get; init; }
    public string UniverseSha256 { get; init; } = UniverseSha256 ?? throw new System.ArgumentNullException(nameof(UniverseSha256));
    public ImmutableDictionary<string, int> NativeUnknownReasons { get; init; } = NativeUnknownReasons ?? throw new System.ArgumentNullException(nameof(NativeUnknownReasons));
    public ImmutableArray<NativeEffectOracleRow> Rows { get; init; } = Rows.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Rows)) : Rows;
}

// Checks the worker's effect verdicts on the pinned open-source methods
// against independent oracles: compiled IL, and the compiled method run on
// each refutation's witness. It never publishes worker authority.
internal static class NativeEffectOracleGate
{
    internal static async Task<NativeEffectOracleReport> RunAsync(string root, CancellationToken cancellationToken, int maximumMethods = 0,
        bool allocations = false, bool purity = false, bool capabilities = false, bool summary = false)
    {
        var wall = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(root);
        var document = OpenSourceCorpusCatalog.Load(root);
        if (maximumMethods < 0 || maximumMethods > document.Methods.Length)
        { throw new ArgumentOutOfRangeException(nameof(maximumMethods)); }
        var universeSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            Path.Combine(root, "SharpProof.Gates", "Corpus", "oss-methods.json"), cancellationToken).ConfigureAwait(false)));
        var selected = maximumMethods == 0 || maximumMethods == document.Methods.Length ? document : document with
        {
            Methods = [.. document.Methods.OrderBy(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Id), StringComparer.Ordinal)
                .Take(maximumMethods == 0 ? document.Methods.Length : maximumMethods)]
        };
        var report = await ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(selected, cancellationToken, allocations, purity, capabilities, summary),
            selected.Methods.Select(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Id)).ToImmutableArray(), root,
            universeSha256, document.Methods.Length, cancellationToken, allocations, purity, capabilities, summary).ConfigureAwait(false);
        return report with { WallSeconds = wall.Elapsed.TotalSeconds };
    }

    internal static async Task<NativeEffectOracleReport> ObserveAsync(CSharpCompilation compilation,
        ImmutableArray<string> methodIds, string root, string universeSha256, int universeMethodCount, CancellationToken cancellationToken,
        bool allocations = false, bool purity = false, bool capabilities = false, bool summary = false)
    {
        System.ArgumentNullException.ThrowIfNull(universeSha256);
        var wall = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(root);
        if (new[] { allocations, purity, capabilities, summary }.Count(selected => selected) > 1)
        { throw new ArgumentException("An oracle run must select exactly one effect contract.", nameof(purity)); }
        if (methodIds.IsDefaultOrEmpty || methodIds.Any(string.IsNullOrWhiteSpace) || methodIds.Distinct(StringComparer.Ordinal).Count() != methodIds.Length ||
            methodIds.Length > universeMethodCount)
        { throw new ArgumentException("An oracle universe must have unique, nonempty method identities.", nameof(methodIds)); }
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var targets = discovery.Targets.Values.Where(target => target is null ? throw new System.ArgumentNullException(nameof(target)) : (OpenSourceCorpusRunner.CorpusMethodId(target.Declaration) != null))
            .ToDictionary(target => target is null ? throw new System.ArgumentNullException(nameof(target)) : (OpenSourceCorpusRunner.CorpusMethodId(target.Declaration)!), StringComparer.Ordinal);
        if (!targets.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(methodIds))
        { throw new InvalidDataException("The declared exception probes do not equal the selected corpus universe."); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, root, "net9.0", WorkerFeatureSet.Effects,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken);
        var validatedArtifact = CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken);
        var sourceSummaries = validatedArtifact.ReachableSource is { } source
            ? EffectSummaryFixpoint.ComputeValidated(source, cancellationToken)
            : ImmutableSortedDictionary<string, SourceEffectSummary>.Empty;
        var owned = preparations.ToDictionary(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.Entry.CallableId), StringComparer.Ordinal);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var oracle = new NativeExceptionWitnessOracle(compilation);
        using var allocationOracle = new NativeAllocationWitnessOracle(compilation);
        var rows = ImmutableArray.CreateBuilder<NativeEffectOracleRow>(methodIds.Length);
        foreach (var id in methodIds.OrderBy(id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = targets[id];
            var preparation = owned[target.Entry.CallableId];
            var claim = preparation.EffectClaims.Single(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ContractKind == (summary ? WorkerEffectContractKind.EffectContract
                : capabilities ? WorkerEffectContractKind.AllowedCapabilities
                : purity ? WorkerEffectContractKind.EnforcePure : allocations
                ? WorkerEffectContractKind.ZeroAllocations : WorkerEffectContractKind.DoesNotThrow)));
            var evidence = summary
                ? await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), cancellationToken).ConfigureAwait(false)
                : capabilities
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
            RequireReplayValidatedWitness(evidence, allocations, purity, capabilities, summary);
            var checkAllocation = allocations || summary && evidence.AllocationWitness != null;
            // Native verifier refutations carry the replay-validated witness
            // family selected by each effect mode's maintained producer.
            var allocationObservation = checkAllocation && outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted
                ? allocationOracle.Check(target.Method, preparation.Total ?? throw new InvalidDataException("A native effect verdict has no total body to replay."), evidence, outcome, cancellationToken) : null;
            var writeObservation = summary && outcome == WorkerClaimOutcome.Refuted && evidence.WriteWitness != null
                ? NativeStaticWriteWitnessOracle.Check(compilation, target.Method, preparation.Total ?? throw new InvalidDataException("A native effect verdict has no total body to replay."), evidence, cancellationToken) : null;
            var runtime = writeObservation?.RuntimeOracle ?? (summary && evidence.LockWitness != null ? "LockOracleNotRun"
                : capabilities ? "CapabilityOracleNotRun" : purity ? "PurityOracleNotRun" : checkAllocation ? allocationObservation?.RuntimeOracle ?? "NotRun" : outcome == WorkerClaimOutcome.Refuted
                ? oracle.Check(target.Method, preparation.Total ?? throw new InvalidDataException("A native effect verdict has no total body to replay."), evidence.EntryModel, evidence.ExceptionWitness!, cancellationToken)
                : "NotRun");
            rows.Add(new(id, preparation.Entry.CallableId, outcome, evidence.Reason,
                preparation.Total != null, preparation.Total?.IsBodyAbstraction == true,
                evidence.ExceptionWitness?.Kind.ToString(), runtime)
            {
                AllocationIlOracle = allocationObservation?.IlOracle ?? "NotRun",
                RuntimeChecks = writeObservation?.RuntimeChecks ?? allocationObservation?.RuntimeChecks ?? 0,
                AllocatedBytes = allocationObservation?.AllocatedBytes
            });
        }
        return Summarize(universeSha256, universeMethodCount, rows.ToImmutable(), wall.Elapsed.TotalSeconds) with
        {
            ContractKind = summary ? "EffectContract" : capabilities ? "AllowedCapabilities" : purity ? "EnforcePure" : allocations ? "ZeroAllocations" : "DoesNotThrow",
            ReachableSourceBodyCount = sourceSummaries.Count,
            ReachableSourceMayDivergeCount = sourceSummaries.Values.Count(summary => summary.MayDiverge),
            ReachableSourceUnknownEffectCount = sourceSummaries.Values.Count(summary =>
                summary.UnknownEffects != SourceMayEffect.None || summary.UnknownExceptions)
        };
    }

    // Raw-result admission only: rejects a refutation without the selected effect
    // witness family. Passing this check grants no effect verdict or ownership.
    internal static void RequireReplayValidatedWitness(PassiveCallableCheckResult evidence,
        bool allocations = false, bool purity = false, bool capabilities = false, bool summary = false)
    {
        if (evidence.Outcome is RefutedOutcome && (summary
            ? evidence.WriteWitness == null && evidence.LockWitness == null && evidence.AllocationWitness == null && evidence.ExceptionWitness == null
            : capabilities ? evidence.LockWitness == null
            : purity ? evidence.WriteWitness == null && evidence.LockWitness == null : allocations ? evidence.AllocationWitness == null : evidence.ExceptionWitness == null))
        { throw new InvalidDataException("A native effect refutation has no replay-validated witness."); }
    }

    internal static NativeEffectOracleReport Summarize(string universeSha256, int universeMethodCount,
        ImmutableArray<NativeEffectOracleRow> rows, double wallSeconds)
    {
        System.ArgumentNullException.ThrowIfNull(universeSha256);
        if (rows.IsDefaultOrEmpty || rows.Length > universeMethodCount || rows.Any(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row is null || string.IsNullOrWhiteSpace(row.MethodId))) ||
            rows.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.MethodId)).Distinct(StringComparer.Ordinal).Count() != rows.Length)
        { throw new ArgumentException("Oracle rows do not define a unique covered universe.", nameof(rows)); }
        var runtimeContradictions = rows.Count(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.RuntimeOracle == "Contradiction"));
        // Passed means no observed runtime contradiction. Unsupported or
        // unchanged write observations remain visible gaps in the rows.
        return new(universeSha256, universeMethodCount, rows.Length, rows.Length == universeMethodCount,
            rows.Count(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.NativeOutcome == WorkerClaimOutcome.Proven)), rows.Count(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.NativeOutcome == WorkerClaimOutcome.Refuted)),
            rows.Count(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.RuntimeOracle == "Confirmed")), runtimeContradictions, wallSeconds, rows,
            rows.Where(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.NativeOutcome == WorkerClaimOutcome.Unknown)).GroupBy(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.NativeReason.ToString()), StringComparer.Ordinal)
                .ToImmutableDictionary(group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Key), group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Count()), StringComparer.Ordinal),
            runtimeContradictions == 0);
    }
}
