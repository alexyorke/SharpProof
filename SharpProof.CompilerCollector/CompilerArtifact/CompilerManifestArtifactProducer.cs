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
        return [.. authorities.Select(authority =>
        {
            var row = new CompilerSummaryEvidenceSnapshot
            {
                Origin = authority.Origin,
                CallIdentity = authority.CallIdentity,
                EvidenceSha256 = authority.EvidenceSha256,
                EvidenceIdentity = authority.EvidenceIdentity,
                SourcePath = authority.SourcePath,
                SourceTreeSha256 = authority.SourceTreeSha256,
                SourceStart = authority.SourceStart,
                SourceLength = authority.SourceLength,
                OwningModuleName = authority.OwningModuleName,
                MethodMetadataToken = authority.MethodMetadataToken
            };

            if (authority.Origin == CompilerSummaryOrigin.Source)
            {
                if (!snapshot.SyntaxTrees.Any(tree =>
                        tree.Path == authority.SourcePath &&
                        tree.Sha256 == authority.SourceTreeSha256 &&
                        authority.SourceStart >= 0 &&
                        authority.SourceLength > 0 &&
                        authority.SourceStart <= tree.TextLength - authority.SourceLength))
                {
                    throw new InvalidOperationException(
                        "A source summary authority is not bound to the captured source tree.");
                }
            }
            else if (authority.Origin == CompilerSummaryOrigin.ImplementationIl)
            {
                var module = snapshot.References
                    .SelectMany(static reference => reference.Modules)
                    .Where(module =>
                        module.Name == authority.OwningModuleName &&
                        module.Sha256 == authority.EvidenceSha256)
                    .ToArray();
                if (module.Length != 1)
                {
                    throw new InvalidOperationException(
                        "An IL summary authority is not bound to one captured module.");
                }

                row.OwningModuleMvid = module[0].Mvid;
                row.OwningModuleSha256 = module[0].Sha256;
            }
            else if (authority.Origin == CompilerSummaryOrigin.SpecificationPack)
            {
                if (authority.EvidenceSha256 != snapshot.SpecificationPackCatalogSha256 ||
                    !IsSelectedPackIdentity(
                        authority.EvidenceIdentity,
                        snapshot.SpecificationPackIds))
                {
                    throw new InvalidOperationException(
                        "A specification-pack summary authority is not bound to the selected catalog.");
                }
            }

            return row;
        })];
    }

    private static bool IsSelectedPackIdentity(
        string identity,
        IEnumerable<string> selectedPackIds)
    {
        if (identity.Length == 0 ||
            !CompilerSpecificationPackCatalogVersions.PackIdentities
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Contains(identity, StringComparer.Ordinal))
        {
            return false;
        }

        var separator = identity.LastIndexOf('@');
        return separator > 0 && selectedPackIds.Contains(
            identity[..separator],
            StringComparer.Ordinal);
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
