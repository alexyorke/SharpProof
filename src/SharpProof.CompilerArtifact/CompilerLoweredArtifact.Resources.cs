using System.Collections.Immutable;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static partial class CompilerLoweredArtifact
{
    private static (byte[]? Program, string? Unknown) EncodeResource(CompilerCallablePreparation preparation, CancellationToken cancellation)
    {
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        if (preparation.ResourceProgram is null)
        { return (null, preparation.ResourceUnknownReason); }
        if (preparation.ResourceUnknownReason is not null)
        { throw new InvalidDataException("Resource proof and gap are mutually exclusive."); }
        try
        { return (ResourceProgramCodec.Encode(preparation.ResourceProgram, cancellation), null); }
        catch (ResourceExpressionLimitException) { return (null, "ResourceProofBudgetExceeded"); }
    }

    private static (ResourceProgram? Program, string? Unknown) DecodeResource(CompilerCallableArtifact artifact,
        WorkerCallableManifestEntry entry, ImmutableArray<WorkerClaimManifestEntry> claims,
        CompilerTotalCallablePreparation? total, CancellationToken cancellation)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));
        entry = ArgumentNullGuard.NotNull(entry, nameof(entry));
        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        cancellation.ThrowIfCancellationRequested();
        var resources = claims.Where(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) :
            claim.EffectContractKind == WorkerEffectContractKind.ResourceUpperBound).ToArray();
        if (resources.Length == 0)
        {
            if (artifact.ResourceProgram is not null || artifact.ResourceUnknownReason is not null)
            { throw new InvalidDataException("Resource evidence has no owning claim."); }
            return (null, null);
        }
        foreach (var claim in resources)
        {
            var constraint = claim.ResourceUpperBound ?? throw new InvalidDataException("Resource claim constraint is missing.");
            if (total is { IsBodyAbstraction: false })
            { _ = ResourceExpressionCodec.DecodeForCallable(constraint.UpperBound, entry.CallableId, total, cancellation); }
            else
            { _ = ResourceExpressionCodec.Decode(constraint.UpperBound, cancellation); }
        }
        if (artifact.ResourceProgram is null)
        {
            var reason = artifact.ResourceUnknownReason;
            if (reason is null || reason.Length == 0 || reason.Length > 128 || reason.Any(static value => value < '!' || value > '~'))
            { throw new InvalidDataException("Resource proof gap is missing or malformed."); }
            return (null, reason);
        }
        if (artifact.ResourceUnknownReason is not null || total is null || total.IsBodyAbstraction)
        { throw new InvalidDataException("Resource proof ownership is invalid."); }
        var program = ResourceProgramCodec.Decode(artifact.ResourceProgram, cancellation);
        ResourceProgramOwnership.Validate(program, entry.CallableId, total, cancellation);
        return (program, null);
    }
}
