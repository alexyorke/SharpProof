// This lowerer runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

// Classifies a discovered callable and lowers its Total programs. The reason
// says why a callable cannot be verified; a body the Total IR cannot lower is
// UnsupportedBody.
internal sealed class CompilerCallableLowerer
{
    private readonly CSharpCompilation _compilation;
    private readonly CompilerSyntaxTreeSnapshot[]? _capturedTrees;
    private readonly CompilerReferenceSnapshot[]? _capturedReferences;
    private readonly ContractBinder _contracts;
    private readonly CompilerSpecificationPackConfiguration _specificationPackAuthority;

    internal CompilerCallableLowerer(
        CSharpCompilation compilation,
        IrFactory factory,
        IEnumerable<string>? specificationPacks = null)
        : this(
            compilation,
            factory,
            CompilerSpecificationPackProvider.ResolveConfiguration(specificationPacks))
    {
    }

    internal CompilerCallableLowerer(
        CSharpCompilation compilation,
        IrFactory factory,
        CompilerSpecificationPackConfiguration specificationPackAuthority,
        CompilerSyntaxTreeSnapshot[]? capturedTrees = null,
        CompilerReferenceSnapshot[]? capturedReferences = null)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _compilation = compilation;
        _capturedTrees = capturedTrees;
        _capturedReferences = capturedReferences;
        _specificationPackAuthority = specificationPackAuthority;
        _contracts = new ContractBinder(compilation, ArgumentNullGuard.NotNull(factory, nameof(factory)));
    }

    internal CompilerCallablePreparation Prepare(ManifestCallableTarget target, CancellationToken cancellationToken = default)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        var capturedTrees = _capturedTrees ?? CompilerCompilationCapture.CaptureTrees(_compilation, cancellationToken);
        var total = CompilerTotalCallableLowerer.Prepare(_compilation, target, capturedTrees,
            _capturedReferences, _specificationPackAuthority, cancellationToken);
        var entry = total == null
            ? CompilerTotalCallableLowerer.PrepareEntry(_compilation, target, capturedTrees, cancellationToken)
            : new CompilerTotalEntryPreparation(total.CallableId, total.Program.Factory, total.Parameters,
                [.. total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Requires)]);
        var reason = Classify(target, total != null, cancellationToken);
        if (reason != CompilerCallableArtifactReasonCatalog.SuccessReason &&
            !CompilerCallableArtifactReasonCatalog.IsFailureReason(reason))
        {
            throw new InvalidOperationException(
                "The compiler callable failure reason is not producer-owned.");
        }
        return new(target.Entry, reason) { Total = total, TotalEntry = entry };
    }

    private WorkerClaimReason Classify(ManifestCallableTarget target, bool hasTotal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsVerifierSupported || target.Declaration is not BaseMethodDeclarationSyntax || target.SemanticModel == null)
        {
            return WorkerClaimReason.UnsupportedCallable;
        }

        var binding = _contracts.Bind(target.Method);
        if (!binding.IsSuccess)
        {
            return MapBindingFailure(binding.Failure);
        }

        var contracts = binding.Contracts!;
        var assumptions = ArgumentNullGuard.NotNull(target.Entry.Assumptions, nameof(target.Entry.Assumptions));
        if (contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Requires) !=
                assumptions.Count(static evidence => evidence.Kind == WorkerAssumptionKind.Precondition) ||
            contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Assume) !=
                assumptions.Count(static evidence => evidence.Kind == WorkerAssumptionKind.UserAssume) ||
            !HasManifestParity(target, contracts))
        {
            return WorkerClaimReason.UnsupportedContract;
        }

        // An effect-only callable without clauses needs no body admission.
        return contracts.Clauses.IsDefaultOrEmpty || hasTotal ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody;
    }

    private static bool HasManifestParity(ManifestCallableTarget target, BoundMethodContracts contracts)
    {
        var ensures = contracts.Clauses.Where(static clause => clause.Kind == BoundContractKind.Ensures).ToImmutableArray();
        if (ensures.Length != target.Claims.Length ||
            !target.Entry.ClaimIds.Take(target.Claims.Length).SequenceEqual(
                target.Claims.Select(static claim => claim.Entry.ClaimId), StringComparer.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < ensures.Length; index++)
        {
            var claim = target.Claims[index];
            if (claim.Entry.Ordinal != index ||
                claim.Entry.Kind != WorkerClaimKind.Postcondition ||
                claim.Entry.CallableId != target.Entry.CallableId ||
                claim.Entry.Evidence !=
                    CompilerLoweringWireMappings.ToWorkerEvidence(ensures[index].Evidence) ||
                (ensures[index].Evidence == BoundContractEvidence.ClosedAttribute) != (claim.SourceAttribute != null) ||
                (ensures[index].Evidence != BoundContractEvidence.ClosedAttribute) != (claim.SourceOperation != null))
            {
                return false;
            }
        }
        return true;
    }

    private static WorkerClaimReason MapBindingFailure(ContractBindingFailure failure)
    {
        try
        {
            return CompilerLoweringWireMappings.ToWorkerFailure(failure);
        }
        catch (ArgumentOutOfRangeException)
        {
            return WorkerClaimReason.UnsupportedCallable;
        }
    }
}
