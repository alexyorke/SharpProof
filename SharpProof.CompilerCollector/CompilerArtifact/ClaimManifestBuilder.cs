using SharpProof.Analyzer;

// This builder runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal sealed partial class ClaimManifestBuilder(
    CSharpCompilation compilation,
    WorkerFeatureSet enabledFeatures = WorkerFeatureSet.All,
    CancellationToken cancellationToken = default)
{
    private readonly CSharpCompilation _compilation =
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
    private readonly ContractClauseInventoryBuilder _clauses =
        ContractClauseInventoryBuilder.ForCompilation(compilation);
    private readonly ContractSelectionInventory _attributes =
        ContractSelectionInventory.ForCompilation(compilation);
    private readonly EffectiveContractSourceResolver _contractSources =
        EffectiveContractSourceResolver.ForCompilation(compilation);
    private readonly AnalyzerSession _effectSession =
        new(compilation, AnalyzerConfiguration.AdvisoryAll, cancellationToken);

    internal ClaimManifestBuildResult Build()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovered = DiscoverMethods().Select(CreateSeed).ToImmutableArray();
        var ids = CreateCallableIds(discovered);
        var targets = ImmutableDictionary.CreateBuilder<IMethodSymbol, ManifestCallableTarget>(
            SymbolEqualityComparer.Default);
        foreach (var seed in discovered.OrderBy(seed => ids[seed.Method], StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BuildTarget(seed, ids[seed.Method]) is { } target)
            {
                targets.Add(seed.Method, target);
            }
        }
        var ordered = targets.Values
            .OrderBy(static target => target.Entry.CallableId, StringComparer.Ordinal)
            .ToImmutableArray();
        var manifest = new WorkerClaimManifest
        {
            Callables = [.. ordered.Select(static target => target.Entry)],
            Claims = [.. ordered.SelectMany(static target =>
                target.Claims.Select(static claim => claim.Entry)
                    .Concat(target.EffectClaims.Select(static claim => claim.Entry)))]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return new ClaimManifestBuildResult(manifest, targets.ToImmutable());
    }

    private ManifestCallableTarget? BuildTarget(CallableSeed seed, string callableId)
    {
        var target = seed.Method;
        if (SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
                target,
                _effectSession,
                static _ => { },
                cancellationToken))
        {
            return null;
        }

        var resolution = _contractSources.Resolve(target);
        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var postconditions = CreatePostconditions(
            target, source, inventory, usesCompanion, callableId);
        var trustedAttributes = TrustedAttributes(target).ToImmutableArray();
        var selected = SelectFeatures(target, resolution, trustedAttributes);
        var assumptions = CreateAssumptions(
            target,
            source,
            inventory,
            usesCompanion,
            callableId,
            trustedAttributes);
        if (postconditions.IsDefaultOrEmpty && selected.IsDefaultOrEmpty && assumptions.IsDefaultOrEmpty)
        {
            return null;
        }

        var analyzerSelection = _attributes.Select(
            target,
            resolution.HasSelectedContractIntent);
        var analyzerContractsSelected =
            ContractsEnabled &&
            (analyzerSelection &
             ContractSelectionFeatures.Contracts) != 0;
        var analyzerEffectsSelected =
            EffectsEnabled &&
            (analyzerSelection &
             ContractSelectionFeatures.Effects) != 0;
        var selectedSubset =
            analyzerContractsSelected ||
            analyzerEffectsSelected
            ? ClassifySelectedSubset(
                seed,
                analyzerContractsSelected,
                analyzerEffectsSelected)
            : LanguageSubsetDecision.Supported;
        var supported =
            seed.Declaration is
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax &&
            target.MethodKind is
                MethodKind.Ordinary or
                MethodKind.Constructor or
                MethodKind.ExplicitInterfaceImplementation &&
            selectedSubset.IsSupported;
        var location = CallableLocation(target, seed.Declaration);
        var effects = EffectsEnabled
            ? CreateEffectClaims(
                EffectContractDiagnostics.Evaluate(
                    target, location, _effectSession, static _ => { }, cancellationToken),
                target, callableId, postconditions.Length, supported)
            : [];
        var features = new HashSet<WorkerSelectedFeature>(selected);
        if (!postconditions.IsDefaultOrEmpty ||
            assumptions.Any(static evidence => evidence.Kind == WorkerAssumptionKind.UserAssume))
        {
            features.Add(WorkerSelectedFeature.Contracts);
        }

        if (!effects.IsDefaultOrEmpty)
        {
            features.Add(WorkerSelectedFeature.Effects);
        }

        var reasons = ImmutableArray.CreateBuilder<WorkerSelectionReason>(2);
        if (!selected.IsDefaultOrEmpty || !assumptions.IsDefaultOrEmpty)
        {
            reasons.Add(WorkerSelectionReason.ExplicitAnnotation);
        }

        if (!postconditions.IsDefaultOrEmpty)
        {
            reasons.Add(WorkerSelectionReason.DiscoveredPostcondition);
        }

        var entry = new WorkerCallableManifestEntry
        {
            CallableId = callableId,
            SelectedFeatures = [.. features.OrderBy(static feature => feature)],
            SelectionReasons = reasons.ToArray(),
            Location = location.IsInSource
                ? ToSourceLocation(location)
                : postconditions.FirstOrDefault()?.Entry.Location ?? new WorkerSourceLocation(),
            ClaimIds = [.. postconditions.Select(static claim => claim.Entry.ClaimId),
                .. effects.Select(static claim => claim.Entry.ClaimId)],
            Assumptions = assumptions.ToArray()
        };
        return new ManifestCallableTarget(target, seed.Declaration, seed.Model,
            entry, postconditions, effects, supported);
    }

    private LanguageSubsetDecision ClassifySelectedSubset(
        CallableSeed seed,
        bool contractsSelected,
        bool effectsSelected)
    {
        if ((seed.Method.IsAbstract || seed.Method.IsExtern) &&
            effectsSelected &&
            !contractsSelected &&
            _effectSession.ResolveEffectContract(seed.Method).Kind ==
            EffectContractResolutionKind.Valid)
        {
            return LanguageSubsetDecision.Supported;
        }

        if (seed.Declaration == null || seed.Model == null)
        {
            return LanguageSubsetDecision.Abstain(
                LanguageSubsetAbstentionReason.UnsupportedCallable);
        }

        return LanguageSubsetGate.ClassifyEffects(
            seed.Method,
            seed.Declaration,
            seed.Model,
            [],
            _effectSession.HasResolvedApiSpec,
            cancellationToken);
    }

    private ImmutableArray<ManifestClaim> CreatePostconditions(
        IMethodSymbol target,
        IMethodSymbol source,
        ContractClauseInventory inventory,
        bool usesCompanion,
        string callableId)
    {
        if (!ContractsEnabled)
        {
            return [];
        }

        var candidates = inventory.Clauses
            .Where(static clause => clause is
            {
                Kind: BoundContractKind.Ensures,
                Placement: not ContractClausePlacement.NestedCallable
            })
            .Select(clause => new ClaimCandidate(
                SemanticClaimIdentity.CreateInvocationFingerprint(
                    clause.Invocation, target, source, usesCompanion),
                clause.Location,
                usesCompanion
                    ? WorkerClaimEvidence.CompanionClause
                    : WorkerClaimEvidence.DirectClause,
                clause.Invocation, null, clause.Placement))
            .Concat(target.GetReturnTypeAttributes()
                .Where(_attributes.IsClosedContract)
                .OrderBy(
                    static attribute =>
                        attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath ?? string.Empty,
                    StringComparer.Ordinal)
                .ThenBy(static attribute =>
                    attribute.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue)
                .Select(attribute => new ClaimCandidate(
                    SemanticClaimIdentity.CreateAttributeFingerprint(attribute, target),
                    AttributeLocation(attribute, target),
                    WorkerClaimEvidence.ReturnAttribute,
                    null, attribute, null)))
            .ToImmutableArray();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var claims = ImmutableArray.CreateBuilder<ManifestClaim>(candidates.Length);
        for (var ordinal = 0; ordinal < candidates.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[ordinal];
            var rank = NextRank(ranks, candidate.Fingerprint);
            claims.Add(new ManifestClaim(new WorkerClaimManifestEntry
            {
                ClaimId = SemanticClaimIdentity.Create(
                    AssemblyName, callableId, candidate.Fingerprint, rank),
                CallableId = callableId,
                Ordinal = ordinal,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = candidate.Evidence,
                Location = ToSourceLocation(candidate.Location)
            }, candidate.Operation, candidate.Attribute, candidate.Placement));
        }
        return claims.MoveToImmutable();
    }

    private ImmutableArray<WorkerSelectedFeature> SelectFeatures(
        IMethodSymbol method,
        EffectiveContractSourceResolution resolution,
        ImmutableArray<(ISymbol Scope, AttributeData Attribute)> trustedAttributes)
    {
        var selected = _attributes.Select(
            method,
            resolution.HasSelectedContractIntent,
            !trustedAttributes.IsDefaultOrEmpty);
        var result = ImmutableArray.CreateBuilder<WorkerSelectedFeature>(2);
        if (EffectsEnabled && (selected & ContractSelectionFeatures.Effects) != 0)
        {
            result.Add(WorkerSelectedFeature.Effects);
        }

        if (ContractsEnabled && (selected & ContractSelectionFeatures.Contracts) != 0)
        {
            result.Add(WorkerSelectedFeature.Contracts);
        }

        return result.ToImmutable();
    }

    private ImmutableArray<WorkerAssumptionEvidence> CreateAssumptions(
        IMethodSymbol target,
        IMethodSymbol source,
        ContractClauseInventory inventory,
        bool usesCompanion,
        string callableId,
        ImmutableArray<(ISymbol Scope, AttributeData Attribute)> trustedAttributes)
    {
        var candidates = ImmutableArray.CreateBuilder<AssumptionCandidate>();
        if (ContractsEnabled)
        {
            foreach (var clause in inventory.Clauses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clause.Kind is not (BoundContractKind.Requires or BoundContractKind.Assume) ||
                    clause.Placement == ContractClausePlacement.NestedCallable)
                {
                    continue;
                }

                candidates.Add(new AssumptionCandidate(
                    clause.Kind == BoundContractKind.Requires
                        ? WorkerAssumptionKind.Precondition
                        : WorkerAssumptionKind.UserAssume,
                    SemanticClaimIdentity.CreateInvocationFingerprint(
                        clause.Invocation, target, source, usesCompanion)));
            }
            foreach (var parameter in target.Parameters)
            {
                foreach (var attribute in parameter.GetAttributes().Where(_attributes.IsClosedContract))
                {
                    candidates.Add(new AssumptionCandidate(
                        WorkerAssumptionKind.Precondition,
                        SemanticClaimIdentity.CreateAttributeFingerprint(attribute, target, parameter)));
                }
            }
        }
        foreach (var (scope, attribute) in trustedAttributes)
        {
            candidates.Add(new AssumptionCandidate(
                WorkerAssumptionKind.TrustedBoundary,
                SemanticClaimIdentity.CreateTrustedFingerprint(attribute, scope, target)));
        }

        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        return [.. candidates.Select(candidate => {
            cancellationToken.ThrowIfCancellationRequested();
            var key = candidate.Kind + ":" + candidate.Fingerprint;
            return new WorkerAssumptionEvidence {
                Id = SemanticClaimIdentity.CreateAssumption(
                    AssemblyName, callableId, candidate.Kind, candidate.Fingerprint, NextRank(ranks, key)),
                Kind = candidate.Kind,
                Used = false
            };
        })];
    }

    private ImmutableArray<ManifestEffectClaim> CreateEffectClaims(
        ImmutableArray<EffectClaimEvaluation> evaluations,
        IMethodSymbol method,
        string callableId,
        int ordinalOffset,
        bool isSupported)
    {
        var claims = ImmutableArray.CreateBuilder<ManifestEffectClaim>();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var evaluation in evaluations.OrderBy(static evaluation => evaluation.Kind))
        {
            foreach (var attribute in evaluation.Attributes
                .OrderBy(
                    static attribute =>
                        attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath ?? string.Empty,
                    StringComparer.Ordinal)
                .ThenBy(static attribute =>
                    attribute.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fingerprint = "effect:" + evaluation.Kind + ":" +
                    SemanticClaimIdentity.CreateAttributeFingerprint(attribute, method);
                var claimId = SemanticClaimIdentity.Create(
                    AssemblyName, callableId, fingerprint, NextRank(ranks, fingerprint));
                var entry = new WorkerClaimManifestEntry
                {
                    ClaimId = claimId,
                    CallableId = callableId,
                    Ordinal = ordinalOffset + claims.Count,
                    Kind = WorkerClaimKind.Effect,
                    Evidence = WorkerClaimEvidence.Attribute,
                    EffectContractKind =
                        CompilerEffectEvaluationWireMappings.ToWorker(
                            evaluation.Kind),
                    Location = ToSourceLocation(AttributeLocation(attribute, method))
                };
                var evidence = CreateEffectEvidence(
                    claimId, evaluation, isSupported);
                CompilerEffectClaimArtifactCodec.Seal(evidence);
                var sourceTreePath = attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath;
                claims.Add(new ManifestEffectClaim(
                    entry,
                    evidence,
                    CompilerEffectAuthority.Create(
                        entry,
                        evidence,
                        sourceTreePath)));
            }
        }

        return claims.ToImmutable();
    }

    private CompilerEffectClaimArtifact CreateEffectEvidence(
        string claimId,
        EffectClaimEvaluation evaluation,
        bool isSupported)
    {
        var evidence = new CompilerEffectClaimArtifact
        {
            ClaimId = claimId,
            ContractKind =
                CompilerEffectEvaluationWireMappings.ToWorker(
                    evaluation.Kind),
            Outcome =
                CompilerEffectEvaluationWireMappings.ToWorker(
                    evaluation.Outcome),
            Reason =
                CompilerEffectEvaluationWireMappings.ToWorker(
                    evaluation.Reason),
            Certainty =
                CompilerEffectEvaluationWireMappings.ToWorker(
                    evaluation.Certainty),
            Constraint = new CompilerEffectConstraintArtifact
            {
                AllowedEffects = ToWorkerEffects(evaluation.Constraint.Effects),
                AllowedCapabilities = ToWorkerCapabilities(evaluation.Constraint.Capabilities),
                AllowedExceptionTypes = [.. evaluation.Constraint.ExceptionTypes
                    .Select(CompilerExceptionTypeIdentity.Encode)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static value => value, StringComparer.Ordinal)]
            },
            Evidence = evaluation.Evidence
        };
        if (!isSupported)
        {
            MarkUnavailable(evidence, WorkerClaimReason.UnsupportedContract);
            return evidence;
        }

        if (evidence.Outcome != WorkerClaimOutcome.Refuted)
        {
            return evidence;
        }

        if (evidence.Reason != WorkerClaimReason.None ||
            evidence.Certainty !=
            WorkerEffectEvidenceCertainty.DefiniteViolation ||
            evaluation.Witness is not { } witness ||
            !CompilerEffectReplayLowerer.TryCreate(
                _compilation,
                _effectSession.ApiSpecs,
                witness,
                ToSourceLocation(witness.Origin.Syntax.GetLocation()),
                cancellationToken,
            out var replay,
            out var witnessDetail))
        {
            MarkUnavailable(evidence, WorkerClaimReason.CounterexampleNotReplayable);
            return evidence;
        }

        evidence.Witness = new WorkerEffectViolationWitness
        {
            Kind = witness.Kind,
            Detail = witnessDetail,
            Effects = ToWorkerEffects(witness.Effects),
            Capabilities =
                ToWorkerCapabilities(witness.Capabilities),
            ExactExceptionTypeHierarchy =
                [.. replay!.Events[0].ExactExceptionTypeHierarchy],
            Location = replay!.Events[0].Location
        };
        evidence.Replay = replay;
        return evidence;
    }

    private static void MarkUnavailable(
        CompilerEffectClaimArtifact evidence,
        WorkerClaimReason reason)
    {
        evidence.Outcome = WorkerClaimOutcome.Unknown;
        evidence.Reason = reason;
        evidence.Certainty = WorkerEffectEvidenceCertainty.Unavailable;
        evidence.Witness = null;
        evidence.Replay = null;
    }

    private IEnumerable<(ISymbol Scope, AttributeData Attribute)> TrustedAttributes(
        IMethodSymbol method)
    {
        foreach (var scope in CompilerMethodScopes.Enumerate(method))
        {
            foreach (var attribute in scope.GetAttributes())
            {
                if (ContractSelectionInventory.Is(attribute, _attributes.Trusted))
                {
                    yield return (scope, attribute);
                }
            }
        }
    }

    private ImmutableArray<IMethodSymbol> DiscoverMethods()
    {
        var methods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, tree);
            foreach (var node in tree.GetRoot(cancellationToken).DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (node)
                {
                    case TypeDeclarationSyntax type
                        when PrimaryConstructorCallableInventory.TryGet(
                            type,
                            model,
                            cancellationToken,
                            out var primaryConstructor):
                        Add(primaryConstructor);
                        break;
                    case BaseMethodDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case LocalFunctionStatementSyntax:
                        Add(model.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol);
                        break;
                    case AnonymousFunctionExpressionSyntax anonymous:
                        Add((model.GetOperation(anonymous, cancellationToken) as IAnonymousFunctionOperation)?.Symbol);
                        break;
                    case GlobalStatementSyntax global:
                        Add(model.GetEnclosingSymbol(global.SpanStart, cancellationToken) as IMethodSymbol);
                        break;
                    case BasePropertyDeclarationSyntax property:
                        AddAccessors(model.GetDeclaredSymbol(property, cancellationToken));
                        break;
                    case EventFieldDeclarationSyntax eventField:
                        foreach (var variable in eventField.Declaration.Variables)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            AddAccessors(model.GetDeclaredSymbol(variable, cancellationToken));
                        }
                        break;
                }
            }
        }
        foreach (var companion in _contractSources.Companions)
        {
            foreach (var method in ContractForSymbolMatcher.GetOrdinaryMethods(companion.Target))
            {
                cancellationToken.ThrowIfCancellationRequested();
                methods.Add(ContractClauseInventoryBuilder.NormalizeCallable(method));
            }
        }

        return methods.ToImmutableArray();

        void Add(IMethodSymbol? method)
        {
            if (method != null &&
                !ContractForSymbolMatcher.IsCompanionType(
                    _contractSources.Companions,
                    method.ContainingType))
            {
                methods.Add(ContractClauseInventoryBuilder.NormalizeCallable(method));
            }
        }
        void AddAccessors(ISymbol? symbol)
        {
            if (symbol is IPropertySymbol property)
            {
                Add(property.GetMethod);
                Add(property.SetMethod);
            }
            else if (symbol is IEventSymbol @event)
            {
                Add(@event.AddMethod);
                Add(@event.RemoveMethod);
                Add(@event.RaiseMethod);
            }
        }
    }

    private CallableSeed CreateSeed(IMethodSymbol method)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var declaration = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OrderBy(static syntax => syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static syntax => syntax.SpanStart)
            .FirstOrDefault();
        var model = declaration == null ? null :
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, declaration.SyntaxTree);
        return new CallableSeed(ContractClauseInventoryBuilder.NormalizeCallable(method), declaration, model);
    }

    private ImmutableDictionary<IMethodSymbol, string> CreateCallableIds(
        ImmutableArray<CallableSeed> callables)
    {
        var ordinals = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        foreach (var group in callables
                     .Where(static seed => seed.Method.MethodKind is
                         MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                     // Callables without contract clauses do not participate in
                     // the manifest identity. Excluding them keeps an unrelated
                     // sibling from renumbering the callables that do.
                     .Where(static seed => seed.Declaration?.ToString()
                         .IndexOf("Contract.", StringComparison.Ordinal) >= 0)
                     .GroupBy(static seed => seed.Method.ContainingSymbol!,
                         SymbolEqualityComparer.Default))
        {
            foreach (var item in group
                         .OrderBy(seed => seed.Declaration == null
                             ? int.MaxValue
                              : _clauses.GetTreeOrdinal(seed.Declaration.SyntaxTree))
                         .ThenBy(static seed => seed.Declaration?.SpanStart ?? int.MaxValue)
                         .Select(static (seed, ordinal) => (seed, ordinal)))
            {
                ordinals.Add(item.seed.Method, item.ordinal);
            }
        }

        var ids = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var seed in callables)
        {
            Resolve(seed.Method);
        }

        return ids.ToImmutableDictionary(SymbolEqualityComparer.Default);

        void Resolve(IMethodSymbol method)
        {
            var unresolved = new Stack<IMethodSymbol>();
            string parentId;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                method = ContractClauseInventoryBuilder.NormalizeCallable(
                    method);
                if (ids.TryGetValue(method, out parentId))
                {
                    break;
                }

                if (method.MethodKind is
                    MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                {
                    unresolved.Push(method);
                    if (method.ContainingSymbol is IMethodSymbol parentMethod)
                    {
                        method = parentMethod;
                        continue;
                    }

                    parentId = SemanticClaimIdentity.CreateContainerId(
                        method.ContainingSymbol);
                    break;
                }

                parentId = SemanticClaimIdentity.CreateCallableId(method);
                ids.Add(method, parentId);
                break;
            }

            while (unresolved.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nested = unresolved.Pop();
                // Clause-free nested callables are intentionally excluded from
                // the stable ordinal sequence. They may still be visited while
                // resolving a containing callable, so use a deterministic
                // neutral ordinal rather than failing the entire manifest.
                var ordinal = ordinals.TryGetValue(nested, out var value)
                    ? value
                    : 0;
                parentId = SemanticClaimIdentity.CreateNestedCallableId(
                    parentId,
                    nested,
                    ordinal);
                ids.Add(nested, parentId);
            }
        }
    }

    private bool ContractsEnabled => enabledFeatures is WorkerFeatureSet.Contracts or WorkerFeatureSet.All;
    private bool EffectsEnabled => enabledFeatures is WorkerFeatureSet.Effects or WorkerFeatureSet.All;
    private string AssemblyName => _compilation.Assembly.Identity.Name;

    private static int NextRank(Dictionary<string, int> ranks, string key)
    {
        ranks.TryGetValue(key, out var rank);
        ranks[key] = rank + 1;
        return rank;
    }

    private Location AttributeLocation(AttributeData attribute, IMethodSymbol target)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
        target.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    }

    private static Location CallableLocation(IMethodSymbol method, SyntaxNode? declaration)
    {
        return declaration?.GetLocation() ?? method.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    }

    private WorkerSourceLocation ToSourceLocation(Location location)
    {
        if (!location.IsInSource)
        {
            return new WorkerSourceLocation();
        }

        var mapped = location.GetMappedLineSpan();
        var path = string.IsNullOrEmpty(mapped.Path)
            ? location.SourceTree?.FilePath ?? string.Empty
            : mapped.Path;
        var result = new WorkerSourceLocation
        {
            Path = string.IsNullOrEmpty(path) ? "<compiler-generated>" : path,
            Start = location.SourceSpan.Start,
            Length = location.SourceSpan.Length,
            Line = mapped.StartLinePosition.Line + 1,
            Column = mapped.StartLinePosition.Character + 1
        };
        if (location.SourceTree is { } sourceTree)
        {
            var ordinal = _compilation.SyntaxTrees.IndexOf(sourceTree);
            if (ordinal >= 0)
            {
                CompilerSourceLocationAuthority.RememberTree(result, ordinal);
            }
        }
        return result;
    }

}

internal sealed partial record ManifestCallableTarget
{
    internal BaseMethodDeclarationSyntax VerifierDeclaration => (BaseMethodDeclarationSyntax)Declaration!;
    internal SemanticModel VerifierSemanticModel => SemanticModel!;
}
