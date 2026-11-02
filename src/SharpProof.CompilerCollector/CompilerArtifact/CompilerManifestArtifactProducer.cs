// This producer runs only in the build-time compiler collector.
using System.Text.Json;

namespace SharpProof.CompilerCollector;

internal static class CompilerManifestArtifactProducer
{
    internal static CompilerManifestArtifact Create(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, WorkerFeatureSet features, ClaimManifestBuildResult discovery,
        int maximumExpressionDepth, CancellationToken cancellationToken,
        ImmutableArray<AdditionalText> additionalFiles = default,
        ImmutableArray<string> specificationPacks = default,
        long callerSelectionBytes = CompilerManifestArtifactFile.CallerSelectionBytes)
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
            .Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (CreateDiagnostic(item, compilation, snapshot)));
        var diagnosticArtifacts =
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics);
        ImmutableArray<ManifestCallableTarget> targets = [.. discovery.Targets.Values.OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId), StringComparer.Ordinal)];
        var manifest = discovery.Manifest;
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
            ImmutableArray<CompilerCallPreconditionClaims.LoweredCallable> lowered = [.. targets.Concat(discovery.ContractCallers).OrderBy(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId), StringComparer.Ordinal).Select(item => {
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
                return new CompilerCallPreconditionClaims.LoweredCallable(item, lowerer.Prepare(item, cancellationToken));
            })];
            if (features is WorkerFeatureSet.Contracts or WorkerFeatureSet.All)
            {
                (manifest, lowered) = CompilerCallPreconditionClaims.Attach(compilation, snapshot.SyntaxTrees, manifest, lowered,
                    [.. discovery.ContractCallers], discovery.PreconditionCallSites, cancellationToken);
                targets = [.. lowered.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : item.Target)];
            }
            callables = [.. lowered.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : CompilerLoweredArtifact.Encode(item.Preparation, cancellationToken).AttachEffectEvidence(item.Target))];
        }
        var resourceSnapshot = snapshot;
        var artifact = Compose(manifest, callables, targets);
        // Past the cap, callers selected only for their calls leave the
        // artifact, last callable id first, and their calls count as
        // unchecked. Declared claims always stay.
        while (!discovery.ContractCallers.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var excess = JsonSerializer.SerializeToUtf8Bytes(artifact, WorkerProtocolJson.SharedOptions).LongLength + 1 - callerSelectionBytes;
            if (excess <= 0)
            { break; }
            var callOnly = CompilerCallPreconditionClaims.SelectedOnlyForCalls(manifest);
            var dropped = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            for (var index = callables.Length - 1; index >= 0 && excess > 0; index--)
            {
                if (callOnly.Contains(callables[index].CallableId))
                {
                    dropped.Add(callables[index].CallableId);
                    excess -= JsonSerializer.SerializeToUtf8Bytes(callables[index], WorkerProtocolJson.SharedOptions).LongLength;
                }
            }
            if (dropped.Count == 0)
            { break; }
            var removed = dropped.ToImmutable();
            manifest = CompilerCallPreconditionClaims.Drop(manifest, removed, discovery.PreconditionCallSites);
            callables = [.. callables.Where(callable => callable is null ? throw new ArgumentNullException(nameof(callable)) : !removed.Contains(callable.CallableId))];
            targets = [.. targets.Where(target => target is null ? throw new ArgumentNullException(nameof(target)) : !removed.Contains(target.Entry.CallableId))];
            artifact = Compose(manifest, callables, targets);
        }
        return artifact;

        CompilerManifestArtifact Compose(WorkerClaimManifest composedManifest, ImmutableArray<CompilerCallableArtifact> composedCallables,
            ImmutableArray<ManifestCallableTarget> composedTargets)
        {
            composedManifest = RepositoryParameterGuard.NotNull(composedManifest, nameof(composedManifest));
            RepositoryParameterGuard.RequireInitialized(composedCallables.IsDefault, nameof(composedCallables), "Array must be initialized.");
            RepositoryParameterGuard.RequireInitialized(composedTargets.IsDefault, nameof(composedTargets), "Array must be initialized.");
            var composedSnapshot = resourceSnapshot;
            // Preserve only reference evidence actually needed by resource metadata
            // leaves. General compiler reference snapshots remain build-time data.
            var resourceAssemblies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var callable in composedCallables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pending = new Stack<ResourceProgram>();
                if (callable.ResourceProgram is { } payload)
                { pending.Push(ResourceProgramCodec.Decode(payload, cancellationToken)); }
                if (callable.ForkJoinProjection is { } forkPayload)
                {
                    var entry = composedManifest.Callables.Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
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
            if (composedManifest.Claims.Any(static claim => claim is null ? throw new ArgumentNullException(nameof(claim)) :
                claim.ResourceUpperBound?.Metric is WorkerResourceMetric.AllocatedBytes or WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes))
            {
                composedSnapshot = composedSnapshot with { ResourceTargetPlatform = compilation.Options.Platform.ToString() };
                resourceAssemblies.Add(compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly.Identity.ToString());
            }
            composedSnapshot = composedSnapshot with
            {
                ResourceMetadataReferences = composedSnapshot.References.Where(reference =>
                reference is null ? throw new ArgumentNullException(nameof(reference)) : resourceAssemblies.Contains(reference.Identity)).ToImmutableArray()
            };
            return new CompilerManifestArtifact
            {
                SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds],
                SpecificationPackCatalogVersion =
                    specificationPackAuthority.SpecificationPackCatalogVersion,
                SpecificationPackCatalogSha256 =
                    specificationPackAuthority.SpecificationPackCatalogSha256,
                Features = features,
                Compilation = composedSnapshot,
                Manifest = composedManifest,
                MaximumExpressionDepth = maximumExpressionDepth,
                CompilerDiagnostics = diagnosticArtifacts,
                Callables = composedCallables,
                ReachableSource = diagnosticArtifacts.Length == 0
                    ? CompilerReachableSourceCollector.Collect(compilation, composedTargets, composedSnapshot.SyntaxTrees, specificationPackAuthority, cancellationToken)
                    : null
            };
        }
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
        artifact = artifact with { EffectClaims = evidence.ToImmutable() };
        if (artifact.EffectClaims.Any(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ContractKind == WorkerEffectContractKind.ResourceUpperBound) &&
            artifact.ResourceProgram is null && artifact.ResourceUnknownReason is null)
        { artifact = artifact with { ResourceUnknownReason = "CompilationOrBodyUnavailable" }; }
        return artifact;
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(
        Diagnostic diagnostic,
        CSharpCompilation sourceCompilation,
        CompilerCompilationSnapshot compilation)
    {
        sourceCompilation = RepositoryParameterGuard.NotNull(sourceCompilation, nameof(sourceCompilation));
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
            if (diagnostic.Location.SourceTree is { } sourceTree)
            {
                var ordinal = sourceCompilation.SyntaxTrees.IndexOf(sourceTree);
                if (ordinal >= 0)
                {
                    CompilerSourceCoordinates.RememberTree(location, ordinal);
                }
            }
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
