// This producer runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal static class CompilerManifestArtifactProducer
{
    private static readonly char[] PackIdentitySeparators = [';'];

    internal static CompilerManifestArtifact Create(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, WorkerFeatureSet features, ClaimManifestBuildResult discovery,
        int maximumExpressionDepth, CancellationToken cancellationToken,
        ImmutableArray<AdditionalText> additionalFiles = default,
        ImmutableArray<string> specificationPacks = default)
    {
        var specificationPackAuthority =
            CompilerSpecificationPackProvider.ResolveAuthority(
                specificationPacks.IsDefault ? [] : specificationPacks);
        var sourceTrees = compilation.SyntaxTrees.ToArray();
        var snapshot = CompilerCompilationCapture.Capture(
            compilation, projectDirectory, targetFramework, additionalFiles, cancellationToken);
        snapshot.SpecificationPackIds = [.. specificationPackAuthority.SpecificationPackIds];
        snapshot.SpecificationPackCatalogVersion =
            specificationPackAuthority.SpecificationPackCatalogVersion;
        snapshot.SpecificationPackCatalogSha256 =
            specificationPackAuthority.SpecificationPackCatalogSha256;
        var locationValidation =
            new CompilerSourceLocationAuthority.ValidationContext();
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item.Severity == DiagnosticSeverity.Error)
            .Select(item => CreateDiagnostic(
                item,
                sourceTrees,
                snapshot,
                locationValidation));
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
            LocationAuthorities = CreateLocationAuthorities(
                discovery,
                sourceTrees,
                snapshot,
                locationValidation),
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
                if (module.Length == 0 ||
                    module.Any(candidate => candidate.Mvid != module[0].Mvid))
                {
                    throw new InvalidOperationException(
                        "An IL summary authority is not bound to one consistent captured module.");
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
                .Split(PackIdentitySeparators, StringSplitOptions.RemoveEmptyEntries)
                .Contains(identity, StringComparer.Ordinal))
        {
            return false;
        }

        var separator = identity.LastIndexOf('@');
        return separator > 0 && selectedPackIds.Contains(
            identity.Substring(0, separator),
            StringComparer.Ordinal);
    }

    private static CompilerLocationAuthorityArtifact[] CreateLocationAuthorities(
        ClaimManifestBuildResult discovery,
        IReadOnlyList<SyntaxTree> sourceTrees,
        CompilerCompilationSnapshot compilation,
        CompilerSourceLocationAuthority.ValidationContext locationValidation)
    {
        return [
            .. discovery.Targets.Values.Select(target =>
                CreateLocationAuthority(
                    CompilerSourceLocationOwnerKind.Callable,
                    target.Entry.CallableId,
                    target.Entry.Location,
                    FindSourceTreeOrdinal(
                        target.Declaration?.SyntaxTree ??
                        target.Method.Locations.FirstOrDefault(
                            static location => location.IsInSource)?.SourceTree,
                        sourceTrees),
                    compilation,
                    locationValidation))
                .Concat(discovery.Targets.Values.SelectMany(target =>
                    target.Claims.Select(claim =>
                        CreateLocationAuthority(
                            CompilerSourceLocationOwnerKind.Claim,
                            claim.Entry.ClaimId,
                            claim.Entry.Location,
                            FindSourceTreeOrdinal(
                                claim.SourceOperation?.Syntax.SyntaxTree ??
                                claim.SourceAttribute?.ApplicationSyntaxReference?.SyntaxTree ??
                                target.Declaration?.SyntaxTree,
                                sourceTrees),
                            compilation,
                            locationValidation))))
                .Concat(discovery.Targets.Values.SelectMany(target =>
                    target.EffectClaims.Select(claim =>
                        CreateLocationAuthority(
                            CompilerSourceLocationOwnerKind.Claim,
                            claim.Entry.ClaimId,
                            claim.Entry.Location,
                            FindSourceTreeOrdinalByPath(
                                claim.Authority.SourceTreePath,
                                sourceTrees),
                            compilation,
                            locationValidation))))
                .OrderBy(static value => value.OwnerKind)
                .ThenBy(static value => value.OwnerId, StringComparer.Ordinal)
        ];
    }

    private static CompilerLocationAuthorityArtifact CreateLocationAuthority(
        CompilerSourceLocationOwnerKind ownerKind,
        string ownerId,
        WorkerSourceLocation location,
        int sourceTreeOrdinal,
        CompilerCompilationSnapshot compilation,
        CompilerSourceLocationAuthority.ValidationContext locationValidation)
    {
        return sourceTreeOrdinal >= 0
            ? CompilerSourceLocationAuthority.CreateAuthority(
                ownerKind,
                ownerId,
                location,
                compilation,
                sourceTreeOrdinal,
                locationValidation)
            : CompilerSourceLocationAuthority.CreateAuthority(
                ownerKind,
                ownerId,
                location,
                compilation,
                locationValidation);
    }

    private static CompilerDiagnosticArtifact CreateDiagnostic(
        Diagnostic diagnostic,
        IReadOnlyList<SyntaxTree> sourceTrees,
        CompilerCompilationSnapshot compilation,
        CompilerSourceLocationAuthority.ValidationContext locationValidation)
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

        var sourceTreeOrdinal = FindSourceTreeOrdinal(
            diagnostic.Location.SourceTree,
            sourceTrees);
        string sourceTreePath;
        string sourceTreeSha256;
        string sourceLineMapSha256;
        if (sourceTreeOrdinal >= 0)
        {
            CompilerSourceLocationAuthority.Bind(
                location,
                compilation,
                sourceTreeOrdinal,
                out sourceTreePath,
                out sourceTreeSha256,
                out sourceLineMapSha256,
                locationValidation);
        }
        else
        {
            CompilerSourceLocationAuthority.Bind(
                location,
                compilation,
                out sourceTreeOrdinal,
                out sourceTreePath,
                out sourceTreeSha256,
                out sourceLineMapSha256,
                locationValidation);
        }
        result.SourceTreeOrdinal = sourceTreeOrdinal;
        result.SourceTreePath = sourceTreePath;
        result.SourceTreeSha256 = sourceTreeSha256;
        result.SourceLineMapSha256 = sourceLineMapSha256;
        return result;
    }

    private static int FindSourceTreeOrdinal(
        SyntaxTree? sourceTree,
        IReadOnlyList<SyntaxTree> sourceTrees)
    {
        if (sourceTree == null)
        {
            return -1;
        }

        for (var index = 0; index < sourceTrees.Count; index++)
        {
            if (ReferenceEquals(sourceTree, sourceTrees[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindSourceTreeOrdinalByPath(
        string? sourceTreePath,
        IReadOnlyList<SyntaxTree> sourceTrees)
    {
        if (sourceTreePath is not { } candidatePath ||
            string.IsNullOrWhiteSpace(candidatePath))
        {
            return -1;
        }

        var normalizedPath = CompilerCaptureAuthority.NormalizePath(
            candidatePath);
        var ordinal = -1;
        for (var index = 0; index < sourceTrees.Count; index++)
        {
            if (sourceTrees[index].FilePath is not { } sourcePath)
            {
                continue;
            }

            if (!string.Equals(
                    CompilerCaptureAuthority.NormalizePath(sourcePath),
                    normalizedPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (ordinal >= 0)
            {
                return -1;
            }

            ordinal = index;
        }

        return ordinal;
    }
}
