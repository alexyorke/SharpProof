namespace SharpProof.ContractForValidation;

internal static class ContractForValidationEngine
{
    internal static ImmutableArray<Diagnostic> Validate(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates,
        CancellationToken cancellationToken,
        Func<SyntaxTree, bool>? includeTree = null)
    {
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.IsDefaultOrEmpty)
        {
            return [];
        }

        var diagnostics = ImmutableList<Diagnostic>.Empty;
        void Report(Diagnostic diagnostic)
        {
            diagnostic = RepositoryParameterGuard.NotNull(diagnostic, nameof(diagnostic));
            diagnostics = diagnostics.Add(diagnostic);
        }
        var contractFor = ContractSelectionInventory.ForCompilation(compilation).ContractFor;
        if (contractFor == null)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(Diagnostic.Create(
                    ContractForDiagnosticDescriptors.InvalidTarget,
                    ContractForCompanionValidator.GetSourceLocation(
                        candidate, compilation, Location.None),
                    candidate.Name));
            }
            return Order(diagnostics);
        }

        var companions = ResolveCompanions(
            contractFor,
            compilation,
            candidates,
            Report,
            includeTree,
            cancellationToken);
        var relationships = ContractForSymbolMatcher
            .ClassifyCompanionRelationships(
                ContractForSymbolMatcher.DiscoverCompanionRelationships(
                    compilation,
                    cancellationToken),
                cancellationToken);
        var accepted = ImmutableArray.CreateBuilder<ResolvedCompanion>();
        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ContractForCompanionValidator.ReportRelationship(
                    companion,
                    relationships,
                    Report))
            {
                accepted.Add(companion);
            }
        }

        companions = accepted.ToImmutable();
        var clauses = ContractClauseInventoryBuilder.ForCompilation(compilation);
        var overlapping = FindOverlappingCompanions(
            companions,
            relationships.Accepted,
            cancellationToken);
        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!overlapping.Contains(companion))
            {
                ContractForCompanionValidator.ReportValidation(
                    companion,
                    compilation,
                    clauses,
                    Report,
                    cancellationToken);
                continue;
            }
            Report(Diagnostic.Create(ContractForDiagnosticDescriptors.DuplicateCompanion,
                companion.AttributeLocation, companion.Target.Name));
        }

        // Metadata companions participate in overlap detection, but analyzer
        // diagnostics may only point into the compilation being analyzed. The
        // source participant above is the actionable location for the group.
        return Order(diagnostics);
    }

    internal static ImmutableArray<INamedTypeSymbol> FindCandidates(
        Compilation compilation,
        Func<SyntaxTree, bool> includeTree,
        CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        ArgumentNullGuard.NotNull(includeTree, nameof(includeTree));
        var selections = ContractSelectionInventory.ForCompilation(
            compilation);

        var candidates = ImmutableList<INamedTypeSymbol>.Empty;
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!includeTree(tree))
            {
                continue;
            }
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetRootSemanticModel(compilation, tree);
            foreach (var declaration in tree.GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<TypeDeclarationSyntax>()
                         .Where(static declaration =>
                             declaration is null ? throw new System.ArgumentNullException(nameof(declaration)) : (declaration.AttributeLists.Count != 0)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(
                        declaration,
                        cancellationToken) is INamedTypeSymbol symbol &&
                    symbol.GetAttributes().Any(
                        selections.IsContractForCandidate))
                {
                    candidates = candidates.Add(symbol);
                }
            }
        }
        return candidates.Distinct(
                (IEqualityComparer<INamedTypeSymbol>)
                    SymbolEqualityComparer.Default)
            .ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> Order(
        IEnumerable<Diagnostic> diagnostics)
    {
        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));
        return [.. diagnostics
            .OrderBy(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Location.SourceTree?.FilePath),
                StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Location.SourceSpan.Start))
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id), StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture)),
                StringComparer.Ordinal)];
    }

    private static ImmutableHashSet<ResolvedCompanion> FindOverlappingCompanions(
        ImmutableArray<ResolvedCompanion> companions,
        ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> discovered,
        CancellationToken cancellationToken)
    {


        RepositoryParameterGuard.ThrowIfInvalid(companions.IsDefault, nameof(companions));
        RepositoryParameterGuard.ThrowIfInvalid(discovered.IsDefault, nameof(discovered));
        var overlapping = ImmutableHashSet<ResolvedCompanion>.Empty;
        foreach (var source in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (discovered.Any(candidate =>
                    candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (!SymbolEqualityComparer.Default.Equals(
                        candidate.Type, source.Companion) &&
                    ContractForSymbolMatcher.TargetsOverlap(
                        candidate.ContractTarget,
                        (source.Target, source.IsOpenTarget)))))
            {
                overlapping = overlapping.Add(source);
            }
        }
        return overlapping;
    }

    private static ImmutableArray<ResolvedCompanion> ResolveCompanions(
        INamedTypeSymbol contractFor,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates,
        Action<Diagnostic> diagnostics,
        Func<SyntaxTree, bool>? includeTree,
        CancellationToken cancellationToken)
    {

        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        contractFor = RepositoryParameterGuard.NotNull(contractFor, nameof(contractFor));
        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));
        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        var result = ImmutableArray.CreateBuilder<ResolvedCompanion>();
        foreach (var companion in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = ContractForSymbolMatcher.GetAttributes(
                companion, contractFor, includeTree);
            var fallback = ContractForCompanionValidator.GetSourceLocation(
                companion, compilation, Location.None);
            if (attributes.Count != 1)
            {
                var location = attributes.First is { } first
                    ? GetAttributeLocation(
                        first,
                        compilation,
                        fallback,
                        cancellationToken)
                    : fallback;
                diagnostics(Diagnostic.Create(
                    ContractForDiagnosticDescriptors.InvalidTarget, location, companion.Name));
                continue;
            }
            var attribute = attributes.First ?? throw new System.InvalidOperationException(
                "A single counted contract attribute must have a first attribute.");
            var attributeLocation = GetAttributeLocation(
                attribute,
                compilation,
                fallback,
                cancellationToken);
            if (!ContractForSymbolMatcher.TryGetTarget(attribute, out var target))
            {
                diagnostics(Diagnostic.Create(ContractForDiagnosticDescriptors.InvalidTarget,
                    attributeLocation, companion.Name));
                continue;
            }
            result.Add(new ResolvedCompanion(
                companion, target.Target, attributeLocation, target.IsOpen));
        }
        return result.ToImmutable();
    }

    private static Location GetAttributeLocation(
        AttributeData attribute,
        Compilation compilation,
        Location fallback,
        CancellationToken cancellationToken)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        fallback = RepositoryParameterGuard.NotNull(fallback, nameof(fallback));
        var location = attribute.ApplicationSyntaxReference?
            .GetSyntax(cancellationToken)
            .GetLocation();
        return location?.SourceTree is { } tree &&
               compilation.ContainsSyntaxTree(tree)
            ? location
            : fallback;
    }

}

internal sealed class ResolvedCompanion(
    INamedTypeSymbol companion,
    INamedTypeSymbol target,
    Location attributeLocation,
    bool isOpenTarget)
{
    internal INamedTypeSymbol Companion { get; } = (companion ?? throw new System.ArgumentNullException(nameof(companion)));
    internal INamedTypeSymbol Target { get; } = (target ?? throw new System.ArgumentNullException(nameof(target)));
    internal Location AttributeLocation { get; } = (attributeLocation ?? throw new System.ArgumentNullException(nameof(attributeLocation)));
    internal bool IsOpenTarget { get; } = isOpenTarget;
}
