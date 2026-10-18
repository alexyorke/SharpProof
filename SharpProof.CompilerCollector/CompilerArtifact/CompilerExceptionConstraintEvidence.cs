namespace SharpProof.CompilerArtifact;

// Projects eligible compiler effect evidence atomically against a runtime assembly.
internal static class CompilerExceptionConstraintEvidence
{
    internal static ImmutableArray<CompilerTotalExceptionConstraint> Project(
        IAssemblySymbol core, IEnumerable<CompilerEffectClaimArtifact> claims)
    {
        claims = RepositoryParameterGuard.NotNull(claims, nameof(claims));
        var constraints = ImmutableList<CompilerTotalExceptionConstraint>.Empty;
        foreach (var evidence in claims)
        {
            var listsTypes = evidence.ContractKind == WorkerEffectContractKind.AllowedExceptions ||
                evidence.ContractKind == WorkerEffectContractKind.EffectContract &&
                (evidence.Constraint.AllowedEffects & WorkerEffectSet.Throws) != 0;
            var allowed = ImmutableList<IrExceptionKind>.Empty;
            foreach (var kind in (IrExceptionKind[])Enum.GetValues(typeof(IrExceptionKind)))
            {
                var runtime = core.GetTypeByMetadataName(CSharpOperationSemantics.ExceptionMetadataName(kind));
                if (runtime == null)
                { return []; }
                if (listsTypes &&
                    CompilerExceptionTypeIdentity.EncodeHierarchy(runtime).Any(identity =>
                        identity is null ? throw new System.ArgumentNullException(nameof(identity)) : (evidence.Constraint.AllowedExceptionTypes.Contains(identity, StringComparer.Ordinal))))
                { allowed = allowed.Add(kind); }
            }
            constraints = constraints.Add(new(evidence.ClaimId, allowed.ToImmutableArray()));
        }
        return constraints.ToImmutableArray();
    }
}
