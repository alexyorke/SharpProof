namespace SharpProof.Specs;

public enum ApiSpecResolutionFailureKind {
    MissingContainingType,
    AmbiguousContainingType,
    MissingMember,
    AmbiguousMember,
    DuplicateResolvedSymbol
}

public sealed record ApiSpecResolutionFailure(
    SpecId Spec,
    string WitnessIdentifier,
    ApiSpecResolutionFailureKind Kind,
    string Detail);

public sealed record ResolvedApiSpec(ApiSpecTemplate Template, ISymbol Symbol);

public enum ApiSpecLookupStatus {
    Resolved,
    Unknown
}

public enum ApiSpecLookupFailureKind {
    UnspecifiedMember
}

public sealed record ApiSpecLookupFailure(
    ApiSpecLookupFailureKind Kind,
    string SymbolIdentifier,
    string Detail);

public sealed class ApiSpecLookupResult {
    private ApiSpecLookupResult(
        ApiSpecLookupStatus status,
        ResolvedApiSpec? spec,
        ApiSpecLookupFailure? failure) {
        Status = status;
        Spec = spec;
        Failure = failure;
    }

    public ApiSpecLookupStatus Status { get; }
    public ResolvedApiSpec? Spec { get; }
    public ApiSpecLookupFailure? Failure { get; }

    internal static ApiSpecLookupResult Resolved(ResolvedApiSpec spec) =>
        new(ApiSpecLookupStatus.Resolved, spec, null);

    internal static ApiSpecLookupResult Unknown(ApiSpecLookupFailure failure) =>
        new(ApiSpecLookupStatus.Unknown, null, failure);
}

public sealed class ResolvedApiSpecTable {
    private readonly ImmutableDictionary<ISymbol, ResolvedApiSpec> _specs;

    internal ResolvedApiSpecTable(
        ImmutableDictionary<ISymbol, ResolvedApiSpec> specs,
        ImmutableArray<ApiSpecResolutionFailure> failures) {
        _specs = specs;
        Failures = failures;
    }

    public ImmutableArray<ResolvedApiSpec> Specs => [.. _specs.Values
        .OrderBy(static spec => spec.Template.Id.Value)];
    public ImmutableArray<ApiSpecResolutionFailure> Failures { get; }
    public bool IsComplete => Failures.IsDefaultOrEmpty;

    public bool TryGet(
        ISymbol symbol,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolvedApiSpec? spec) {
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        var normalized = NormalizeSymbol(symbol);
        if (normalized != null) return _specs.TryGetValue(normalized, out spec);
        spec = null;
        return false;
    }

    public bool IsPureAndAllocationFree(IMethodSymbol method) =>
        TryGet(method, out var spec) &&
        spec.Template.Facets.Effects.Effects == SpecEffect.None &&
        spec.Template.Facets.Allocation.Behavior ==
            SpecAllocationBehavior.None;

    public ApiSpecLookupResult Lookup(ISymbol symbol) {
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        if (TryGet(symbol, out var spec)) return ApiSpecLookupResult.Resolved(spec);
        var identifier = symbol.GetDocumentationCommentId() ?? symbol.MetadataName;
        return ApiSpecLookupResult.Unknown(new ApiSpecLookupFailure(
            ApiSpecLookupFailureKind.UnspecifiedMember,
            identifier,
            "No resolved API spec exists for this original definition."));
    }

    internal static ISymbol? NormalizeSymbol(ISymbol symbol) => symbol switch {
        IMethodSymbol method => (method.ReducedFrom ?? method).OriginalDefinition,
        IPropertySymbol property => property.GetMethod?.OriginalDefinition,
        _ => symbol.OriginalDefinition
    };
}

public sealed class ApiSpecResolver(ApiSpecTable table) {
    private readonly ConditionalWeakTable<Compilation, ResolvedApiSpecTable> _cache = new();
    private readonly ApiSpecTable _table = table ?? throw new ArgumentNullException(nameof(table));

    public ResolvedApiSpecTable Resolve(Compilation compilation) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        return _cache.GetValue(compilation, Build);
    }

    private ResolvedApiSpecTable Build(Compilation compilation) {
        var failures = ImmutableArray.CreateBuilder<ApiSpecResolutionFailure>();
        var candidates = new Dictionary<ISymbol, List<ResolvedCandidate>>(SymbolEqualityComparer.Default);
        foreach (var template in _table.Templates) {
            var resolved = ResolveTemplate(compilation, template);
            if (resolved.Failure != null) {
                failures.Add(resolved.Failure);
                continue;
            }
            var symbol = resolved.Symbol!;
            if (!candidates.TryGetValue(symbol, out var rows)) {
                rows = [];
                candidates.Add(symbol, rows);
            }
            rows.Add(new ResolvedCandidate(template, symbol));
        }
        var specs = ImmutableDictionary.CreateBuilder<ISymbol, ResolvedApiSpec>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates) {
            if (candidate.Value.Count == 1) {
                var row = candidate.Value[0];
                specs.Add(candidate.Key, new ResolvedApiSpec(row.Template, row.Symbol));
                continue;
            }
            foreach (var row in candidate.Value)
                failures.Add(Failure(
                    row.Template,
                    ApiSpecResolutionFailureKind.DuplicateResolvedSymbol,
                    "Multiple spec rows resolved to the same original symbol."));
        }
        return new ResolvedApiSpecTable(specs.ToImmutable(), failures.ToImmutable());
    }

    private static (ISymbol? Symbol, ApiSpecResolutionFailure? Failure) ResolveTemplate(
        Compilation compilation,
        ApiSpecTemplate template) {
        var target = template.Target;
        var containingType = compilation.GetTypeByMetadataName(target.ContainingTypeMetadataName);
        if (containingType == null) {
            var alternatives = compilation.GetTypesByMetadataName(target.ContainingTypeMetadataName);
            return alternatives.Length > 1
                ? (null, Failure(
                    template,
                    ApiSpecResolutionFailureKind.AmbiguousContainingType,
                    "Multiple referenced assemblies define the containing metadata type."))
                : (null, Failure(
                    template,
                    ApiSpecResolutionFailureKind.MissingContainingType,
                    "The containing metadata type is unavailable in this compilation."));
        }
        var normalized = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in DocumentationCommentId.GetSymbolsForDeclarationId(
                     target.DocumentationCommentId,
                     compilation)) {
            if (!SymbolEqualityComparer.Default.Equals(
                    candidate.ContainingType?.OriginalDefinition,
                    containingType.OriginalDefinition) ||
                !MatchesTarget(candidate, target))
                continue;
            var symbol = ResolvedApiSpecTable.NormalizeSymbol(candidate);
            if (symbol != null) normalized.Add(symbol);
        }
        return normalized.Count switch {
            0 => (null, Failure(
                template,
                ApiSpecResolutionFailureKind.MissingMember,
                "The documentation identifier did not resolve to the declared member shape.")),
            1 => (normalized.Single(), null),
            _ => (null, Failure(
                template,
                ApiSpecResolutionFailureKind.AmbiguousMember,
                "The documentation identifier resolved to multiple original definitions."))
        };
    }

    private static bool MatchesTarget(ISymbol symbol, ApiSpecTarget target) => target.MemberKind switch {
        SpecTargetMemberKind.Constructor => symbol is IMethodSymbol {
            MethodKind: MethodKind.Constructor
        } constructor &&
            !constructor.IsStatic &&
            string.Equals(constructor.MetadataName, target.MemberName, StringComparison.Ordinal) &&
            constructor.Arity == target.GenericArity &&
            constructor.Parameters.Length == target.ParameterTypes.Length,
        SpecTargetMemberKind.Method => symbol is IMethodSymbol {
            MethodKind: MethodKind.Ordinary
        } method &&
            method.IsStatic == target.IsStatic &&
            string.Equals(method.Name, target.MemberName, StringComparison.Ordinal) &&
            method.Arity == target.GenericArity &&
            method.Parameters.Length == target.ParameterTypes.Length,
        SpecTargetMemberKind.PropertyGet => symbol is IPropertySymbol property &&
            property.GetMethod != null &&
            property.IsStatic == target.IsStatic &&
            string.Equals(property.Name, target.MemberName, StringComparison.Ordinal) &&
            property.Parameters.Length == target.ParameterTypes.Length,
        _ => false
    };

    private static ApiSpecResolutionFailure Failure(
        ApiSpecTemplate template,
        ApiSpecResolutionFailureKind kind,
        string detail) => new(
        template.Id,
        template.Target.WitnessIdentifier,
        kind,
        detail);

    private sealed record ResolvedCandidate(ApiSpecTemplate Template, ISymbol Symbol);
}
