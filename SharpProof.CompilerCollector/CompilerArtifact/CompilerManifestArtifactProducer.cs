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
                return artifact.AttachEffectEvidence(item, snapshot);
            })];
        }
        else
        {
            var summaryAuthorities =
                ImmutableArray.CreateBuilder<CompilerSummaryEvidenceAuthority>();
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
                    snapshot.SyntaxTrees);
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken));
                summaryAuthorities.AddRange(lowerer.SummaryEvidenceAuthorities);
                return artifact.AttachEffectEvidence(item, snapshot);
            })];
            var canonicalAuthorities = summaryAuthorities
                .GroupBy(static authority => (
                    authority.Origin,
                    authority.CallIdentity,
                    authority.EvidenceIdentity,
                    authority.EvidenceSha256))
                .Select(static group => group.First())
                .OrderBy(static authority => (int)authority.Origin)
                .ThenBy(static authority => authority.CallIdentity, StringComparer.Ordinal)
                .ThenBy(static authority => authority.EvidenceIdentity, StringComparer.Ordinal)
                .ThenBy(static authority => authority.EvidenceSha256, StringComparer.Ordinal)
                .ToImmutableArray();
            snapshot.SummaryEvidence = BuildSummaryEvidence(
                snapshot,
                canonicalAuthorities);
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
                snapshot, diagnosticArtifacts, maximumExpressionDepth),
            Compilation = snapshot,
            Manifest = discovery.Manifest,
            MaximumExpressionDepth = maximumExpressionDepth,
            LocationAuthorities = CreateLocationAuthorities(
                discovery.Manifest,
                snapshot),
            CompilerDiagnostics = diagnosticArtifacts,
            Callables = callables
        };
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);
        CompilerManifestArtifactJson.Validate(artifact);
        return artifact;
    }

    private static CompilerCallableArtifact AttachEffectEvidence(
        this CompilerCallableArtifact artifact,
        ManifestCallableTarget target,
        CompilerCompilationSnapshot snapshot)
    {
        var claims = target.EffectClaims;
        var evidence = new CompilerEffectClaimArtifact[claims.Length];
        var authorities = new CompilerEffectAuthorityArtifact[claims.Length];
        for (var index = 0; index < claims.Length; index++)
        {
            var claim = claims[index];
            CompilerEffectAuthority.BindSourceTree(
                claim.Authority,
                snapshot);
            evidence[index] = claim.Evidence;
            authorities[index] = claim.Authority;
        }
        artifact.EffectClaims = evidence;
        artifact.EffectAuthorities = authorities;
        return artifact;
    }

    private static CompilerSummaryEvidenceSnapshot[] BuildSummaryEvidence(
        CompilerCompilationSnapshot snapshot,
        ImmutableArray<CompilerSummaryEvidenceAuthority> authorities)
    {
        Dictionary<(string Path, string Sha256), CompilerSyntaxTreeSnapshot[]>?
            syntaxTreesByIdentity = null;
        Dictionary<(string Name, string Sha256), CompilerReferenceModuleSnapshot[]>?
            modulesByIdentity = null;
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
                syntaxTreesByIdentity ??= snapshot.SyntaxTrees
                    .GroupBy(static tree => (tree.Path, tree.Sha256))
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.ToArray());
                if (!syntaxTreesByIdentity.TryGetValue(
                        (authority.SourcePath, authority.SourceTreeSha256),
                        out var matchingTrees) ||
                    !matchingTrees.Any(tree =>
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
                modulesByIdentity ??= snapshot.References
                    .SelectMany(static reference => reference.Modules)
                    .GroupBy(static module => (module.Name, module.Sha256))
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.ToArray());
                if (!modulesByIdentity.TryGetValue(
                        (authority.OwningModuleName, authority.EvidenceSha256),
                        out var matchingModules) ||
                    matchingModules.Length == 0)
                {
                    throw new InvalidOperationException(
                        "An IL summary authority is not bound to a captured module.");
                }

                var selectedModule = matchingModules
                    .OrderBy(static module => module.Path, StringComparer.Ordinal)
                    .First();
                if (matchingModules.Any(module =>
                        !string.Equals(
                            module.Mvid,
                            selectedModule.Mvid,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "An IL summary authority is bound to conflicting captured modules.");
                }

                row.OwningModuleMvid = selectedModule.Mvid;
                row.OwningModuleSha256 = selectedModule.Sha256;
            }
            else if (authority.Origin == CompilerSummaryOrigin.SpecificationPack)
            {
                if (authority.EvidenceSha256 != snapshot.SpecificationPackCatalogSha256 ||
                        !CompilerSpecificationPackAuthorityValidation.IsValidPackIdentity(
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

    private static CompilerLocationAuthorityArtifact[] CreateLocationAuthorities(
        WorkerClaimManifest manifest,
        CompilerCompilationSnapshot compilation)
    {
        return [
            .. manifest.Callables
                .Select(entry => CompilerSourceLocationAuthority.CreateAuthority(
                    CompilerSourceLocationOwnerKind.Callable,
                    entry.CallableId,
                    entry.Location,
                    compilation))
                .Concat(manifest.Claims.Select(entry =>
                    CompilerSourceLocationAuthority.CreateAuthority(
                        CompilerSourceLocationOwnerKind.Claim,
                        entry.ClaimId,
                        entry.Location,
                        compilation)))
                .OrderBy(static value => value.OwnerKind)
                .ThenBy(static value => value.OwnerId, StringComparer.Ordinal)
        ];
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

        CompilerSourceLocationAuthority.Bind(
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
