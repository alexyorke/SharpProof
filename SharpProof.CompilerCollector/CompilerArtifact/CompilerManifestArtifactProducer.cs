// This producer runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal static class CompilerManifestArtifactProducer
{
    internal static CompilerManifestArtifact Create(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, WorkerFeatureSet features, ClaimManifestBuildResult discovery,
        int maximumExpressionDepth, CancellationToken cancellationToken,
        ImmutableArray<AdditionalText> additionalFiles = default,
        ImmutableArray<string> specificationPacks = default)
    {
        var specificationPackAuthority =
            CompilerSpecificationPackProvider.ResolveAuthority(
                specificationPacks.IsDefault ? [] : specificationPacks);
        var snapshot = CompilerCompilationCapture.Capture(
            compilation, projectDirectory, targetFramework, additionalFiles, cancellationToken);
        snapshot.SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds];
        snapshot.SpecificationPackCatalogVersion =
            specificationPackAuthority.SpecificationPackCatalogVersion;
        snapshot.SpecificationPackCatalogSha256 =
            specificationPackAuthority.SpecificationPackCatalogSha256;
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item.Severity == DiagnosticSeverity.Error)
            .Select(CreateDiagnostic);
        var diagnosticArtifacts =
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics);
        var targets = discovery.Targets.Values.OrderBy(static item => item.Entry.CallableId, StringComparer.Ordinal);
        CompilerCallableArtifact[] callables;
        if (diagnosticArtifacts.Length != 0)
        {
            callables = [.. targets.Select(item => new CompilerCallableArtifact {
                CallableId = item.Entry.CallableId,
                FailureReason =
                    CompilerCallableProducerReasonCatalog.DiagnosticFailureReason,
                EffectClaims = [.. item.EffectClaims.Select(static claim => claim.Evidence)],
                EffectAuthorities = [.. item.EffectClaims.Select(claim =>
                {
                    CompilerEffectAuthority.BindSourceTree(
                        claim.Authority,
                        snapshot);
                    return claim.Authority;
                })]
            })];
        }
        else
        {
            var lowerer = new CompilerCallableLowerer(
                compilation,
                new IrFactory(),
                specificationPackAuthority);
            callables = [.. targets.Select(item => {
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken));
                artifact.EffectClaims = [.. item.EffectClaims.Select(
                    static claim => claim.Evidence)];
                artifact.EffectAuthorities = [.. item.EffectClaims.Select(claim =>
                {
                    CompilerEffectAuthority.BindSourceTree(
                        claim.Authority,
                        snapshot);
                    return claim.Authority;
                })];
                return artifact;
            })];
            snapshot.SummaryEvidence = BuildSummaryEvidence(
                snapshot,
                lowerer.SummaryEvidenceAuthorities);
        }
        var artifact = new CompilerManifestArtifact
        {
            SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds],
            SpecificationPackCatalogVersion =
                specificationPackAuthority.SpecificationPackCatalogVersion,
            SpecificationPackCatalogSha256 =
                specificationPackAuthority.SpecificationPackCatalogSha256,
            Features = features,
            CompilationSha256 = CompilationFingerprint.ComputeSha256(
                snapshot, diagnosticArtifacts),
            Compilation = snapshot,
            Manifest = discovery.Manifest,
            MaximumExpressionDepth = maximumExpressionDepth,
            CompilerDiagnostics = diagnosticArtifacts,
            Callables = callables
        };
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);
        CompilerManifestArtifactJson.Validate(artifact);
        return artifact;
    }

    private static CompilerSummaryEvidenceSnapshot[] BuildSummaryEvidence(
        CompilerCompilationSnapshot snapshot,
        ImmutableArray<CompilerSummaryEvidenceAuthority> authorities)
    {
        var modules = snapshot.References
            .SelectMany(static reference => reference.Modules)
            .ToArray();
        return [.. authorities
            .Select(authority => {
                if (authority.Origin != CompilerSummaryOrigin.ImplementationIl)
                {
                    if (authority.OwningModuleName.Length != 0)
                    {
                        throw new InvalidDataException(
                            "A non-IL summary has an owning module.");
                    }

                    return new CompilerSummaryEvidenceSnapshot
                    {
                        Origin = authority.Origin,
                        CallIdentity = authority.CallIdentity,
                        EvidenceSha256 = authority.EvidenceSha256,
                        EvidenceIdentity = authority.EvidenceIdentity
                    };
                }

                var module = modules.SingleOrDefault(candidate =>
                    candidate.Name == authority.OwningModuleName &&
                    candidate.Sha256 == authority.EvidenceSha256);
                if (module == null)
                {
                    throw new InvalidDataException(
                        "An IL summary has no matching owning module evidence.");
                }

                return new CompilerSummaryEvidenceSnapshot
                {
                    Origin = authority.Origin,
                    CallIdentity = authority.CallIdentity,
                    EvidenceSha256 = authority.EvidenceSha256,
                    EvidenceIdentity = authority.EvidenceIdentity,
                    OwningModuleName = module.Name,
                    OwningModuleMvid = module.Mvid,
                    OwningModuleSha256 = module.Sha256
                };
            })
            .OrderBy(static value => (int)value.Origin)
            .ThenBy(static value => value.CallIdentity, StringComparer.Ordinal)
            .ThenBy(static value => value.EvidenceIdentity, StringComparer.Ordinal)
            .ThenBy(static value => value.EvidenceSha256, StringComparer.Ordinal)
            .ThenBy(static value => value.OwningModuleName, StringComparer.Ordinal)
            .ThenBy(static value => value.OwningModuleMvid, StringComparer.Ordinal)
            .ThenBy(static value => value.OwningModuleSha256, StringComparer.Ordinal)];
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(Diagnostic diagnostic)
    {
        var source = diagnostic.Location.IsInSource;
        var span = source ? diagnostic.Location.GetMappedLineSpan() : default;
        return new CompilerDiagnosticArtifact
        {
            Code = "compiler." + diagnostic.Id,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Location = new WorkerSourceLocation
            {
                Path = span.Path ?? string.Empty,
                Start = source ? diagnostic.Location.SourceSpan.Start : 0,
                Length = source ? diagnostic.Location.SourceSpan.Length : 0,
                Line = source ? span.StartLinePosition.Line + 1 : 0,
                Column = source ? span.StartLinePosition.Character + 1 : 0
            }
        };
    }
}
