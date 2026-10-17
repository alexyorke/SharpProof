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

        var diagnostics = new List<Diagnostic>();
        var contractFor = ContractSelectionInventory.ForCompilation(compilation).ContractFor;
        if (contractFor == null)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(Diagnostic.Create(
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
            diagnostics,
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
            if (ContractForCompanionValidator.ValidateRelationship(
                    companion,
                    relationships,
                    diagnostics))
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
                ContractForCompanionValidator.Validate(
                    companion,
                    compilation,
                    clauses,
                    diagnostics,
                    cancellationToken);
                continue;
            }
            diagnostics.Add(Diagnostic.Create(ContractForDiagnosticDescriptors.DuplicateCompanion,
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

        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
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
                    candidates.Add(symbol);
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
        if (diagnostics is null)
        {
            throw new System.ArgumentNullException(nameof(diagnostics));
        }
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

    private static HashSet<ResolvedCompanion> FindOverlappingCompanions(
        ImmutableArray<ResolvedCompanion> companions,
        ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> discovered,
        CancellationToken cancellationToken)
    {


        if (companions.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(companions));
        }
        if (discovered.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(discovered));
        }
        var overlapping = new HashSet<ResolvedCompanion>();
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
                overlapping.Add(source);
            }
        }
        return overlapping;
    }

    private static ImmutableArray<ResolvedCompanion> ResolveCompanions(
        INamedTypeSymbol contractFor,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates,
        List<Diagnostic> diagnostics,
        Func<SyntaxTree, bool>? includeTree,
        CancellationToken cancellationToken)
    {

        if (compilation is null)
        {
            throw new System.ArgumentNullException(nameof(compilation));
        }
        if (contractFor is null)
        {
            throw new System.ArgumentNullException(nameof(contractFor));
        }
        if (diagnostics is null)
        {
            throw new System.ArgumentNullException(nameof(diagnostics));
        }
        if (candidates.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(candidates));
        }
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
                diagnostics.Add(Diagnostic.Create(
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
                diagnostics.Add(Diagnostic.Create(ContractForDiagnosticDescriptors.InvalidTarget,
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
        if (attribute is null)
        {
            throw new System.ArgumentNullException(nameof(attribute));
        }
        if (compilation is null)
        {
            throw new System.ArgumentNullException(nameof(compilation));
        }
        if (fallback is null)
        {
            throw new System.ArgumentNullException(nameof(fallback));
        }
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
