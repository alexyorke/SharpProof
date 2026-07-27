namespace SharpProof.CompilerArtifact;
internal sealed class ClaimManifestBuilder(
    CSharpCompilation compilation,
    WorkerFeatureSet enabledFeatures = WorkerFeatureSet.All) {
    private readonly CSharpCompilation _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly ContractClauseInventoryBuilder _clauses = new(compilation);
    private readonly SelectedAttributeSymbols _attributes = new(compilation);
    private readonly ImmutableArray<CompanionDescriptor> _companions = DiscoverCompanions(compilation);
    internal ClaimManifestBuildResult Build() {
        var discovered = DiscoverMethods().Select(CreateSeed).ToImmutableArray();
        var callableIds = CreateCallableIds(discovered);
        var targets = ImmutableDictionary.CreateBuilder<IMethodSymbol, ManifestCallableTarget>(SymbolEqualityComparer.Default);
        var callables = ImmutableArray.CreateBuilder<WorkerCallableManifestEntry>();
        var claims = ImmutableArray.CreateBuilder<WorkerClaimManifestEntry>();
        foreach (var seed in discovered.OrderBy(
                     seed => callableIds[seed.Method], StringComparer.Ordinal)) {
            var target = BuildTarget(seed, callableIds[seed.Method]);
            if (target == null) continue;
            targets.Add(seed.Method, target);
            callables.Add(target.Entry);
            claims.AddRange(target.Claims.Select(static claim => claim.Entry));
        }
        var manifest = new WorkerClaimManifest {
            Callables = callables.ToArray(),
            Claims = claims.ToArray()
        };
        WorkerProtocolJson.SealManifest(manifest);
        return new ClaimManifestBuildResult(manifest, targets.ToImmutable());
    }
    private ManifestCallableTarget? BuildTarget(CallableSeed seed, string callableId) {
        var target = seed.Method;
        var inventory = seed.Body == null
            ? _clauses.Create(target)
            : _clauses.Create(target, seed.Body);
        var source = target;
        var usesCompanion = false;
        if (inventory.Clauses.IsDefaultOrEmpty &&
            TryResolveCompanion(target, out var companion)) {
            source = companion!;
            inventory = _clauses.Create(source);
            usesCompanion = true;
        }
        var candidates = ImmutableArray.CreateBuilder<ClaimCandidate>();
        foreach (var occurrence in inventory.Clauses) {
            if (!ContractsEnabled ||
                occurrence.Kind != BoundContractKind.Ensures ||
                occurrence.Placement == ContractClausePlacement.NestedCallable)
                continue;
            candidates.Add(new ClaimCandidate(
                SemanticClaimIdentity.CreateInvocationFingerprint(occurrence.Invocation, target, source, usesCompanion),
                occurrence.Location,
                usesCompanion ? WorkerClaimEvidence.CompanionClause : WorkerClaimEvidence.DirectClause,
                occurrence.Invocation, null, occurrence.Placement));
        }
        foreach (var attribute in target.GetReturnTypeAttributes()
                     .Where(attribute => ContractsEnabled && _attributes.IsClosedReturnAttribute(attribute))
                     .OrderBy(GetAttributeOrder))
            candidates.Add(new ClaimCandidate(
                SemanticClaimIdentity.CreateAttributeFingerprint(attribute, target),
                GetAttributeLocation(attribute, target), WorkerClaimEvidence.ReturnAttribute,
                 null, attribute, null));
        var selected = _attributes.GetSelectedFeatures(target).Where(IsFeatureEnabled).ToImmutableArray();
        var assumptions = CreateAssumptions(
            target, source, inventory, usesCompanion, callableId);
        if (candidates.Count == 0 && selected.IsDefaultOrEmpty && assumptions.IsDefaultOrEmpty)
            return null;
        var claims = CreateClaims(candidates.ToImmutable(), callableId, _compilation.Assembly.Identity.Name);
        var features = new HashSet<WorkerSelectedFeature>(selected);
        if (claims.Length != 0 ||
            assumptions.Any(static evidence => evidence.Kind == WorkerAssumptionKind.UserAssume))
            features.Add(WorkerSelectedFeature.Contracts);
        var reasons = ImmutableArray.CreateBuilder<WorkerSelectionReason>(2);
        if (!selected.IsDefaultOrEmpty || assumptions.Length != 0)
            reasons.Add(WorkerSelectionReason.ExplicitAnnotation);
        if (claims.Length != 0)
            reasons.Add(WorkerSelectionReason.DiscoveredPostcondition);
        var declaration = seed.Declaration;
        var callableLocation = GetCallableLocation(target, declaration);
        var entry = new WorkerCallableManifestEntry {
            CallableId = callableId,
            SelectedFeatures = [.. features.OrderBy(static value => value)],
            SelectionReasons = reasons.ToArray(),
            Location = callableLocation.IsInSource ? ToSourceLocation(callableLocation) :
                claims.FirstOrDefault()?.Entry.Location ?? new WorkerSourceLocation(),
            ClaimIds = [.. claims.Select(static claim => claim.Entry.ClaimId)]
        };
        var supported = declaration is MethodDeclarationSyntax or ConstructorDeclarationSyntax
            && target.MethodKind is MethodKind.Ordinary or MethodKind.Constructor;
        return new ManifestCallableTarget(
            target, declaration, seed.Model, entry, claims, assumptions, supported);
    }
    private bool ContractsEnabled =>
        enabledFeatures is WorkerFeatureSet.Contracts or WorkerFeatureSet.All;
    private bool IsFeatureEnabled(WorkerSelectedFeature feature) =>
        enabledFeatures == WorkerFeatureSet.All
        || enabledFeatures == WorkerFeatureSet.Contracts && feature == WorkerSelectedFeature.Contracts
        || enabledFeatures == WorkerFeatureSet.Effects && feature == WorkerSelectedFeature.Effects;
    private ImmutableArray<WorkerAssumptionEvidence> CreateAssumptions(
        IMethodSymbol target, IMethodSymbol source,
        ContractClauseInventory inventory, bool usesCompanion, string callableId) {
        var candidates = ImmutableArray.CreateBuilder<AssumptionCandidate>();
        if (ContractsEnabled)
            foreach (var occurrence in inventory.Clauses) {
                if (occurrence.Kind != BoundContractKind.Assume ||
                    occurrence.Placement == ContractClausePlacement.NestedCallable)
                    continue;
                candidates.Add(new AssumptionCandidate(
                    WorkerAssumptionKind.UserAssume,
                    SemanticClaimIdentity.CreateInvocationFingerprint(occurrence.Invocation, target, source, usesCompanion)));
            }
        foreach (var (scope, attribute) in _attributes.GetTrustedAttributes(target))
            candidates.Add(new AssumptionCandidate(
                WorkerAssumptionKind.TrustedBoundary,
                SemanticClaimIdentity.CreateTrustedFingerprint(attribute, scope, target)));
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<WorkerAssumptionEvidence>(candidates.Count);
        foreach (var candidate in candidates) {
            var key = candidate.Kind + ":" + candidate.Fingerprint;
            ranks.TryGetValue(key, out var rank);
            ranks[key] = rank + 1;
            result.Add(new WorkerAssumptionEvidence {
                Id = SemanticClaimIdentity.CreateAssumption(
                    _compilation.Assembly.Identity.Name, callableId, candidate.Kind, candidate.Fingerprint, rank),
                Kind = candidate.Kind,
                Used = false
            });
        }
        return result.MoveToImmutable();
    }
    private static ImmutableArray<ManifestClaim> CreateClaims(
        ImmutableArray<ClaimCandidate> candidates,
        string callableId,
        string assemblyName) {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var claims = ImmutableArray.CreateBuilder<ManifestClaim>(candidates.Length);
        for (var ordinal = 0; ordinal < candidates.Length; ordinal++) {
            var candidate = candidates[ordinal];
            ranks.TryGetValue(candidate.PredicateFingerprint, out var rank);
            ranks[candidate.PredicateFingerprint] = rank + 1;
            var entry = new WorkerClaimManifestEntry {
                ClaimId = SemanticClaimIdentity.Create(
                    assemblyName, callableId, candidate.PredicateFingerprint, rank),
                CallableId = callableId,
                Ordinal = ordinal,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = candidate.Evidence,
                Location = ToSourceLocation(candidate.Location)
            };
            claims.Add(new ManifestClaim(entry, candidate.SourceOperation, candidate.SourceAttribute, candidate.Placement));
        }
        return claims.MoveToImmutable();
    }
    private ImmutableArray<IMethodSymbol> DiscoverMethods() {
        var methods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in _compilation.SyntaxTrees) {
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, tree);
            foreach (var node in tree.GetRoot().DescendantNodesAndSelf()) {
                switch (node) {
                    case BaseMethodDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case LocalFunctionStatementSyntax:
                        Add(model.GetDeclaredSymbol(node) as IMethodSymbol); break;
                    case AnonymousFunctionExpressionSyntax anonymous:
                        Add((model.GetOperation(anonymous) as IAnonymousFunctionOperation)?.Symbol);
                        break;
                    case GlobalStatementSyntax global:
                        Add(model.GetEnclosingSymbol(global.SpanStart) as IMethodSymbol);
                        break;
                    case BasePropertyDeclarationSyntax property:
                        switch (model.GetDeclaredSymbol(property)) {
                            case IPropertySymbol propertySymbol:
                                Add(propertySymbol.GetMethod);
                                Add(propertySymbol.SetMethod);
                                break;
                            case IEventSymbol eventSymbol:
                                Add(eventSymbol.AddMethod);
                                Add(eventSymbol.RemoveMethod);
                                Add(eventSymbol.RaiseMethod);
                                break;
                        }
                        break;
                }
            }
        }
        foreach (var companion in _companions)
            foreach (var method in companion.Target.GetMembers().OfType<IMethodSymbol>().Where(IsExplicitOrdinaryMethod))
                methods.Add(NormalizePartial(method));
        return methods.ToImmutableArray();
        void Add(IMethodSymbol? method) {
            if (method != null && !IsCompanionType(method.ContainingType))
                methods.Add(NormalizePartial(method));
        }
    }
    private CallableSeed CreateSeed(IMethodSymbol method) {
        var declaration = GetDeclaration(method);
        var model = declaration == null ? null :
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                _compilation, declaration.SyntaxTree);
        var body = declaration switch {
            AnonymousFunctionExpressionSyntax when
                model?.GetOperation(declaration) is IAnonymousFunctionOperation anonymous =>
                anonymous.Body,
            CompilationUnitSyntax when model?.GetOperation(declaration) is { } operation =>
                operation,
            _ => null
        };
        return new CallableSeed(NormalizePartial(method), declaration, model, body);
    }
    private ImmutableDictionary<IMethodSymbol, string> CreateCallableIds(
        ImmutableArray<CallableSeed> callables) {
        var trees = _compilation.SyntaxTrees.Select(
            static (tree, ordinal) => (tree, ordinal)).ToDictionary(
            static value => value.tree, static value => value.ordinal);
        var ordinals = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        foreach (var group in callables
                     .Where(static seed => seed.Method.MethodKind is
                         MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                     .GroupBy(static seed => seed.Method.ContainingSymbol!,
                         SymbolEqualityComparer.Default))
            foreach (var (seed, ordinal) in group
                .OrderBy(seed => seed.Declaration == null ? int.MaxValue :
                    trees[seed.Declaration.SyntaxTree])
                .ThenBy(static seed => seed.Declaration?.SpanStart ?? int.MaxValue)
                .Select(static (seed, ordinal) => (seed, ordinal)))
                ordinals.Add(seed.Method, ordinal);
        var ids = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var seed in callables) Resolve(seed.Method);
        return ids.ToImmutableDictionary(SymbolEqualityComparer.Default);

        string Resolve(IMethodSymbol method) {
            method = NormalizePartial(method);
            if (ids.TryGetValue(method, out var id)) return id;
            if (method.MethodKind is not
                (MethodKind.AnonymousFunction or MethodKind.LocalFunction))
                id = SemanticClaimIdentity.CreateCallableId(method);
            else {
                var parent = method.ContainingSymbol;
                var parentId = parent is IMethodSymbol parentMethod
                    ? Resolve(parentMethod)
                    : SemanticClaimIdentity.CreateContainerId(parent);
                id = SemanticClaimIdentity.CreateNestedCallableId(
                    parentId, method, ordinals[method]);
            }
            ids.Add(method, id);
            return id;
        }
    }
    private bool TryResolveCompanion(IMethodSymbol target, out IMethodSymbol? source) {
        source = null;
        if (target.MethodKind != MethodKind.Ordinary) return false;
        var companions = _companions
            .Where(companion => ContractForSymbolMatcher.TargetsType(companion.ContractTarget, target.ContainingType))
            .ToImmutableArray();
        if (companions.Length != 1 ||
            !ContractForSymbolMatcher.CompanionTypeMatches(companions[0].Type, companions[0].ContractTarget))
            return false;
        var descriptor = companions[0];
        var signatureTarget = descriptor.ContractTarget.IsOpen
            ? target.OriginalDefinition
            : target.ConstructedFrom;
        var matches = descriptor.Type.GetMembers(target.Name).OfType<IMethodSymbol>()
            .Where(IsExplicitOrdinaryMethod)
            .Where(candidate => ContractForSymbolMatcher.MemberSignaturesMatch(signatureTarget, candidate))
            .ToImmutableArray();
        if (matches.Length != 1 ||
            target.ContainingType.GetMembers().OfType<IMethodSymbol>()
                .Where(IsExplicitOrdinaryMethod)
                .Count(candidate => ContractForSymbolMatcher.MemberSignaturesMatch(candidate, matches[0])) != 1)
            return false;
        source = NormalizePartial(matches[0]);
        return true;
    }
    private bool IsCompanionType(INamedTypeSymbol type) =>
        _companions.Any(companion => SymbolEqualityComparer.Default.Equals(
            companion.Type.OriginalDefinition, type.OriginalDefinition));
    private static bool IsExplicitOrdinaryMethod(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared;
    private static ImmutableArray<CompanionDescriptor> DiscoverCompanions(Compilation compilation) {
        var contractFor = compilation.GetTypeByMetadataName(ContractForSymbolMatcher.AttributeMetadataName);
        if (contractFor == null) return [];
        var companions = ImmutableArray.CreateBuilder<CompanionDescriptor>();
        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace)) {
            var attributes = ContractForSymbolMatcher.GetAttributes(type, contractFor);
            if (attributes.Length == 1 &&
                ContractForSymbolMatcher.TryGetTarget(attributes[0], out var target))
                companions.Add(new CompanionDescriptor(type, target));
        }
        return companions.ToImmutable();
    }
    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol value) {
        foreach (var type in value.GetTypeMembers()) {
            yield return type;
            foreach (var nested in GetAllTypes(type)) yield return nested;
        }
        if (value is INamespaceSymbol @namespace)
            foreach (var child in @namespace.GetNamespaceMembers())
                foreach (var type in GetAllTypes(child))
                    yield return type;
    }
    private static SyntaxNode? GetDeclaration(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OrderBy(static syntax => syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static syntax => syntax.SpanStart)
            .FirstOrDefault();
    private static Location GetCallableLocation(IMethodSymbol method, SyntaxNode? declaration) =>
        declaration?.GetLocation() ?? method.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
    private static Location GetAttributeLocation(AttributeData attribute, IMethodSymbol target) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
        ?? target.Locations.FirstOrDefault(static location => location.IsInSource)
        ?? Location.None;
    private static (string Path, int Start) GetAttributeOrder(AttributeData attribute) {
        var syntax = attribute.ApplicationSyntaxReference;
        return (syntax?.SyntaxTree.FilePath ?? string.Empty, syntax?.Span.Start ?? int.MaxValue);
    }

    private static WorkerSourceLocation ToSourceLocation(Location location) {
        if (!location.IsInSource) return new WorkerSourceLocation();
        var mapped = location.GetMappedLineSpan();
        var path = string.IsNullOrEmpty(mapped.Path) ? location.SourceTree?.FilePath ?? string.Empty : mapped.Path;
        if (string.IsNullOrEmpty(path)) path = "<compiler-generated>";
        return new WorkerSourceLocation {
            Path = path,
            Start = location.SourceSpan.Start,
            Length = location.SourceSpan.Length,
            Line = mapped.StartLinePosition.Line + 1,
            Column = mapped.StartLinePosition.Character + 1
        };
    }

    private static IMethodSymbol NormalizePartial(IMethodSymbol method) =>
        method.PartialImplementationPart ?? method;

    private readonly record struct ClaimCandidate(
        string PredicateFingerprint, Location Location, WorkerClaimEvidence Evidence,
        IInvocationOperation? SourceOperation, AttributeData? SourceAttribute,
        ContractClausePlacement? Placement);

    private readonly record struct AssumptionCandidate(WorkerAssumptionKind Kind, string Fingerprint);
    private readonly record struct CallableSeed(IMethodSymbol Method,
        SyntaxNode? Declaration, SemanticModel? Model, IOperation? Body);

    private readonly record struct CompanionDescriptor(INamedTypeSymbol Type,
        (INamedTypeSymbol Target, bool IsOpen) ContractTarget) {
        internal INamedTypeSymbol Target => ContractTarget.Target;
    }

    private sealed class SelectedAttributeSymbols(Compilation compilation) {
        private readonly INamedTypeSymbol? _contractApi =
            compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");

        internal bool IsClosedReturnAttribute(AttributeData attribute) =>
            GetName(attribute) is "NotNullAttribute" or "PositiveAttribute" or "InRangeAttribute";

        internal ImmutableArray<WorkerSelectedFeature> GetSelectedFeatures(IMethodSymbol method) {
            var contract = false;
            var effects = false;
            foreach (var attribute in GetAttributes(method)) {
                var name = GetName(attribute);
                contract |= IsContract(name) || IsControl(name);
                effects |= IsEffect(name) || IsControl(name);
            }
            if (GetControlAttributes(method).Any(value => IsControl(GetName(value.Attribute))))
                contract = effects = true;
            var result = ImmutableArray.CreateBuilder<WorkerSelectedFeature>(2);
            if (effects) result.Add(WorkerSelectedFeature.Effects);
            if (contract) result.Add(WorkerSelectedFeature.Contracts);
            return result.ToImmutable();
        }

        internal IEnumerable<(ISymbol Scope, AttributeData Attribute)> GetTrustedAttributes(IMethodSymbol method) =>
            GetControlAttributes(method).Where(value =>
                GetName(value.Attribute) == "SharpProofTrustedAttribute");

        private string? GetName(AttributeData attribute) {
            var type = attribute.AttributeClass;
            return type != null && _contractApi != null &&
                   SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _contractApi.ContainingAssembly) &&
                   SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, _contractApi.ContainingNamespace)
                ? type.MetadataName
                : null;
        }

        private static bool IsContract(string? name) =>
            name is "NotNullAttribute" or "PositiveAttribute" or "InRangeAttribute";

        private static bool IsEffect(string? name) => name is
            "AllowedCapabilitiesAttribute" or "AllowedExceptionsAttribute" or
            "DoesNotThrowAttribute" or "EffectContractAttribute" or
            "EnforcePureAttribute" or "ZeroAllocationsAttribute";

        private static bool IsControl(string? name) =>
            name == "SharpProofTrustedAttribute";

        private static IEnumerable<AttributeData> GetAttributes(IMethodSymbol method) => method.GetAttributes()
            .Concat(method.GetReturnTypeAttributes())
            .Concat(method.Parameters.SelectMany(static parameter => parameter.GetAttributes()))
            .Concat(method.AssociatedSymbol?.GetAttributes() ?? []);

        private static IEnumerable<(ISymbol Scope, AttributeData Attribute)> GetControlAttributes(IMethodSymbol method) {
            foreach (var scope in EnumerateControlScopes(method))
                foreach (var attribute in scope.GetAttributes())
                    yield return (scope, attribute);
        }

        private static IEnumerable<ISymbol> EnumerateControlScopes(IMethodSymbol method) {
            yield return method;
            if (method.AssociatedSymbol != null) yield return method.AssociatedSymbol;
            for (var type = method.ContainingType; type != null; type = type.ContainingType)
                yield return type;
            yield return method.ContainingAssembly;
        }
    }
}

internal sealed record ClaimManifestBuildResult(
    WorkerClaimManifest Manifest, ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> Targets);

internal sealed record ManifestCallableTarget(
    IMethodSymbol Method, SyntaxNode? Declaration, SemanticModel? SemanticModel,
    WorkerCallableManifestEntry Entry, ImmutableArray<ManifestClaim> Claims,
    ImmutableArray<WorkerAssumptionEvidence> Assumptions,
    bool IsVerifierSupported) {
    internal BaseMethodDeclarationSyntax VerifierDeclaration =>
        (BaseMethodDeclarationSyntax)Declaration!;
    internal SemanticModel VerifierSemanticModel => SemanticModel!;
}

internal sealed record ManifestClaim(
    WorkerClaimManifestEntry Entry, IInvocationOperation? SourceOperation, AttributeData? SourceAttribute,
    ContractClausePlacement? Placement);
