using SharpProof.Analyzer;

namespace SharpProof.CompilerArtifact;

internal sealed class ClaimManifestBuilder(
    CSharpCompilation compilation,
    WorkerFeatureSet enabledFeatures = WorkerFeatureSet.All,
    CancellationToken cancellationToken = default)
{
    private readonly CSharpCompilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
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
        var ordered = targets.Values.OrderBy(static target => target.Entry.CallableId, StringComparer.Ordinal);
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
        var resolution = _contractSources.Resolve(target);
        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var postconditions = CreatePostconditions(
            target, source, inventory, usesCompanion, callableId);
        var selected = SelectFeatures(target, resolution);
        var assumptions = CreateAssumptions(
            target, source, inventory, usesCompanion, callableId);
        if (postconditions.IsDefaultOrEmpty && selected.IsDefaultOrEmpty && assumptions.IsDefaultOrEmpty)
        {
            return null;
        }

        var location = CallableLocation(target, seed.Declaration);
        var effects = EffectsEnabled
            ? CreateEffectClaims(
                EffectContractDiagnostics.Evaluate(
                    target, location, _effectSession, static _ => { }, cancellationToken),
                target, callableId, postconditions.Length)
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
            Assumptions = [.. assumptions]
        };
        var supported = seed.Declaration is MethodDeclarationSyntax or ConstructorDeclarationSyntax &&
            target.MethodKind is MethodKind.Ordinary or MethodKind.Constructor;
        return new ManifestCallableTarget(target, seed.Declaration, seed.Model,
            entry, postconditions, effects, supported);
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
                .OrderBy(AttributeOrder)
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
        EffectiveContractSourceResolution resolution)
    {
        var selected = _attributes.Select(
            method,
            resolution.HasSelectedContractIntent,
            TrustedAttributes(method).Any());
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
        string callableId)
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
        foreach (var (scope, attribute) in TrustedAttributes(target))
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
        int ordinalOffset)
    {
        var claims = ImmutableArray.CreateBuilder<ManifestEffectClaim>();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var evaluation in evaluations.OrderBy(static evaluation => evaluation.Kind))
        {
            foreach (var attribute in evaluation.Attributes.OrderBy(AttributeOrder))
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
                    EffectContractKind = evaluation.Kind,
                    Location = ToSourceLocation(AttributeLocation(attribute, method))
                };
                var evidence = CreateEffectEvidence(claimId, evaluation);
                CompilerEffectClaimArtifactCodec.Seal(evidence);
                claims.Add(new ManifestEffectClaim(entry, evidence));
            }
        }

        return claims.ToImmutable();
    }

    private static CompilerEffectClaimArtifact CreateEffectEvidence(
        string claimId,
        EffectClaimEvaluation evaluation)
    {
        return new()
        {
            ClaimId = claimId,
            ContractKind = evaluation.Kind,
            Outcome = evaluation.Outcome,
            Reason = evaluation.Reason,
            Certainty = evaluation.Certainty,
            Constraint = new CompilerEffectConstraintArtifact
            {
                AllowedEffects = ToWorkerEffects(evaluation.Constraint.Effects),
                AllowedCapabilities = ToWorkerCapabilities(evaluation.Constraint.Capabilities),
                AllowedExceptionTypes = [.. evaluation.Constraint.ExceptionTypes
                    .Select(CompilerExceptionTypeIdentity.Encode)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static value => value, StringComparer.Ordinal)]
            },
            Witness = evaluation.Witness is not { } witness
                ? null
                : new WorkerEffectViolationWitness
                {
                    Kind = witness.Kind,
                    Detail = witness.Detail,
                    Effects = ToWorkerEffects(witness.Effects),
                    Capabilities = ToWorkerCapabilities(witness.Capabilities),
                    ExactExceptionTypeHierarchy =
                        CompilerExceptionTypeIdentity.EncodeHierarchy(
                            witness.ExceptionType),
                    Location = ToSourceLocation(witness.Location)
                },
            Evidence = evaluation.Evidence
        };
    }

    internal static WorkerEffectSet ToWorkerEffects(EffectContractKind source)
    {
        return source switch
        {
            EffectContractKind.None => WorkerEffectSet.None,
            _ when (source & EffectContractKind.ReadsReceiverState) != 0 =>
                WorkerEffectSet.ReadsReceiverState | ToWorkerEffects(source & ~EffectContractKind.ReadsReceiverState),
            _ when (source & EffectContractKind.ReadsArgumentState) != 0 =>
                WorkerEffectSet.ReadsArgumentState | ToWorkerEffects(source & ~EffectContractKind.ReadsArgumentState),
            _ when (source & EffectContractKind.ReadsCapturedState) != 0 =>
                WorkerEffectSet.ReadsCapturedState | ToWorkerEffects(source & ~EffectContractKind.ReadsCapturedState),
            _ when (source & EffectContractKind.ReadsStaticState) != 0 =>
                WorkerEffectSet.ReadsStaticState | ToWorkerEffects(source & ~EffectContractKind.ReadsStaticState),
            _ when (source & EffectContractKind.ReadsAmbientState) != 0 =>
                WorkerEffectSet.ReadsAmbientState | ToWorkerEffects(source & ~EffectContractKind.ReadsAmbientState),
            _ when (source & EffectContractKind.WritesReceiverState) != 0 =>
                WorkerEffectSet.WritesReceiverState | ToWorkerEffects(source & ~EffectContractKind.WritesReceiverState),
            _ when (source & EffectContractKind.WritesArgumentState) != 0 =>
                WorkerEffectSet.WritesArgumentState | ToWorkerEffects(source & ~EffectContractKind.WritesArgumentState),
            _ when (source & EffectContractKind.WritesCapturedState) != 0 =>
                WorkerEffectSet.WritesCapturedState | ToWorkerEffects(source & ~EffectContractKind.WritesCapturedState),
            _ when (source & EffectContractKind.WritesStaticState) != 0 =>
                WorkerEffectSet.WritesStaticState | ToWorkerEffects(source & ~EffectContractKind.WritesStaticState),
            _ when (source & EffectContractKind.WritesAmbientState) != 0 =>
                WorkerEffectSet.WritesAmbientState | ToWorkerEffects(source & ~EffectContractKind.WritesAmbientState),
            _ when (source & EffectContractKind.Allocates) != 0 =>
                WorkerEffectSet.Allocates | ToWorkerEffects(source & ~EffectContractKind.Allocates),
            _ when (source & EffectContractKind.Throws) != 0 =>
                WorkerEffectSet.Throws | ToWorkerEffects(source & ~EffectContractKind.Throws),
            _ when (source & EffectContractKind.Synchronizes) != 0 =>
                WorkerEffectSet.Synchronizes | ToWorkerEffects(source & ~EffectContractKind.Synchronizes),
            _ when (source & EffectContractKind.UsesNondeterminism) != 0 =>
                WorkerEffectSet.UsesNondeterminism | ToWorkerEffects(source & ~EffectContractKind.UsesNondeterminism),
            _ when (source & EffectContractKind.UsesNativeCode) != 0 =>
                WorkerEffectSet.UsesNativeCode | ToWorkerEffects(source & ~EffectContractKind.UsesNativeCode),
            _ when (source & EffectContractKind.UsesReflection) != 0 =>
                WorkerEffectSet.UsesReflection | ToWorkerEffects(source & ~EffectContractKind.UsesReflection),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    internal static WorkerEffectCapabilitySet ToWorkerCapabilities(
        EffectContractCapabilityKind source)
    {
        return source switch
        {
            EffectContractCapabilityKind.None => WorkerEffectCapabilitySet.None,
            _ when (source & EffectContractCapabilityKind.IO) != 0 =>
                WorkerEffectCapabilitySet.IO | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.IO),
            _ when (source & EffectContractCapabilityKind.FileRead) != 0 =>
                WorkerEffectCapabilitySet.FileRead | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.FileRead),
            _ when (source & EffectContractCapabilityKind.FileWrite) != 0 =>
                WorkerEffectCapabilitySet.FileWrite | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.FileWrite),
            _ when (source & EffectContractCapabilityKind.Network) != 0 =>
                WorkerEffectCapabilitySet.Network | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Network),
            _ when (source & EffectContractCapabilityKind.Console) != 0 =>
                WorkerEffectCapabilitySet.Console | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Console),
            _ when (source & EffectContractCapabilityKind.Process) != 0 =>
                WorkerEffectCapabilitySet.Process | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Process),
            _ when (source & EffectContractCapabilityKind.Environment) != 0 =>
                WorkerEffectCapabilitySet.Environment | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Environment),
            _ when (source & EffectContractCapabilityKind.Registry) != 0 =>
                WorkerEffectCapabilitySet.Registry | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Registry),
            _ when (source & EffectContractCapabilityKind.Clock) != 0 =>
                WorkerEffectCapabilitySet.Clock | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Clock),
            _ when (source & EffectContractCapabilityKind.Randomness) != 0 =>
                WorkerEffectCapabilitySet.Randomness | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Randomness),
            _ when (source & EffectContractCapabilityKind.Reflection) != 0 =>
                WorkerEffectCapabilitySet.Reflection | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Reflection),
            _ when (source & EffectContractCapabilityKind.Synchronization) != 0 =>
                WorkerEffectCapabilitySet.Synchronization | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.Synchronization),
            _ when (source & EffectContractCapabilityKind.NativeInterop) != 0 =>
                WorkerEffectCapabilitySet.NativeInterop | ToWorkerCapabilities(source & ~EffectContractCapabilityKind.NativeInterop),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    private IEnumerable<(ISymbol Scope, AttributeData Attribute)> TrustedAttributes(
        IMethodSymbol method)
    {
        foreach (var scope in SharpProofControlAttributePolicy.EnumerateScopes(method))
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

        string Resolve(IMethodSymbol method)
        {
            cancellationToken.ThrowIfCancellationRequested();
            method = ContractClauseInventoryBuilder.NormalizeCallable(method);
            if (ids.TryGetValue(method, out var id))
            {
                return id;
            }

            if (method.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction)
            {
                var parent = method.ContainingSymbol;
                var parentId = parent is IMethodSymbol parentMethod
                    ? Resolve(parentMethod)
                    : SemanticClaimIdentity.CreateContainerId(parent);
                id = SemanticClaimIdentity.CreateNestedCallableId(parentId, method, ordinals[method]);
            }
            else
            {
                id = SemanticClaimIdentity.CreateCallableId(method);
            }
            ids.Add(method, id);
            return id;
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

    private static (string Path, int Start) AttributeOrder(AttributeData attribute)
    {
        return (attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath ?? string.Empty,
                attribute.ApplicationSyntaxReference?.Span.Start ?? int.MaxValue);
    }

    private static Location CallableLocation(IMethodSymbol method, SyntaxNode? declaration)
    {
        return declaration?.GetLocation() ?? method.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    }

    private static WorkerSourceLocation ToSourceLocation(Location location)
    {
        if (!location.IsInSource)
        {
            return new WorkerSourceLocation();
        }

        var mapped = location.GetMappedLineSpan();
        var path = string.IsNullOrEmpty(mapped.Path)
            ? location.SourceTree?.FilePath ?? string.Empty
            : mapped.Path;
        return new WorkerSourceLocation
        {
            Path = string.IsNullOrEmpty(path) ? "<compiler-generated>" : path,
            Start = location.SourceSpan.Start,
            Length = location.SourceSpan.Length,
            Line = mapped.StartLinePosition.Line + 1,
            Column = mapped.StartLinePosition.Character + 1
        };
    }

    private readonly record struct ClaimCandidate(
        string Fingerprint, Location Location, WorkerClaimEvidence Evidence,
        IInvocationOperation? Operation, AttributeData? Attribute,
        ContractClausePlacement? Placement);
    private readonly record struct AssumptionCandidate(WorkerAssumptionKind Kind, string Fingerprint);
    private readonly record struct CallableSeed(
        IMethodSymbol Method, SyntaxNode? Declaration, SemanticModel? Model);
}

internal sealed record ClaimManifestBuildResult(WorkerClaimManifest Manifest,
    ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> Targets);

internal sealed record ManifestCallableTarget(IMethodSymbol Method, SyntaxNode? Declaration,
    SemanticModel? SemanticModel, WorkerCallableManifestEntry Entry, ImmutableArray<ManifestClaim> Claims,
    ImmutableArray<ManifestEffectClaim> EffectClaims, bool IsVerifierSupported)
{
    internal BaseMethodDeclarationSyntax VerifierDeclaration => (BaseMethodDeclarationSyntax)Declaration!;
    internal SemanticModel VerifierSemanticModel => SemanticModel!;
}

internal sealed record ManifestClaim(WorkerClaimManifestEntry Entry, IInvocationOperation? SourceOperation,
    AttributeData? SourceAttribute, ContractClausePlacement? Placement);

internal sealed record ManifestEffectClaim(WorkerClaimManifestEntry Entry, CompilerEffectClaimArtifact Evidence);
