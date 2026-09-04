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
            var lowerer = new CompilerCallableLowerer(
                compilation,
                new IrFactory(),
                specificationPackAuthority,
                snapshot.SyntaxTrees);
            callables = [.. targets.Select(item => {
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken));
                return artifact.AttachEffectEvidence(item, snapshot);
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
        var span = source ? diagnostic.Location.GetMappedLineSpan() : default;
        var location = new WorkerSourceLocation
        {
            Path = source
                ? string.IsNullOrEmpty(span.Path)
                    ? diagnostic.Location.SourceTree?.FilePath ??
                        throw new InvalidDataException(
                            "A compiler diagnostic has no source tree path.")
                    : span.Path
                : string.Empty,
            Start = source ? diagnostic.Location.SourceSpan.Start : 0,
            Length = source ? diagnostic.Location.SourceSpan.Length : 0,
            Line = source ? span.StartLinePosition.Line + 1 : 0,
            Column = source ? span.StartLinePosition.Character + 1 : 0
        };
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
