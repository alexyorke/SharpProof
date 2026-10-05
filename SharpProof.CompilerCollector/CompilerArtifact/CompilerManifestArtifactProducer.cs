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
            CompilerSpecificationPackProvider.ResolveConfiguration(
                specificationPacks.IsDefault ? [] : specificationPacks);
        var snapshot = CompilerCompilationCapture.Capture(
            compilation, projectDirectory, targetFramework, additionalFiles, cancellationToken);
        snapshot.SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds];
        snapshot.SpecificationPackCatalogVersion =
            specificationPackAuthority.SpecificationPackCatalogVersion;
        snapshot.SpecificationPackCatalogSha256 =
            specificationPackAuthority.SpecificationPackCatalogSha256;
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item.Severity == DiagnosticSeverity.Error &&
                !item.IsSuppressed)
            .Select(item => CreateDiagnostic(item, snapshot));
        var diagnosticArtifacts =
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics);
        var targets = discovery.Targets.Values.OrderBy(static item => item.Entry.CallableId, StringComparer.Ordinal);
        CompilerCallableArtifact[] callables;
        if (diagnosticArtifacts.Length != 0)
        {
            callables = [.. targets.Select(item =>
            {
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
                var lowerer = new CompilerCallableLowerer(
                    compilation,
                    new IrFactory(),
                    specificationPackAuthority,
                    snapshot.SyntaxTrees,
                    snapshot.References);
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken));
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
        var claims = target.EffectClaims;
        var evidence = new CompilerEffectClaimArtifact[claims.Length];
        for (var index = 0; index < claims.Length; index++)
        {
            var claim = claims[index];
            evidence[index] = claim.Evidence;
        }
        artifact.EffectClaims = evidence;
        return artifact;
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(
        Diagnostic diagnostic,
        CompilerCompilationSnapshot compilation)
    {
        var source = diagnostic.Location.IsInSource;
        var location = CompilerSourceLocationProjection.Create(diagnostic.Location);
        if (source && string.IsNullOrEmpty(location.Path))
        {
            location.Path = diagnostic.Location.SourceTree?.FilePath ??
                throw new InvalidDataException(
                    "A compiler diagnostic has no source tree path.");
        }
        var result = new CompilerDiagnosticArtifact
        {
            Code = "compiler." + diagnostic.Id,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            IsSource = source,
            Location = location
        };
        if (!source)
        {
            return result;
        }

        CompilerSourceCoordinates.Bind(
            location,
            compilation,
            out var sourceTreeOrdinal,
            out var sourceTreePath,
            out var sourceTreeSha256,
            out var sourceLineMapSha256);
        result.SourceTreeOrdinal = sourceTreeOrdinal;
        result.SourceTreePath = sourceTreePath;
        result.SourceTreeSha256 = sourceTreeSha256;
        result.SourceLineMapSha256 = sourceLineMapSha256;
        return result;
    }
}
