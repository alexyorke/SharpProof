namespace SharpProof.CompilerArtifact;

// Projects eligible compiler effect evidence atomically against a runtime assembly.
internal static class CompilerExceptionConstraintEvidence
{
    internal static ImmutableArray<CompilerTotalExceptionConstraint> Project(
        IAssemblySymbol core, IEnumerable<CompilerEffectClaimArtifact> claims)
    {
        if (claims is null)
        {
            throw new System.ArgumentNullException(nameof(claims));
        }
        var constraints = ImmutableArray.CreateBuilder<CompilerTotalExceptionConstraint>();
        foreach (var evidence in claims)
        {
            var listsTypes = evidence.ContractKind == WorkerEffectContractKind.AllowedExceptions ||
                evidence.ContractKind == WorkerEffectContractKind.EffectContract &&
                (evidence.Constraint.AllowedEffects & WorkerEffectSet.Throws) != 0;
            var allowed = ImmutableArray.CreateBuilder<IrExceptionKind>();
            foreach (var kind in (IrExceptionKind[])Enum.GetValues(typeof(IrExceptionKind)))
            {
                var runtime = core.GetTypeByMetadataName(CSharpOperationSemantics.ExceptionMetadataName(kind));
                if (runtime == null)
                { return []; }
                if (listsTypes &&
                    CompilerExceptionTypeIdentity.EncodeHierarchy(runtime).Any(identity =>
                        identity is null ? throw new System.ArgumentNullException(nameof(identity)) : (evidence.Constraint.AllowedExceptionTypes.Contains(identity, StringComparer.Ordinal))))
                { allowed.Add(kind); }
            }
            constraints.Add(new(evidence.ClaimId, allowed.ToImmutable()));
        }
        return constraints.ToImmutable();
    }
}
