using SharpProof.Analyzer;

// This builder runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal sealed partial class ClaimManifestBuilder(
    CSharpCompilation compilation,
    WorkerFeatureSet enabledFeatures = WorkerFeatureSet.All,
    CancellationToken cancellationToken = default)
{
    private const string TopLevelMainCallableId =
        "M:Program.<Main>$(System.String[])";

    private readonly CSharpCompilation _compilation =
        InitializeCompilation(compilation, cancellationToken);
    // Optional source guards must run before any semantic inventory is created.
    private readonly Lazy<ContractClauseInventoryBuilder> _lazyClauses =
        new(() => ContractClauseInventoryBuilder.ForCompilation(compilation));
    private readonly Lazy<ContractSelectionInventory> _lazyAttributes =
        new(() => ContractSelectionInventory.ForCompilation(compilation));
    private readonly Lazy<ContractApiSymbols?> _lazyIntrinsics =
        new(() => ContractApiSymbols.TryCreate(compilation));
    private readonly Lazy<EffectiveContractSourceResolver> _lazyContractSources =
        new(() => EffectiveContractSourceResolver.ForCompilation(compilation));
    private readonly Lazy<AnalyzerSession> _lazyEffectSession =
        new(() => new(compilation, AnalyzerConfiguration.AdvisoryAll, cancellationToken));
    private ContractClauseInventoryBuilder _clauses => _lazyClauses.Value;
    private ContractSelectionInventory _attributes => _lazyAttributes.Value;
    private ContractApiSymbols? _intrinsics => _lazyIntrinsics.Value;
    private EffectiveContractSourceResolver _contractSources => _lazyContractSources.Value;
    private AnalyzerSession _effectSession => _lazyEffectSession.Value;

    private static CSharpCompilation InitializeCompilation(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        cancellationToken.ThrowIfCancellationRequested();
        return compilation;
    }

    internal ClaimManifestBuildResult Build(bool includePotentialCallShadow = false)
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
        var result = new ClaimManifestBuildResult(manifest, targets.ToImmutable());
        return includePotentialCallShadow
            ? result with { PotentialCalls = DiscoverPotentialCallShadow(result.Targets) }
            : result;
    }

    internal CompilerPotentialCallInventory BuildPotentialCallShadow(bool allowReferenceOwners = false)
    {
        return DiscoverPotentialCallShadow(ImmutableDictionary.Create<IMethodSymbol, ManifestCallableTarget>(
            SymbolEqualityComparer.Default), allowReferenceOwners);
    }

    // Separate source census: it must not change mandatory manifest membership
    // or the selected nested callable ordinal stream.
    private CompilerPotentialCallInventory DiscoverPotentialCallShadow(
        ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> published, bool allowReferenceOwners = false)
    {
        var owners = ImmutableArray.CreateBuilder<CompilerPotentialCallOwner>();
        var gaps = ImmutableArray.CreateBuilder<CompilerPotentialCallGap>();
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var guarded = new List<(SyntaxTree Tree, ImmutableArray<SyntaxNode> Nodes)>();
        var remainingSyntaxNodes = 1_048_576;
        var treeOrdinal = 0;
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (treeOrdinal >= CompilerArtifactLimits.MaximumInstructions)
            {
                gaps.Add(new(treeOrdinal, 0, 0, "InventoryBudget"));
                return new([], gaps.ToImmutable());
            }
            var root = tree.GetRoot(cancellationToken);
            if (!TryCollectPotentialSyntax(root, ref remainingSyntaxNodes, out var nodes))
            {
                gaps.Add(new(treeOrdinal++, root.SpanStart, root.Span.Length, "SyntaxBudget"));
                // Contract screening may bind source targets or companions in
                // any tree. No optional binding occurs unless every tree passed.
                return new([], gaps.ToImmutable());
            }
            guarded.Add((tree, nodes));
            treeOrdinal++;
        }
        if (GuardReferencedPotentialSyntax(ref remainingSyntaxNodes) is { } referenceGap)
        {
            gaps.Add(referenceGap);
            return new([], gaps.ToImmutable());
        }
        treeOrdinal = 0;
        foreach (var (tree, nodes) in guarded)
        {
            SemanticModel? model = null;
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is not MethodDeclarationSyntax declaration)
                {
                    continue;
                }
                if (owners.Count + gaps.Count >= CompilerArtifactLimits.MaximumInstructions)
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "InventoryBudget"));
                    return new(owners.ToImmutable(), gaps.ToImmutable());
                }
                if (!declaration.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                    declaration.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
                    declaration.TypeParameterList != null ||
                    declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any(static type => type.TypeParameterList != null) ||
                    declaration.DescendantNodes().Any(static child =>
                        child is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or YieldStatementSyntax))
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "UnsupportedOwner"));
                    continue;
                }
                if (declaration.Body == null && declaration.ExpressionBody == null)
                {
                    // Partial definition declarations do not execute a body.
                    continue;
                }
                model ??= SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, tree);
                var method = model.GetDeclaredSymbol(declaration, cancellationToken);
                if (method == null)
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "MissingSymbol"));
                    continue;
                }
                method = ContractClauseInventoryBuilder.NormalizeCallable(method);
                if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic)
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "UnsupportedOwner"));
                    continue;
                }
                if (!seen.Add(method))
                {
                    continue;
                }
                if (method.ReturnsByRef || method.ReturnsByRefReadonly ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                        !SupportedShadowOwnerType(parameter.Type, allowReferenceOwners)) ||
                    !method.ReturnsVoid && !SupportedShadowOwnerType(method.ReturnType, allowReferenceOwners))
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "UnsupportedSignature"));
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(declaration, cancellationToken) is not IMethodBodyOperation operation ||
                    operation.Parent != null || operation.Syntax != declaration)
                {
                    gaps.Add(new(treeOrdinal, declaration.SpanStart, declaration.Span.Length, "MissingBody"));
                    continue;
                }
                var owned = PotentialCalls(method, operation, _effectSession.HasPotentialCallPreconditions);
                const bool complete = true;
                var id = published.TryGetValue(method, out var selected)
                    ? selected.Entry.CallableId : SemanticClaimIdentity.CreateCallableId(method);
                owners.Add(new(method, declaration, model, id, owned, complete));
            }
            treeOrdinal++;
        }
        return new(owners.ToImmutable(), gaps.ToImmutable());
    }

    // The explicit calls and constructions whose target may have a
    // precondition, in source order.
    private static ImmutableArray<PotentialRequiresCallSite> PotentialCalls(IMethodSymbol owner, IOperation body,
        Func<IMethodSymbol, bool> hasPreconditions)
    {
        var calls = ImmutableArray.CreateBuilder<PotentialRequiresCallSite>();
        foreach (var operation in body.Descendants().OrderBy(static operation => operation.Syntax.SpanStart))
        {
            var (target, instance, arguments) = operation switch
            {
                IInvocationOperation invocation => (invocation.TargetMethod, invocation.Instance, invocation.Arguments),
                IObjectCreationOperation { Constructor: { } constructor } creation => (constructor, null, creation.Arguments),
                _ => ((IMethodSymbol?)null, (IOperation?)null, ImmutableArray<IArgumentOperation>.Empty)
            };
            if (target != null && hasPreconditions(target))
            {
                calls.Add(new(owner, operation, operation.Syntax, PotentialRequiresCallOrigin.Operation, 0, target,
                    target.ReducedFrom ?? target, instance, arguments, ImmutableDictionary<int, IOperation>.Empty,
                    ImmutableDictionary<int, long>.Empty, true));
            }
        }
        return calls.ToImmutable();
    }

    private static bool SupportedShadowOwnerType(ITypeSymbol type, bool allowReferenceOwners)
    {
        return SharpProof.Frontend.CSharpOperationSemantics.IsScalar(type) ||
            allowReferenceOwners && type.TypeKind != TypeKind.Dynamic &&
            type.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Delegate;
    }

    private CompilerPotentialCallGap? GuardReferencedPotentialSyntax(ref int remainingNodes)
    {
        var pending = new Stack<Compilation>();
        var visited = new HashSet<Compilation> { _compilation };
        var treeOwners = new Dictionary<SyntaxTree, Compilation>();
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            treeOwners.Add(tree, _compilation);
        }
        pending.Push(_compilation);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var reference in current.References)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remainingNodes < 0)
                {
                    return new(0, 0, 0, "ReferenceBudget", current.AssemblyName);
                }
                if (reference is not CompilationReference source || !visited.Add(source.Compilation))
                {
                    continue;
                }
                if (visited.Count > CompilerArtifactLimits.MaximumInstructions)
                {
                    return new(0, 0, 0, "ReferenceBudget", source.Compilation.AssemblyName);
                }
                var ordinal = 0;
                foreach (var tree in source.Compilation.SyntaxTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (treeOwners.TryGetValue(tree, out var owner) && !ReferenceEquals(owner, source.Compilation))
                    {
                        return new(ordinal, 0, 0, "ReferenceOwnership", source.Compilation.AssemblyName);
                    }
                    treeOwners[tree] = source.Compilation;
                    var root = tree.GetRoot(cancellationToken);
                    if (!TryCollectPotentialSyntax(root, ref remainingNodes, out _))
                    {
                        return new(ordinal, root.SpanStart, root.Span.Length, "ReferenceSyntaxBudget",
                            source.Compilation.AssemblyName);
                    }
                    ordinal++;
                }
                pending.Push(source.Compilation);
            }
        }
        return null;
    }

    private bool TryCollectPotentialSyntax(SyntaxNode root, ref int remainingNodes,
        out ImmutableArray<SyntaxNode> nodes)
    {
        const int maximumNodes = 65_536;
        var pending = new Stack<(SyntaxNode Node, int Depth)>();
        var collected = ImmutableArray.CreateBuilder<SyntaxNode>();
        pending.Push((root, 0));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, depth) = pending.Pop();
            if (depth > 128 || collected.Count >= maximumNodes || --remainingNodes < 0)
            {
                nodes = [];
                return false;
            }
            collected.Add(node);
            foreach (var child in node.ChildNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pending.Count + collected.Count >= maximumNodes)
                {
                    nodes = [];
                    return false;
                }
                pending.Push((child, depth + 1));
            }
        }
        nodes = collected.OrderBy(static node => node.SpanStart).ToImmutableArray();
        return true;
    }

    private ManifestCallableTarget? BuildTarget(CallableSeed seed, string callableId)
    {
        var target = seed.Method;
        _ = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            target,
            _effectSession,
            static _ => { },
            cancellationToken);

        var resolution = _contractSources.Resolve(target);
        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var trustedAttributes = TrustedAttributes(target).ToImmutableArray();
        var selection = _attributes.Select(
            target,
            resolution.HasSelectedContractIntent);
        var selected = SelectFeatures(
            selection,
            !trustedAttributes.IsDefaultOrEmpty);
        var effectAssumptionsEnabled =
            EffectsEnabled &&
            (selection & ContractSelectionFeatures.Effects) != 0;
        var clausePartitions = ContractsEnabled || effectAssumptionsEnabled
            ? PartitionClauses(inventory.Clauses)
            : default;
        var postconditions = CreatePostconditions(
            target, source, clausePartitions.Postconditions,
            usesCompanion, callableId);
        var assumptions = CreateAssumptions(
            target,
            source,
            clausePartitions.Assumptions,
            usesCompanion,
            callableId,
            trustedAttributes,
            ContractsEnabled || effectAssumptionsEnabled);
        if (postconditions.IsDefaultOrEmpty && selected.IsDefaultOrEmpty && assumptions.IsDefaultOrEmpty)
        {
            return null;
        }

        var analyzerSelection = selection;
        var analyzerContractsSelected =
            ContractsEnabled &&
            (analyzerSelection &
             ContractSelectionFeatures.Contracts) != 0;
        var analyzerEffectsSelected =
            EffectsEnabled &&
            (analyzerSelection &
             ContractSelectionFeatures.Effects) != 0;
        // The worker decides whether a body is supported; here only the
        // callable's shape is classified. A trusted bodyless contract needs
        // no body.
        var selectedSubset = !(analyzerContractsSelected || analyzerEffectsSelected) ||
            (seed.Method.IsAbstract || seed.Method.IsExtern) && analyzerEffectsSelected && !analyzerContractsSelected &&
                _effectSession.ResolveEffectContract(seed.Method).Kind == EffectContractResolutionKind.Valid ||
            seed.Declaration != null && seed.Model != null && CallableSubset.IsSupported(seed.Method, seed.Declaration);
        var supported =
            seed.Declaration is
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax &&
            target.MethodKind is
                MethodKind.Ordinary or
                MethodKind.Constructor or
                MethodKind.ExplicitInterfaceImplementation &&
            selectedSubset;
        var location = CallableLocation(target, seed.Declaration);
        var effects = EffectsEnabled
            ? CreateEffectClaims(
                EffectContractDiagnostics.Declare(target, location, _effectSession, cancellationToken),
                target, callableId, postconditions.Length, supported)
            : [];
        var features = new HashSet<WorkerSelectedFeature>(selected);
        if (!postconditions.IsDefaultOrEmpty)
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

    private ImmutableArray<ManifestClaim> CreatePostconditions(
        IMethodSymbol target,
        IMethodSymbol source,
        ImmutableArray<ContractClauseOccurrence> clauses,
        bool usesCompanion,
        string callableId)
    {
        if (!ContractsEnabled)
        {
            return [];
        }

        var candidates = clauses
            .Select(clause => new ClaimCandidate(
                SemanticClaimIdentity.CreateInvocationFingerprint(
                    clause.Invocation, target, source, usesCompanion, _intrinsics),
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
        ContractSelectionFeatures selection,
        bool hasTrustedAttributes)
    {
        if (hasTrustedAttributes)
        {
            selection |= ContractSelectionFeatures.Contracts |
                ContractSelectionFeatures.Effects;
        }

        var result = ImmutableArray.CreateBuilder<WorkerSelectedFeature>(2);
        if (EffectsEnabled && (selection & ContractSelectionFeatures.Effects) != 0)
        {
            result.Add(WorkerSelectedFeature.Effects);
        }

        if (ContractsEnabled && (selection & ContractSelectionFeatures.Contracts) != 0)
        {
            result.Add(WorkerSelectedFeature.Contracts);
        }

        return result.ToImmutable();
    }

    private ImmutableArray<WorkerAssumptionEvidence> CreateAssumptions(
        IMethodSymbol target,
        IMethodSymbol source,
        ImmutableArray<ContractClauseOccurrence> clauses,
        bool usesCompanion,
        string callableId,
        ImmutableArray<(ISymbol Scope, AttributeData Attribute)> trustedAttributes,
        bool includeContractAssumptions)
    {
        var candidates = ImmutableArray.CreateBuilder<AssumptionCandidate>();
        if (includeContractAssumptions)
        {
            foreach (var clause in clauses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(new AssumptionCandidate(
                    clause.Kind == BoundContractKind.Requires
                        ? WorkerAssumptionKind.Precondition
                        : WorkerAssumptionKind.UserAssume,
                    SemanticClaimIdentity.CreateInvocationFingerprint(
                        clause.Invocation, target, source, usesCompanion, _intrinsics)));
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

    private ClausePartitions PartitionClauses(
        ImmutableArray<ContractClauseOccurrence> clauses)
    {
        var postconditions = ImmutableArray.CreateBuilder<ContractClauseOccurrence>();
        var assumptions = ImmutableArray.CreateBuilder<ContractClauseOccurrence>();
        foreach (var clause in clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clause.Placement == ContractClausePlacement.NestedCallable)
            {
                continue;
            }

            switch (clause.Kind)
            {
                case BoundContractKind.Ensures:
                    postconditions.Add(clause);
                    break;
                case BoundContractKind.Requires:
                case BoundContractKind.Assume:
                    assumptions.Add(clause);
                    break;
            }
        }

        return new(postconditions.ToImmutable(), assumptions.ToImmutable());
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
            var attributes = evaluation.Attributes
                .OrderBy(
                    static attribute =>
                        attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath ?? string.Empty,
                    StringComparer.Ordinal)
                .ThenBy(static attribute =>
                    attribute.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue)
                .ToImmutableArray();

            // AllowedExceptions attributes are unioned by the analyzer, so
            // publishing one claim per attribute would make every claim appear
            // to prove a constraint that its own attribute may not satisfy.
            // Keep one authoritative claim for the combined constraint and use
            // all attribute identities so adding or removing an occurrence also
            // changes the claim identity.
            if (evaluation.Kind == EffectEvaluationContractKind.AllowedExceptions &&
                attributes.Length > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fingerprint = "effect:" + evaluation.Kind + ":combined:" +
                    string.Join(
                        "|",
                        attributes.Select(attribute =>
                            SemanticClaimIdentity.CreateAttributeFingerprint(
                                attribute, method)));
                claims.Add(CreateEffectClaim(
                    evaluation,
                    method,
                    callableId,
                    ordinalOffset + claims.Count,
                    isSupported,
                    attributes[0],
                    fingerprint,
                    ranks,
                    method.Locations.FirstOrDefault(static location => location.IsInSource)));
                continue;
            }

            foreach (var attribute in attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fingerprint = "effect:" + evaluation.Kind + ":" +
                    SemanticClaimIdentity.CreateAttributeFingerprint(attribute, method);
                claims.Add(CreateEffectClaim(
                    evaluation,
                    method,
                    callableId,
                    ordinalOffset + claims.Count,
                    isSupported,
                    attribute,
                    fingerprint,
                    ranks));
            }
        }

        return claims.ToImmutable();
    }

    private ManifestEffectClaim CreateEffectClaim(
        EffectClaimEvaluation evaluation,
        IMethodSymbol method,
        string callableId,
        int ordinal,
        bool isSupported,
        AttributeData attribute,
        string fingerprint,
        Dictionary<string, int> ranks,
        Location? locationOverride = null)
    {
        var claimId = SemanticClaimIdentity.Create(
            AssemblyName, callableId, fingerprint, NextRank(ranks, fingerprint));
        var location = locationOverride ?? AttributeLocation(attribute, method);
        var entry = new WorkerClaimManifestEntry
        {
            ClaimId = claimId,
            CallableId = callableId,
            Ordinal = ordinal,
            Kind = WorkerClaimKind.Effect,
            Evidence = WorkerClaimEvidence.Attribute,
            EffectContractKind =
                CompilerEffectEvaluationWireMappings.ToWorker(
                    evaluation.Kind),
            Location = ToSourceLocation(location)
        };
        var evidence = CreateEffectEvidence(claimId, evaluation, isSupported);
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        return new ManifestEffectClaim(entry, evidence, evaluation.Reason != EffectEvaluationReason.UnsupportedContract);
    }

    private static CompilerEffectClaimArtifact CreateEffectEvidence(
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
            evidence.Outcome = WorkerClaimOutcome.Unknown;
            evidence.Reason = WorkerClaimReason.UnsupportedContract;
            evidence.Certainty = WorkerEffectEvidenceCertainty.Unavailable;
        }
        return evidence;
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
        var assemblySelected = _compilation.Assembly.GetAttributes().Any(RequiresScopeDiscovery);
        var selectedTrees = SelectedScopeTrees();
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            // Semantic-model creation binds every nested local function. Skip
            // trees with no SharpProof syntax so an unrelated deeply nested
            // tree cannot exhaust Roslyn's binder stack during discovery.
            if (!assemblySelected && !selectedTrees.Contains(tree) && !MayContainSharpProofSyntax(root))
            {
                continue;
            }

            var treeMethods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(
                SymbolEqualityComparer.Default);

            void DiscoverTree()
            {
                var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                    _compilation,
                    tree);
                foreach (var node in root.DescendantNodesAndSelf())
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

            DiscoverTree();

            methods.UnionWith(treeMethods);

            void Add(IMethodSymbol? method)
            {
                if (method != null &&
                    !ContractForSymbolMatcher.IsCompanionType(
                        _contractSources.Companions,
                        method.ContainingType))
                {
                    treeMethods.Add(ContractClauseInventoryBuilder.NormalizeCallable(method));
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
        foreach (var companion in _contractSources.Companions)
        {
            foreach (var method in ContractForSymbolMatcher.GetOrdinaryMethods(companion.Target))
            {
                cancellationToken.ThrowIfCancellationRequested();
                methods.Add(ContractClauseInventoryBuilder.NormalizeCallable(method));
            }
        }

        return methods.ToImmutableArray();

        static bool MayContainSharpProofSyntax(SyntaxNode root)
        {
            // An alias declared in another tree can name any attribute. Let
            // semantic selection decide whether attributed trees are relevant.
            if (root.DescendantNodesAndSelf().Any(static node => node is AttributeSyntax))
            { return true; }
            foreach (var token in root.DescendantTokens())
            {
                if (token.ValueText is
                    "SharpProof" or
                    "Contract" or
                    "Requires" or
                    "Ensures" or
                    "Assume" or
                    "Old" or
                    "Result" or
                    "ContractFor" or
                    "EnforcePure" or
                    "ZeroAllocations" or
                    "AllowedCapabilities" or
                    "DoesNotThrow" or
                    "AllowedExceptions" or
                    "EffectContract" or
                    "NotNull" or
                    "Positive" or
                    "InRange" or
                    "SharpProofSuppress" or
                    "SharpProofTrusted" or
                    "ContractForAttribute" or
                    "EnforcePureAttribute" or
                    "ZeroAllocationsAttribute" or
                    "AllowedCapabilitiesAttribute" or
                    "DoesNotThrowAttribute" or
                    "AllowedExceptionsAttribute" or
                    "EffectContractAttribute" or
                    "NotNullAttribute" or
                    "PositiveAttribute" or
                    "InRangeAttribute" or
                    "SharpProofSuppressAttribute" or
                    "SharpProofTrustedAttribute")
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool RequiresScopeDiscovery(AttributeData attribute)
    {
        return ContractSelectionInventory.Is(attribute, _attributes.Trusted) ||
            _attributes.IsRejectedControlAttribute(attribute);
    }

    private HashSet<SyntaxTree> SelectedScopeTrees()
    {
        var trees = new HashSet<SyntaxTree>();
        var pending = new Stack<(INamespaceOrTypeSymbol Scope, bool Selected)>();
        pending.Push((_compilation.Assembly.GlobalNamespace, false));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (scope, inheritedSelection) = pending.Pop();
            var selected = inheritedSelection;
            if (scope is INamedTypeSymbol type)
            {
                selected |= type.GetAttributes().Any(RequiresScopeDiscovery);
                if (selected)
                {
                    foreach (var declaration in type.DeclaringSyntaxReferences)
                    { trees.Add(declaration.SyntaxTree); }
                }
            }
            if (scope is INamespaceSymbol @namespace)
            {
                foreach (var child in @namespace.GetNamespaceMembers())
                { pending.Push((child, selected)); }
            }
            foreach (var child in scope.GetTypeMembers())
            { pending.Push((child, selected)); }
        }
        return trees;
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
        // Identity ancestry is required even when the ancestor has no claims.
        // Keep its membership separate from callable publication.
        var required = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>();
        foreach (var seed in callables)
        {
            if (HasManifestIdentity(seed) || TrustedAttributes(seed.Method).Any())
            {
                required.Add(seed.Method);
                pending.Push(seed.Method);
            }
        }
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pending.Pop().ContainingSymbol is IMethodSymbol containing)
            {
                var parent = ContractClauseInventoryBuilder.NormalizeCallable(containing);
                if (required.Add(parent))
                {
                    pending.Push(parent);
                }
            }
        }
        var ordinals = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        foreach (var group in callables
                     .Where(static seed => seed.Method.MethodKind is
                         MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                     // Callables without contract or effect claims do not
                     // participate in the manifest identity. Excluding them
                     // keeps an unrelated sibling from renumbering the
                     // callables that do. Use the semantic selection inventory
                     // so effect-only callables participate without treating
                     // comments or string literals as contract clauses.
                     .Where(HasManifestIdentity)
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

        // Preserve all existing selected-sibling slots. Append the required
        // plain ancestors and trust-only siblings; unrelated siblings use none.
        foreach (var group in required
                     .Where(static method => method.MethodKind is
                         MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                     .Select(CreateSeed)
                     .GroupBy(static seed => seed.Method.ContainingSymbol!, SymbolEqualityComparer.Default))
        {
            var ordinal = group.Count(seed => ordinals.ContainsKey(seed.Method));
            foreach (var seed in group.Where(seed => !ordinals.ContainsKey(seed.Method))
                         .OrderBy(seed => seed.Declaration == null
                             ? int.MaxValue : _clauses.GetTreeOrdinal(seed.Declaration.SyntaxTree))
                         .ThenBy(static seed => seed.Declaration?.SpanStart ?? int.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seed.Declaration == null)
                {
                    throw new InvalidOperationException("A required nested callable has no source declaration.");
                }
                ordinals.Add(seed.Method, ordinal++);
            }
        }

        var ids = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var seed in callables)
        {
            Resolve(seed.Method);
        }

        return ids.ToImmutableDictionary(SymbolEqualityComparer.Default);

        bool HasManifestIdentity(CallableSeed seed)
        {
            var resolution = _contractSources.Resolve(seed.Method);
            var selection = _attributes.Select(
                seed.Method,
                resolution.HasSelectedContractIntent);
            return (selection & (ContractSelectionFeatures.Contracts |
                ContractSelectionFeatures.Effects)) != 0;
        }

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
                // Unneeded clause-free callables do not participate in published
                // identity. Required ancestors already have distinct ordinals.
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

    private readonly record struct ClausePartitions(
        ImmutableArray<ContractClauseOccurrence> Postconditions,
        ImmutableArray<ContractClauseOccurrence> Assumptions);

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
        if (declaration is CompilationUnitSyntax compilationUnit &&
            method.MethodKind == MethodKind.Ordinary &&
            string.Equals(
                SemanticClaimIdentity.CreateCallableId(method),
                TopLevelMainCallableId,
                StringComparison.Ordinal) &&
            compilationUnit.Members.OfType<GlobalStatementSyntax>()
                .LastOrDefault() is { } lastGlobalStatement)
        {
            return compilationUnit.SyntaxTree.GetLocation(
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    compilationUnit.FullSpan.Start,
                    lastGlobalStatement.Span.End));
        }

        return declaration?.GetLocation() ?? method.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    }

    private WorkerSourceLocation ToSourceLocation(Location location)
    {
        var result = CompilerSourceLocationProjection.Create(location);
        if (!location.IsInSource)
        {
            return result;
        }

        if (string.IsNullOrEmpty(result.Path))
        {
            result.Path = location.SourceTree?.FilePath ?? string.Empty;
        }
        result.Path = string.IsNullOrEmpty(result.Path)
            ? "<compiler-generated>"
            : result.Path;
        if (location.SourceTree is { } sourceTree)
        {
            var ordinal = _compilation.SyntaxTrees.IndexOf(sourceTree);
            if (ordinal >= 0)
            {
                CompilerSourceCoordinates.RememberTree(result, ordinal);
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
