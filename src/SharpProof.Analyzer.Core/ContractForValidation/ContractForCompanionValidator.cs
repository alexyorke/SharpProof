namespace SharpProof.ContractForValidation;

/// <summary>
/// Validates the one-to-one member mapping and clause bodies for a resolved
/// contract companion.
/// </summary>
internal static class ContractForCompanionValidator
{
    // Sink adapters retain guard order while callers own diagnostic collection.
    internal static bool ValidateRelationship(
        ResolvedCompanion companion,
        ContractForSymbolMatcher.CompanionRelationshipInventory relationships,
        Action<Diagnostic> diagnostics)
    {
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
        relationships = RepositoryParameterGuard.NotNull(relationships, nameof(relationships));
        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));
        return ReportRelationship(companion, relationships, diagnostics);
    }

    internal static void Validate(
        ResolvedCompanion companion,
        Compilation compilation,
        ContractClauseInventoryBuilder clauses,
        Action<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));
        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));
        ReportValidation(companion, compilation, clauses, diagnostics, cancellationToken);
    }

    internal static bool ReportRelationship(
        ResolvedCompanion companion,
        ContractForSymbolMatcher.CompanionRelationshipInventory relationships,
        Action<Diagnostic> diagnostics)
    {
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));

        relationships = RepositoryParameterGuard.NotNull(relationships, nameof(relationships));

        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));

        var issue = relationships.GetIssue(companion.Companion);
        switch (issue)
        {
            case ContractForSymbolMatcher.CompanionRelationshipIssue.None:
                return true;
            case ContractForSymbolMatcher.CompanionRelationshipIssue.SelfTarget:
                diagnostics(Diagnostic.Create(
                    ContractForDiagnosticDescriptors.SelfTarget,
                    companion.AttributeLocation,
                    companion.Companion.Name));
                return false;
            default:
                diagnostics(Diagnostic.Create(
                    ContractForDiagnosticDescriptors.CyclicRelationship,
                    companion.AttributeLocation,
                    companion.Companion.Name,
                    companion.Target.Name));
                return false;
        }
    }

    internal static void ReportValidation(
        ResolvedCompanion companion,
        Compilation compilation,
        ContractClauseInventoryBuilder clauses,
        Action<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));

        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));

        clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));

        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));

        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Companion,
                (companion.Target, companion.IsOpenTarget)))
        {
            diagnostics(Diagnostic.Create(
                ContractForDiagnosticDescriptors.InvalidCompanionType,
                companion.AttributeLocation,
                companion.Companion.Name,
                companion.Target.Name));
            return;
        }

        var targets = GetLogicalMethods(companion.Target);
        var candidates = GetLogicalMethods(companion.Companion);
        var intrinsics = new ContractIntrinsicValidator(compilation);
        var comparer = (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default;
        var targetMatches = ImmutableDictionaryCapture.Create(targets,
            static target => target is null ? throw new System.ArgumentNullException(nameof(target)) : (target),
            static _ => ImmutableArray.CreateBuilder<IMethodSymbol>(),
            comparer);
        var candidateMatches = ImmutableDictionaryCapture.Create(candidates,
            static candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate),
            static _ => ImmutableArray.CreateBuilder<IMethodSymbol>(),
            comparer);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = targetMatches[target];
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate))
                {
                    matches.Add(candidate);
                    candidateMatches[candidate].Add(target);
                }
            }

        }

        var byTarget = ImmutableDictionaryCapture.Create(targetMatches, static pair => pair.Key, static pair => pair.Value.ToImmutable(), comparer);
        var byCandidate = ImmutableDictionaryCapture.Create(candidateMatches, static pair => pair.Key, static pair => pair.Value.ToImmutable(), comparer);
        var targetSurfaceIsComplete = targets.All(target =>
            target is null ? throw new System.ArgumentNullException(nameof(target)) : (byTarget[target] is { Length: 1 } matches &&
            byCandidate[matches[0]].Length == 1));
        var diagnosed = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);

        void Diagnose(
            DiagnosticDescriptor descriptor,
            ISymbol symbol,
            ImmutableArray<object?> arguments)
        {
            if (arguments.IsDefault)
            { throw new ArgumentNullException(nameof(arguments)); }
            descriptor = RepositoryParameterGuard.NotNull(descriptor, nameof(descriptor));
            symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
            diagnostics(Diagnostic.Create(
                descriptor,
                GetSourceLocation(
                    symbol,
                    compilation,
                    companion.AttributeLocation),
                arguments.ToArray()));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byTarget[target];
            if (matches.Length > 1)
            {
                Diagnose(ContractForDiagnosticDescriptors.AmbiguousMember, target, [target.Name]);
                diagnosed.UnionWith(matches);
                continue;
            }

            if (matches.Length == 1)
            {
                continue;
            }

            var mismatches = candidates.Where(candidate =>
                    candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (string.Equals(
                        candidate.Name,
                        target.Name,
                        StringComparison.Ordinal) &&
                    byCandidate[candidate].IsDefaultOrEmpty))
                .ToImmutableArray();
            if (mismatches.IsDefaultOrEmpty)
            {
                Diagnose(ContractForDiagnosticDescriptors.MissingMember, target, [target.Name, companion.Companion.Name]);
                continue;
            }

            foreach (var mismatch in mismatches)
            {
                if (diagnosed.Add(mismatch))
                {
                    Diagnose(ContractForDiagnosticDescriptors.SignatureMismatch, mismatch, [mismatch.Name]);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byCandidate[candidate];
            if (matches.Length > 1 && diagnosed.Add(candidate))
            {
                Diagnose(ContractForDiagnosticDescriptors.AmbiguousMember, candidate, [candidate.Name]);
            }
            else if (targetSurfaceIsComplete &&
                     matches.IsDefaultOrEmpty &&
                     diagnosed.Add(candidate))
            {
                Diagnose(ContractForDiagnosticDescriptors.SignatureMismatch, candidate, [candidate.Name]);
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
                intrinsics,
                diagnostics,
                compilation,
                companion.AttributeLocation,
                cancellationToken);
        }
    }

    private static ImmutableArray<IMethodSymbol> GetLogicalMethods(
        INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return [.. ContractForSymbolMatcher.GetOrdinaryMethods(type)
            .Select(ContractClauseInventoryBuilder.NormalizeCallable)
            .Distinct((IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default)];
    }

    private static void ValidateBody(
        IMethodSymbol method,
        ContractClauseInventoryBuilder clauses,
        ContractIntrinsicValidator intrinsics,
        Action<Diagnostic> diagnostics,
        Compilation compilation,
        Location fallback,
        CancellationToken cancellationToken)
    {
        clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        diagnostics = RepositoryParameterGuard.NotNull(diagnostics, nameof(diagnostics));
        fallback = RepositoryParameterGuard.NotNull(fallback, nameof(fallback));
        intrinsics = RepositoryParameterGuard.NotNull(intrinsics, nameof(intrinsics));
        var inventory = clauses.Create(
            method,
            implementationBody: null,
            cancellationToken: cancellationToken);
        if (inventory.HasRejectedContractApiUsage)
        {
            SharpProof.Analyzer.SharpProofControlAttributePolicy.ReportRejectedContractApi(
                method.Name,
                GetSourceLocation(method, compilation, fallback),
                diagnostics);
        }

        if (inventory.ImplementationBody == null)
        {
            diagnostics(Diagnostic.Create(
                ContractForDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, compilation, fallback),
                method.Name));
            return;
        }

        foreach (var violation in intrinsics.Validate(
                     method,
                     inventory.ImplementationBody,
                     includeNestedCallables: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics(
                SharpProof.Analyzer.InvalidContractArgumentDiagnostics.Create(
                    violation));
        }

        foreach (var clause in inventory.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
            {
                diagnostics(Diagnostic.Create(
                    ContractForDiagnosticDescriptors.InvalidClausePlacement,
                    clause.Location,
                    clause.Kind,
                    method.Name,
                    clause.Placement));
            }
        }
    }

    internal static Location GetSourceLocation(
        ISymbol symbol,
        Compilation compilation,
        Location fallback)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));

        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));

        fallback = RepositoryParameterGuard.NotNull(fallback, nameof(fallback));

        return symbol.Locations.Where(location =>
                location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource &&
                location.SourceTree is { } tree &&
                compilation.ContainsSyntaxTree(tree)))
            .OrderBy(
                static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.SourceTree?.FilePath),
                StringComparer.Ordinal)
            .ThenBy(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.SourceSpan.Start))
            .FirstOrDefault() ?? fallback;
    }
}
