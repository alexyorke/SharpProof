namespace SharpProof.ContractForGenerator;

/// <summary>
/// Validates the one-to-one member mapping and clause bodies for a resolved
/// contract companion.
/// </summary>
internal static class ContractForCompanionValidator
{
    internal static void Validate(
        ResolvedCompanion companion,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Companion,
                (companion.Target, companion.IsOpenTarget)))
        {
            diagnostics.Add(At(
                GeneratedDiagnosticDescriptors.InvalidCompanionType,
                companion.AttributeLocation,
                companion.Companion.Name,
                companion.Target.Name));
            return;
        }

        var targets = ContractForSymbolMatcher.GetOrdinaryMethods(companion.Target);
        var candidates = ContractForSymbolMatcher.GetOrdinaryMethods(companion.Companion);
        var comparer = (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default;
        var byTarget = targets.ToDictionary(
            static target => target,
            target => candidates.Where(candidate =>
                ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate))
                .ToImmutableArray(),
            comparer);
        var byCandidate = candidates.ToDictionary(
            static candidate => candidate,
            candidate => targets.Where(target =>
                ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate))
                .ToImmutableArray(),
            comparer);
        var targetSurfaceIsComplete = targets.All(target =>
            byTarget[target] is { Length: 1 } matches &&
            byCandidate[matches[0]].Length == 1);
        var diagnosed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        void Diagnose(
            DiagnosticDescriptor descriptor,
            ISymbol symbol,
            params object?[] arguments)
        {
            diagnostics.Add(At(
                descriptor,
                GetSourceLocation(symbol, companion.AttributeLocation),
                arguments));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byTarget[target];
            if (matches.Length > 1)
            {
                Diagnose(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    target,
                    target.Name);
                diagnosed.UnionWith(matches);
                continue;
            }

            if (matches.Length == 1)
            {
                continue;
            }

            var mismatches = candidates.Where(candidate =>
                    string.Equals(
                        candidate.Name,
                        target.Name,
                        StringComparison.Ordinal) &&
                    byCandidate[candidate].IsDefaultOrEmpty)
                .ToImmutableArray();
            if (mismatches.IsDefaultOrEmpty)
            {
                Diagnose(
                    GeneratedDiagnosticDescriptors.MissingMember,
                    target,
                    target.Name,
                    companion.Companion.Name);
                continue;
            }

            foreach (var mismatch in mismatches)
            {
                if (diagnosed.Add(mismatch))
                {
                    Diagnose(
                        GeneratedDiagnosticDescriptors.SignatureMismatch,
                        mismatch,
                        mismatch.Name);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byCandidate[candidate];
            if (matches.Length > 1 && diagnosed.Add(candidate))
            {
                Diagnose(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    candidate,
                    candidate.Name);
            }
            else if (targetSurfaceIsComplete &&
                     matches.IsDefaultOrEmpty &&
                     diagnosed.Add(candidate))
            {
                Diagnose(
                    GeneratedDiagnosticDescriptors.SignatureMismatch,
                    candidate,
                    candidate.Name);
            }
        }

        foreach (var target in targets)
        {
            var matches = byTarget[target];
            if (matches.Length != 1 || byCandidate[matches[0]].Length != 1)
            {
                continue;
            }

            ValidateBody(
                ContractClauseInventoryBuilder.NormalizeCallable(matches[0]),
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
        CancellationToken cancellationToken)
    {
        var inventory = clauses.Create(method);
        if (inventory.ImplementationBody == null)
        {
            diagnostics.Add(At(
                GeneratedDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, fallback),
                method.Name));
            return;
        }

        foreach (var clause in inventory.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
            {
                diagnostics.Add(At(
                    GeneratedDiagnosticDescriptors.InvalidClausePlacement,
                    clause.Location,
                    clause.Kind,
                    method.Name,
                    clause.Placement));
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

    internal static Location GetSourceLocation(ISymbol symbol, Location fallback)
    {
        return symbol.Locations.Where(static location => location.IsInSource)
            .OrderBy(
                static location => location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static location => location.SourceSpan.Start)
            .FirstOrDefault() ?? fallback;
    }
}
