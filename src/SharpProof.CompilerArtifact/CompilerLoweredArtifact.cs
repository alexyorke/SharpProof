using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// The callable payload is the admission reason, the compiler's effect claims
// and the Total programs; the worker verifies only those programs.
internal static class CompilerLoweredArtifact
{
    internal static CompilerCallableArtifact Encode(CompilerCallablePreparation preparation)
    {
        preparation = RepositoryParameterGuard.NotNull(preparation, nameof(preparation));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return Encode(preparation, cancellation.Token);
    }

    internal static CompilerCallableArtifact Encode(
        CompilerCallablePreparation preparation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        return new CompilerCallableArtifact
        {
            CallableId = preparation.Entry.CallableId,
            FailureReason = preparation.FailureReason,
            EffectClaims = preparation.EffectClaims.ToArray(),
            Total = CompilerTotalCallableArtifactCodec.Encode(preparation.Total, cancellationToken),
            TotalEntry = CompilerTotalCallableArtifactCodec.EncodeEntry(preparation.TotalEntry, cancellationToken)
        };
    }

    internal static ImmutableArray<CompilerCallablePreparation> Decode(
        CompilerCallableArtifact[] artifacts,
        WorkerClaimManifest manifest,
        CompilerCompilationSnapshot compilation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        manifest = ArgumentNullGuard.NotNull(manifest, nameof(manifest));
        if (artifacts == null)
        {
            throw new InvalidDataException("The lowered callable payload is missing.");
        }

        if (compilation == null)
        {
            throw new InvalidDataException("The compiler compilation evidence is missing.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var callables = manifest.Callables.ToDictionary(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId), StringComparer.Ordinal);
        var claims = manifest.Claims.GroupBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId))
            .ToDictionary(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Key),
                static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Ordinal)).ToImmutableArray()),
                StringComparer.Ordinal);
        if (artifacts.Length != callables.Count)
        {
            throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
        }
        var callableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact == null ||
                artifact.CallableId == null ||
                !callableIds.Add(artifact.CallableId) ||
                !callables.ContainsKey(artifact.CallableId))
            {
                throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
            }
        }

        var result = ImmutableArray.CreateBuilder<CompilerCallablePreparation>(artifacts.Length);
        foreach (var artifact in artifacts.OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = callables[artifact.CallableId];
            var targetClaims = claims.TryGetValue(artifact.CallableId, out var rows) ? rows : [];
            if (!entry.ClaimIds.SequenceEqual(targetClaims.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId)), StringComparer.Ordinal))
            {
                throw new InvalidDataException("A lowered callable claim list does not equal the manifest.");
            }

            result.Add(Decode(
                artifact,
                entry,
                targetClaims,
                compilation,
                cancellationToken));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.MoveToImmutable();
    }
    private static CompilerCallablePreparation Decode(
        CompilerCallableArtifact artifact,
        WorkerCallableManifestEntry entry,
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CompilerCompilationSnapshot compilation,
        CancellationToken cancellationToken)
    {

        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        entry = RepositoryParameterGuard.NotNull(entry, nameof(entry));
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.FailureReason !=
                CompilerCallableArtifactReasonCatalog.SuccessReason &&
            !CompilerCallableArtifactReasonCatalog.IsFailureReason(
                artifact.FailureReason))
        {
            throw new InvalidDataException("A lowered callable reason is invalid.");
        }

        return new CompilerCallablePreparation(entry, artifact.FailureReason)
        {
            EffectClaims = DecodeEffects(artifact, claims, cancellationToken),
            Compilation = compilation,
            Total = CompilerTotalCallableArtifactCodec.Decode(artifact.Total, entry, claims, cancellationToken),
            TotalEntry = CompilerTotalCallableArtifactCodec.DecodeEntry(artifact.TotalEntry, entry, cancellationToken)
        };
    }

    private static ImmutableArray<CompilerEffectClaimArtifact> DecodeEffects(
        CompilerCallableArtifact artifact,
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CancellationToken cancellationToken)
    {

        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.EffectClaims == null)
        {
            throw new InvalidDataException("Compiler effect-claim evidence is missing.");
        }

        var expected = claims.Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerClaimKind.Effect)).ToArray();
        if (artifact.EffectClaims.Length != expected.Length)
        {
            throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
        }

        var effectClaimIds = new HashSet<string?>(StringComparer.Ordinal);
        for (var index = 0; index < artifact.EffectClaims.Length; index++)
        {
            if (!effectClaimIds.Add(artifact.EffectClaims[index]?.ClaimId))
            {
                throw new InvalidDataException(
                    "Compiler effect-claim evidence does not equal the manifest.");
            }
        }

        for (var index = 0; index < expected.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = artifact.EffectClaims[index];
            CompilerEffectClaimArtifactCodec.Validate(evidence);
            if (evidence.ClaimId != expected[index].ClaimId || evidence.ContractKind != expected[index].EffectContractKind)
            {
                throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return [.. artifact.EffectClaims];
    }
}
