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


        if (additionalFiles.IsDefault)
        {
            additionalFiles = [];
        }
        projectDirectory = RepositoryParameterGuard.NotNull(projectDirectory, nameof(projectDirectory));
        targetFramework = RepositoryParameterGuard.NotNull(targetFramework, nameof(targetFramework));
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        discovery = ArgumentNullGuard.NotNull(discovery, nameof(discovery));
        var specificationPackAuthority =
            CompilerSpecificationPackProvider.ResolveConfiguration(
                specificationPacks.IsDefault ? [] : specificationPacks);
        var snapshot = CompilerCompilationCapture.Capture(
            compilation, projectDirectory, targetFramework, additionalFiles, cancellationToken);
        snapshot = snapshot with
        {
            SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds],
            SpecificationPackCatalogVersion = specificationPackAuthority.SpecificationPackCatalogVersion,
            SpecificationPackCatalogSha256 = specificationPackAuthority.SpecificationPackCatalogSha256
        };
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Severity == DiagnosticSeverity.Error &&
                !item.IsSuppressed))
            .Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (CreateDiagnostic(item, snapshot)));
        var diagnosticArtifacts =
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics);
        var targets = discovery.Targets.Values.OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId), StringComparer.Ordinal);
        ImmutableArray<CompilerCallableArtifact> callables;
        if (diagnosticArtifacts.Length != 0)
        {
            callables = [.. targets.Select(item =>
            {
                item = RepositoryParameterGuard.NotNull(item, nameof(item));
                var artifact = new CompilerCallableArtifact
                {
                    CallableId = item.Entry.CallableId,
                    FailureReason =
                        CompilerCallableArtifactReasonCatalog.DiagnosticFailureReason
                };
                return artifact.AttachEffectEvidence(item);
            })];
        }
        else
        {
            callables = [.. targets.Select(item => {
                // Each encoded callable is a self-contained IR graph. Keep its
                // lowering factory self-contained too: contract binding and
                // body lowering may request the same symbol with different
                // display-name purposes (opaque versus call), and IrFactory
                // intentionally hash-conses those requests by structural
                // identity rather than display name.
                item = RepositoryParameterGuard.NotNull(item, nameof(item));
                var lowerer = new CompilerCallableLowerer(
                    compilation,
                    new IrFactory(),
                    specificationPackAuthority,
                    snapshot.SyntaxTrees,
                    snapshot.References);
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken), cancellationToken);
                return artifact.AttachEffectEvidence(item);
            })];
        }
        var artifact = new CompilerManifestArtifact
        {
            SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds],
            SpecificationPackCatalogVersion =
                specificationPackAuthority.SpecificationPackCatalogVersion,
            SpecificationPackCatalogSha256 =
                specificationPackAuthority.SpecificationPackCatalogSha256,
            Features = features,
            Compilation = snapshot,
            Manifest = discovery.Manifest,
            MaximumExpressionDepth = maximumExpressionDepth,
            CompilerDiagnostics = diagnosticArtifacts,
            Callables = callables,
            ReachableSource = diagnosticArtifacts.Length == 0
                ? CompilerReachableSourceCollector.Collect(compilation, targets, snapshot.SyntaxTrees, specificationPackAuthority, cancellationToken)
                : null
        };
        return artifact;
    }

    private static CompilerCallableArtifact AttachEffectEvidence(
        this CompilerCallableArtifact artifact,
        ManifestCallableTarget target)
    {
        artifact = RepositoryParameterGuard.NotNull(artifact, nameof(artifact));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        var claims = target.EffectClaims;
        var evidence = ImmutableArray.CreateBuilder<CompilerEffectClaimArtifact>(claims.Length);
        for (var index = 0; index < claims.Length; index++)
        {
            var claim = claims[index];
            evidence.Add(claim.Evidence);
        }
        return artifact with { EffectClaims = evidence.ToImmutable() };
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(
        Diagnostic diagnostic,
        CompilerCompilationSnapshot compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        diagnostic = RepositoryParameterGuard.NotNull(diagnostic, nameof(diagnostic));
        var source = diagnostic.Location.IsInSource;
        var location = CompilerSourceLocationProjection.Create(diagnostic.Location);
        var sourceTreeOrdinal = -1;
        var sourceTreePath = string.Empty;
        var sourceTreeSha256 = string.Empty;
        var sourceLineMapSha256 = string.Empty;
        if (source)
        {
            CompilerSourceCoordinates.Bind(
                location,
                compilation,
                out sourceTreeOrdinal,
                out sourceTreePath,
                out sourceTreeSha256,
                out sourceLineMapSha256);
        }
        return new CompilerDiagnosticArtifact
        {
            Code = "compiler." + diagnostic.Id,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            IsSource = source,
            Location = location,
            SourceTreeOrdinal = sourceTreeOrdinal,
            SourceTreePath = sourceTreePath,
            SourceTreeSha256 = sourceTreeSha256,
            SourceLineMapSha256 = sourceLineMapSha256
        };
    }
}
