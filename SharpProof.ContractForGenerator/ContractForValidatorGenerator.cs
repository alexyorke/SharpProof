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
        var contractFor = ContractSelectionInventory.ForCompilation(compilation).ContractFor;
        if (contractFor == null)
        {
            return;
        }

        var diagnostics = new List<Diagnostic>();
        var companions = ResolveCompanions(
            contractFor, candidates, diagnostics, context.CancellationToken);
        var clauses = ContractClauseInventoryBuilder.ForCompilation(compilation);
        foreach (var group in companions.GroupBy(
                     static companion => companion.Target,
                     (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var resolved = group.ToImmutableArray();
            if (resolved.Length == 1)
            {
                ValidateCompanion(resolved[0], clauses, diagnostics, context.CancellationToken);
                continue;
            }
            foreach (var duplicate in resolved)
            {
                diagnostics.Add(At(GeneratedDiagnosticDescriptors.DuplicateCompanion,
                    duplicate.AttributeLocation, duplicate.Target.Name));
            }
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
            var fallback = GetSourceLocation(companion, Location.None);
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

    private static void ValidateCompanion(
        ResolvedCompanion companion,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Companion, (companion.Target, companion.IsOpenTarget)))
        {
            diagnostics.Add(At(GeneratedDiagnosticDescriptors.InvalidCompanionType,
                companion.AttributeLocation, companion.Companion.Name, companion.Target.Name));
            return;
        }
        var targets = ContractForSymbolMatcher.GetOrdinaryMethods(companion.Target);
        var candidates = ContractForSymbolMatcher.GetOrdinaryMethods(companion.Companion);
        var comparer = (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default;
        var byTarget = targets.ToDictionary(
            static target => target,
            target => candidates.Where(candidate =>
                ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate)).ToImmutableArray(),
            comparer);
        var byCandidate = candidates.ToDictionary(
            static candidate => candidate,
            candidate => targets.Where(target =>
                ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate)).ToImmutableArray(),
            comparer);
        var diagnosed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        void Diagnose(DiagnosticDescriptor descriptor, ISymbol symbol, params object?[] arguments)
        {
            diagnostics.Add(At(descriptor,
                GetSourceLocation(symbol, companion.AttributeLocation), arguments));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byTarget[target];
            if (matches.Length > 1)
            {
                Diagnose(GeneratedDiagnosticDescriptors.AmbiguousMember, target, target.Name);
                diagnosed.UnionWith(matches);
                continue;
            }
            if (matches.Length == 1)
            {
                continue;
            }

            var mismatches = candidates.Where(candidate =>
                    string.Equals(candidate.Name, target.Name, StringComparison.Ordinal) &&
                    byCandidate[candidate].IsDefaultOrEmpty)
                .ToImmutableArray();
            if (mismatches.IsDefaultOrEmpty)
            {
                Diagnose(GeneratedDiagnosticDescriptors.MissingMember,
                    target, target.Name, companion.Companion.Name);
                continue;
            }
            foreach (var mismatch in mismatches)
            {
                if (diagnosed.Add(mismatch))
                {
                    Diagnose(GeneratedDiagnosticDescriptors.SignatureMismatch,
                        mismatch, mismatch.Name);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byCandidate[candidate];
            if (matches.Length > 1 && diagnosed.Add(candidate))
            {
                Diagnose(GeneratedDiagnosticDescriptors.AmbiguousMember, candidate, candidate.Name);
            }
            else if (matches.IsDefaultOrEmpty &&
                     targets.Any(target => string.Equals(
                         target.Name, candidate.Name, StringComparison.Ordinal)) &&
                     diagnosed.Add(candidate))
            {
                Diagnose(GeneratedDiagnosticDescriptors.SignatureMismatch, candidate, candidate.Name);
            }
        }

        foreach (var target in targets)
        {
            var matches = byTarget[target];
            if (matches.Length != 1 || byCandidate[matches[0]].Length != 1)
            {
                continue;
            }

            ValidateBody(ContractClauseInventoryBuilder.NormalizeCallable(matches[0]),
                clauses, diagnostics, companion.AttributeLocation, cancellationToken);
        }
    }

    private static void ValidateBody(
        IMethodSymbol method,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        Location fallback,
        CancellationToken cancellationToken)
    {
        var inventory = clauses.Create(method);
        if (inventory.ImplementationBody == null)
        {
            diagnostics.Add(At(GeneratedDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, fallback), method.Name));
            return;
        }
        foreach (var clause in inventory.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
            {
                diagnostics.Add(At(GeneratedDiagnosticDescriptors.InvalidClausePlacement,
                    clause.Location, clause.Kind, method.Name, clause.Placement));
            }
        }
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

    private static Location GetSourceLocation(ISymbol symbol, Location fallback)
    {
        return symbol.Locations.Where(static location => location.IsInSource)
            .OrderBy(static location => location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static location => location.SourceSpan.Start)
            .FirstOrDefault() ?? fallback;
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
