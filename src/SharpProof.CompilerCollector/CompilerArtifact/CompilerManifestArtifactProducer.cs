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
        snapshot.SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds];
        snapshot.SpecificationPackCatalogVersion =
            specificationPackAuthority.SpecificationPackCatalogVersion;
        snapshot.SpecificationPackCatalogSha256 =
            specificationPackAuthority.SpecificationPackCatalogSha256;
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Severity == DiagnosticSeverity.Error &&
                !item.IsSuppressed))
            .Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (CreateDiagnostic(item, snapshot)));
        var diagnosticArtifacts =
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics);
        var targets = discovery.Targets.Values.OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId), StringComparer.Ordinal);
        CompilerCallableArtifact[] callables;
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
        // Preserve only reference evidence actually needed by resource metadata
        // leaves. General compiler reference snapshots remain build-time data.
        var resourceAssemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var callable in callables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = new Stack<ResourceProgram>();
            if (callable.ResourceProgram is { } payload)
            { pending.Push(ResourceProgramCodec.Decode(payload, cancellationToken)); }
            if (callable.ForkJoinProjection is { } forkPayload)
            {
                var entry = discovery.Manifest.Callables.Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
                    candidate.CallableId == callable.CallableId);
                var root = CompilerTotalCallableArtifactCodec.DecodeEntry(callable.TotalEntry, entry, cancellationToken)
                    ?? throw new InvalidDataException("Fork-join reference ownership has no root entry.");
                var projection = CompilerForkJoinProjectionCodec.Decode(forkPayload, root, cancellationToken);
                resourceAssemblies.Add(projection.FrameworkAssemblyIdentity);
                foreach (var child in projection.Children)
                { pending.Push(child.Cost); }
            }
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var program = pending.Pop();
                if (program.Metadata is { } reference)
                { resourceAssemblies.Add(reference.AssemblyIdentity); }
                foreach (var child in program.Children)
                { pending.Push(child); }
            }
        }
        if (discovery.Manifest.Claims.Any(static claim => claim is null ? throw new ArgumentNullException(nameof(claim)) :
            claim.ResourceUpperBound?.Metric is WorkerResourceMetric.AllocatedBytes or WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes))
        {
            snapshot.ResourceTargetPlatform = compilation.Options.Platform.ToString();
            resourceAssemblies.Add(compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly.Identity.ToString());
        }
        snapshot.ResourceMetadataReferences = snapshot.References.Where(reference =>
            reference is null ? throw new ArgumentNullException(nameof(reference)) : resourceAssemblies.Contains(reference.Identity)).ToArray();
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
        var evidence = new CompilerEffectClaimArtifact[claims.Length];
        for (var index = 0; index < claims.Length; index++)
        {
            var claim = claims[index];
            evidence[index] = claim.Evidence;
        }
        artifact.EffectClaims = evidence;
        if (evidence.Any(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ContractKind == WorkerEffectContractKind.ResourceUpperBound) &&
            artifact.ResourceProgram is null && artifact.ResourceUnknownReason is null)
        { artifact.ResourceUnknownReason = "CompilationOrBodyUnavailable"; }
        return artifact;
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(
        Diagnostic diagnostic,
        CompilerCompilationSnapshot compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        diagnostic = RepositoryParameterGuard.NotNull(diagnostic, nameof(diagnostic));
        var source = diagnostic.Location.IsInSource;
        var location = CompilerSourceLocationProjection.Create(diagnostic.Location);
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
