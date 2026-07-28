namespace SharpProof.ContractForGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ContractForValidatorGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ContractForSymbolMatcher.AttributeMetadataName,
                static (_, cancellationToken) => {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                },
                static (attributeContext, cancellationToken) => {
                    cancellationToken.ThrowIfCancellationRequested();
                    return attributeContext.TargetSymbol as INamedTypeSymbol;
                })
            .Where(static candidate => candidate != null)
            .Select(static (candidate, _) => candidate!)
            .Collect()
            .WithTrackingName("ContractForCandidates");
        var input = context.CompilationProvider
            .Combine(candidates)
            .WithTrackingName("ContractForValidationInput");
        context.RegisterSourceOutput(
            input,
            static (productionContext, value) =>
                Execute(
                    productionContext,
                    value.Left,
                    value.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidateSymbols) {
        context.CancellationToken.ThrowIfCancellationRequested();
        var contractFor = compilation.GetTypeByMetadataName(
            ContractForSymbolMatcher.AttributeMetadataName);
        if (contractFor == null) return;

        var diagnostics = new List<Diagnostic>();
        var companions = ResolveCompanions(
            contractFor,
            candidateSymbols,
            diagnostics,
            context.CancellationToken);
        var contractClauses = new ContractClauseInventoryBuilder(compilation);
        var groups = new Dictionary<
            INamedTypeSymbol,
            List<ResolvedCompanion>>(SymbolEqualityComparer.Default);
        foreach (var companion in companions) {
            if (!groups.TryGetValue(companion.Target, out var group)) {
                group = [];
                groups.Add(companion.Target, group);
            }
            group.Add(companion);
        }

        foreach (var group in groups) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var ordered = group.Value.ToImmutableArray();
            if (ordered.Length != 1) {
                foreach (var duplicate in ordered)
                    diagnostics.Add(Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.DuplicateCompanion,
                        duplicate.AttributeLocation,
                        duplicate.Target.Name));
                continue;
            }
            ValidateCompanion(
                ordered[0],
                contractClauses,
                diagnostics,
                context.CancellationToken);
        }

        foreach (var diagnostic in diagnostics
                     .OrderBy(
                         static diagnostic =>
                             diagnostic.Location.SourceTree?.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static diagnostic =>
                         diagnostic.Location.SourceSpan.Start)
                     .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                     .ThenBy(
                         static diagnostic => diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                         StringComparer.Ordinal))
            context.ReportDiagnostic(diagnostic);
    }

    private static ImmutableArray<ResolvedCompanion> ResolveCompanions(
        INamedTypeSymbol contractFor,
        ImmutableArray<INamedTypeSymbol> candidateSymbols,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken) {
        var unique = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var candidate in candidateSymbols)
            unique.Add(candidate);
        var result = ImmutableArray.CreateBuilder<ResolvedCompanion>();
        foreach (var companion in unique) {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = ContractForSymbolMatcher.GetAttributes(
                companion,
                contractFor);
            var fallback = GetSourceLocation(companion, Location.None);
            if (attributes.Length != 1) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.InvalidTarget,
                    attributes.FirstOrDefault() is { } first
                        ? GetAttributeLocation(first, fallback, cancellationToken)
                        : fallback,
                    companion.Name));
                continue;
            }
            var attribute = attributes[0];
            var attributeLocation = GetAttributeLocation(
                attribute,
                fallback,
                cancellationToken);
            if (!ContractForSymbolMatcher.TryGetTarget(
                    attribute,
                    out var target)) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.InvalidTarget,
                    attributeLocation,
                    companion.Name));
                continue;
            }
            result.Add(new ResolvedCompanion(
                companion,
                target.Target,
                attributeLocation,
                target.IsOpen));
        }
        return result.ToImmutable();
    }

    private static void ValidateCompanion(
        ResolvedCompanion companion,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken) {
        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Companion,
                (companion.Target, companion.IsOpenTarget))) {
            diagnostics.Add(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidCompanionType,
                companion.AttributeLocation,
                companion.Companion.Name,
                companion.Target.Name));
            return;
        }

        var targetMethods = ContractForSymbolMatcher.GetOrdinaryMethods(
            companion.Target);
        var companionMethods = ContractForSymbolMatcher.GetOrdinaryMethods(
            companion.Companion);
        var symbolComparer =
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default;
        var matchesByTarget = targetMethods.ToDictionary(
            static target => target,
            target => companionMethods.Where(candidate =>
                    ContractForSymbolMatcher.MemberSignaturesMatch(
                        target,
                        candidate))
                .ToImmutableArray(),
            symbolComparer);
        var matchesByCompanion = companionMethods.ToDictionary(
            static candidate => candidate,
            candidate => targetMethods.Where(target =>
                    ContractForSymbolMatcher.MemberSignaturesMatch(
                        target,
                        candidate))
                .ToImmutableArray(),
            symbolComparer);

        var diagnosedCandidates = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var target in targetMethods) {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = matchesByTarget[target];
            if (matches.Length > 1) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    GetSourceLocation(target, companion.AttributeLocation),
                    target.Name));
                foreach (var candidate in matches)
                    diagnosedCandidates.Add(candidate);
                continue;
            }
            if (matches.Length == 1) continue;

            var mismatches = companionMethods
                .Where(candidate =>
                    string.Equals(
                        candidate.Name,
                        target.Name,
                        StringComparison.Ordinal) &&
                    matchesByCompanion[candidate].IsDefaultOrEmpty)
                .ToImmutableArray();
            if (mismatches.IsDefaultOrEmpty) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.MissingMember,
                    GetSourceLocation(target, companion.AttributeLocation),
                    target.Name,
                    companion.Companion.Name));
            }
            else {
                foreach (var mismatch in mismatches)
                    if (diagnosedCandidates.Add(mismatch))
                        diagnostics.Add(Diagnostic.Create(
                            GeneratedDiagnosticDescriptors.SignatureMismatch,
                            GetSourceLocation(
                                mismatch,
                                companion.AttributeLocation),
                            mismatch.Name));
            }
        }

        foreach (var candidate in companionMethods) {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = matchesByCompanion[candidate];
            if (matches.Length > 1 &&
                diagnosedCandidates.Add(candidate))
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    GetSourceLocation(candidate, companion.AttributeLocation),
                    candidate.Name));
            else if (matches.IsDefaultOrEmpty &&
                     targetMethods.Any(target =>
                         string.Equals(
                             target.Name,
                             candidate.Name,
                             StringComparison.Ordinal)) &&
                     diagnosedCandidates.Add(candidate))
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SignatureMismatch,
                    GetSourceLocation(candidate, companion.AttributeLocation),
                    candidate.Name));
        }

        foreach (var target in targetMethods) {
            var matches = matchesByTarget[target];
            if (matches.Length != 1) continue;
            var candidate = matches[0];
            if (matchesByCompanion[candidate].Length != 1) continue;
            ValidateBody(
                ContractClauseInventoryBuilder.NormalizeCallable(candidate),
                clauses,
                diagnostics,
                companion.AttributeLocation,
                cancellationToken);
        }
    }

    private static void ValidateBody(
        IMethodSymbol method,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        Location fallback,
        CancellationToken cancellationToken) {
        var inventory = clauses.Create(method);
        if (inventory.ImplementationBody == null) {
            diagnostics.Add(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, fallback),
                method.Name));
            return;
        }
        foreach (var clause in inventory.Clauses) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.InvalidClausePlacement,
                    clause.Location,
                    clause.Kind,
                    method.Name,
                    clause.Placement));
        }
    }

    private static Location GetAttributeLocation(
        AttributeData attribute,
        Location fallback,
        CancellationToken cancellationToken) =>
        attribute.ApplicationSyntaxReference?
            .GetSyntax(cancellationToken)
            .GetLocation() ?? fallback;

    private static Location GetSourceLocation(
        ISymbol symbol,
        Location fallback) =>
        symbol.Locations
            .Where(static location => location.IsInSource)
            .OrderBy(
                static location => location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static location => location.SourceSpan.Start)
            .FirstOrDefault() ?? fallback;
}

internal sealed class ResolvedCompanion(
    INamedTypeSymbol companion,
    INamedTypeSymbol target,
    Location attributeLocation,
    bool isOpenTarget) {
    internal INamedTypeSymbol Companion { get; } = companion;
    internal INamedTypeSymbol Target { get; } = target;
    internal Location AttributeLocation { get; } = attributeLocation;
    internal bool IsOpenTarget { get; } = isOpenTarget;
}
