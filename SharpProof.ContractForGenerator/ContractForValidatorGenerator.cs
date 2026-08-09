namespace SharpProof.ContractForGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ContractForValidatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
                ContractSelectionInventory.ContractForMetadataName,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                },
                static (attributeContext, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return attributeContext.TargetSymbol as INamedTypeSymbol;
                })
            .Where(static candidate => candidate != null)
            .Select(static (candidate, _) => candidate!)
            .Collect()
            .WithTrackingName("ContractForCandidates");
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(candidates)
                .WithTrackingName("ContractForValidationInput"),
            static (output, value) => Execute(output, value.Left, value.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

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
                context.CancellationToken.ThrowIfCancellationRequested();
                context.ReportDiagnostic(At(
                    GeneratedDiagnosticDescriptors.InvalidTarget,
                    ContractForCompanionValidator.GetSourceLocation(
                        candidate, Location.None),
                    candidate.Name));
            }
            return;
        }

        var diagnostics = new List<Diagnostic>();
        var companions = ResolveCompanions(
            contractFor, candidates, diagnostics, context.CancellationToken);
        var clauses = ContractClauseInventoryBuilder.ForCompilation(compilation);
        var overlapping = FindOverlappingCompanions(
            companions,
            ContractForSymbolMatcher.DiscoverCompanions(
                compilation, context.CancellationToken),
            context.CancellationToken);
        foreach (var companion in companions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!overlapping.Contains(companion))
            {
                ContractForCompanionValidator.Validate(
                    companion,
                    clauses,
                    diagnostics,
                    context.CancellationToken);
                continue;
            }
            diagnostics.Add(At(GeneratedDiagnosticDescriptors.DuplicateCompanion,
                companion.AttributeLocation, companion.Target.Name));
        }
        foreach (var diagnostic in diagnostics
                     .OrderBy(static diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                     .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
                     .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                     .ThenBy(static diagnostic => diagnostic.GetMessage(
                         System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal))
        {
            context.ReportDiagnostic(diagnostic);
        }
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
                    GeneratedDiagnosticDescriptors.InvalidTarget, location, companion.Name));
                continue;
            }
            var attribute = attributes[0];
            var attributeLocation = GetAttributeLocation(attribute, fallback, cancellationToken);
            if (!ContractForSymbolMatcher.TryGetTarget(attribute, out var target))
            {
                diagnostics.Add(At(GeneratedDiagnosticDescriptors.InvalidTarget,
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
