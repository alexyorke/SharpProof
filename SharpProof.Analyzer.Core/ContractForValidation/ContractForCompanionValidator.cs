using SharpProof.Analyzer;

namespace SharpProof.ContractForValidation;

/// <summary>
/// Validates the one-to-one member mapping and clause bodies for a resolved
/// contract companion.
/// </summary>
internal static class ContractForCompanionValidator
{
    internal static void Validate(
        ResolvedCompanion companion,
        Compilation compilation,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Companion,
                (companion.Target, companion.IsOpenTarget)))
        {
            diagnostics.Add(At(
                ContractForDiagnosticDescriptors.InvalidCompanionType,
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
                GetSourceLocation(
                    symbol,
                    compilation,
                    companion.AttributeLocation),
                arguments));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = byTarget[target];
            if (matches.Length > 1)
            {
                Diagnose(
                    ContractForDiagnosticDescriptors.AmbiguousMember,
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
                    ContractForDiagnosticDescriptors.MissingMember,
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
                        ContractForDiagnosticDescriptors.SignatureMismatch,
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
                    ContractForDiagnosticDescriptors.AmbiguousMember,
                    candidate,
                    candidate.Name);
            }
            else if (targetSurfaceIsComplete &&
                     matches.IsDefaultOrEmpty &&
                     diagnosed.Add(candidate))
            {
                Diagnose(
                    ContractForDiagnosticDescriptors.SignatureMismatch,
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
                compilation,
                companion.AttributeLocation,
                cancellationToken);
        }
    }

    private static void ValidateBody(
        IMethodSymbol method,
        ContractClauseInventoryBuilder clauses,
        List<Diagnostic> diagnostics,
        Compilation compilation,
        Location fallback,
        CancellationToken cancellationToken)
    {
        var inventory = clauses.Create(method);
        if (inventory.HasRejectedContractApiUsage)
        {
            SharpProofControlAttributePolicy.ReportRejectedContractApi(
                method.Name,
                GetSourceLocation(method, compilation, fallback),
                diagnostics.Add);
            return;
        }

        if (inventory.ImplementationBody == null)
        {
            diagnostics.Add(At(
                ContractForDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, compilation, fallback),
                method.Name));
            return;
        }

        foreach (var violation in new ContractIntrinsicValidator(compilation).Validate(
                     method,
                     inventory.ImplementationBody,
                     includeNestedCallables: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isOld = violation.Failure is
                ContractBindingFailure.OldOutsideEnsures or
                ContractBindingFailure.NestedOld;
            var (argument, reason) = SharpProof.Analyzer.AnalyzerDiagnosticCatalog
                .DescribeIntrinsicViolation(violation.Failure, isOld);
            diagnostics.Add(SharpProof.Analyzer.InvalidContractArgumentDiagnostics.Create(
                isOld ? "Contract.Old" : "Contract.Result",
                argument,
                reason,
                violation.Invocation.Syntax.GetLocation()));
        }

        foreach (var clause in inventory.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
            {
                diagnostics.Add(At(
                    ContractForDiagnosticDescriptors.InvalidClausePlacement,
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

    internal static Location GetSourceLocation(
        ISymbol symbol,
        Compilation compilation,
        Location fallback)
    {
        return symbol.Locations.Where(location =>
                location.IsInSource &&
                location.SourceTree is { } tree &&
                compilation.ContainsSyntaxTree(tree))
            .OrderBy(
                static location => location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static location => location.SourceSpan.Start)
            .FirstOrDefault() ?? fallback;
    }
}
