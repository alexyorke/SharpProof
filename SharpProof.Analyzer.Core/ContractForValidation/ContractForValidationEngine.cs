namespace SharpProof.ContractForValidation;

internal static class ContractForValidationEngine
{
    internal static ImmutableArray<Diagnostic> Validate(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.IsDefaultOrEmpty)
        {
            return [];
        }

        var diagnostics = new List<Diagnostic>();
        var contractFor = ContractSelectionInventory.ForCompilation(compilation).ContractFor;
        if (contractFor == null)
        {
            foreach (var candidate in candidates
                         .Distinct((IEqualityComparer<INamedTypeSymbol>)
                             SymbolEqualityComparer.Default)
                         .OrderBy(static candidate =>
                             candidate.Locations.FirstOrDefault()?.SourceTree?.FilePath,
                             StringComparer.Ordinal)
                         .ThenBy(static candidate =>
                             candidate.Locations.FirstOrDefault()?.SourceSpan.Start ??
                             int.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(At(
                    ContractForDiagnosticDescriptors.InvalidTarget,
                    ContractForCompanionValidator.GetSourceLocation(
                        candidate, Location.None),
                    candidate.Name));
            }
            return Order(diagnostics);
        }

        var companions = ResolveCompanions(
            contractFor, candidates, diagnostics, cancellationToken);
        var clauses = ContractClauseInventoryBuilder.ForCompilation(compilation);
        var overlapping = FindOverlappingCompanions(
            companions,
            ContractForSymbolMatcher.DiscoverCompanions(
                compilation, cancellationToken),
            cancellationToken);
        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!overlapping.Contains(companion))
            {
                ContractForCompanionValidator.Validate(
                    companion,
                    clauses,
                    diagnostics,
                    cancellationToken);
                continue;
            }
            diagnostics.Add(At(ContractForDiagnosticDescriptors.DuplicateCompanion,
                companion.AttributeLocation, companion.Target.Name));
        }
        return Order(diagnostics);
    }

    internal static ImmutableArray<INamedTypeSymbol> FindCandidates(
        Compilation compilation,
        Func<SyntaxTree, bool> includeTree,
        CancellationToken cancellationToken)
    {
        var contractFor = ContractSelectionInventory.ForCompilation(
            compilation).ContractFor;
        if (contractFor == null)
        {
            return [];
        }

        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!includeTree(tree))
            {
                continue;
            }
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, tree);
            foreach (var declaration in tree.GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<TypeDeclarationSyntax>()
                         .Where(static declaration =>
                             declaration.AttributeLists.Count != 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetDeclaredSymbol(
                        declaration,
                        cancellationToken) is INamedTypeSymbol symbol &&
                    ContractForSymbolMatcher.GetAttributes(
                        symbol,
                        contractFor).Length != 0)
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
        return [.. diagnostics
            .OrderBy(static diagnostic =>
                diagnostic.Location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal)];
    }

    private static HashSet<ResolvedCompanion> FindOverlappingCompanions(
        ImmutableArray<ResolvedCompanion> companions,
        ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> discovered,
        CancellationToken cancellationToken)
    {
        var overlapping = new HashSet<ResolvedCompanion>();
        foreach (var source in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (discovered.Any(candidate =>
                    !SymbolEqualityComparer.Default.Equals(
                        candidate.Type, source.Companion) &&
                    ContractForSymbolMatcher.TargetsOverlap(
                        candidate.ContractTarget,
                        (source.Target, source.IsOpenTarget))))
            {
                overlapping.Add(source);
            }
        }
        return overlapping;
    }

    private static ImmutableArray<ResolvedCompanion> ResolveCompanions(
        INamedTypeSymbol contractFor,
        ImmutableArray<INamedTypeSymbol> candidates,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedCompanion>();
        foreach (var companion in candidates.Distinct(
                     (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = ContractForSymbolMatcher.GetAttributes(companion, contractFor);
            var fallback = ContractForCompanionValidator.GetSourceLocation(
                companion, Location.None);
            if (attributes.Length != 1)
            {
                var location = attributes.FirstOrDefault() is { } first
                    ? GetAttributeLocation(first, fallback, cancellationToken)
                    : fallback;
                diagnostics.Add(At(
                    ContractForDiagnosticDescriptors.InvalidTarget, location, companion.Name));
                continue;
            }
            var attribute = attributes[0];
            var attributeLocation = GetAttributeLocation(attribute, fallback, cancellationToken);
            if (!ContractForSymbolMatcher.TryGetTarget(attribute, out var target))
            {
                diagnostics.Add(At(ContractForDiagnosticDescriptors.InvalidTarget,
                    attributeLocation, companion.Name));
                continue;
            }
            result.Add(new ResolvedCompanion(
                companion, target.Target, attributeLocation, target.IsOpen));
        }
        return result.ToImmutable();
    }

    private static Diagnostic At(
        DiagnosticDescriptor descriptor,
        Location location,
        params object?[] arguments)
    {
        return Diagnostic.Create(descriptor, location, arguments);
    }

    private static Location GetAttributeLocation(
        AttributeData attribute,
        Location fallback,
        CancellationToken cancellationToken)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? fallback;
    }

}

internal sealed class ResolvedCompanion(
    INamedTypeSymbol companion,
    INamedTypeSymbol target,
    Location attributeLocation,
    bool isOpenTarget)
{
    internal INamedTypeSymbol Companion { get; } = companion;
    internal INamedTypeSymbol Target { get; } = target;
    internal Location AttributeLocation { get; } = attributeLocation;
    internal bool IsOpenTarget { get; } = isOpenTarget;
}
