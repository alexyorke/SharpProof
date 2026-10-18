namespace SharpProof.Analyzer;

// Clause placement is a compiler-symbol policy, independent of analyzer callback
// enrollment. One instance owns anonymous Requires reporting for one session.
internal sealed class ContractClausePlacementDiagnostics(
    Compilation compilation,
    CancellationToken cancellationToken)
{
    private readonly Compilation _compilation = (compilation ?? throw new System.ArgumentNullException(nameof(compilation)));
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _reportedAnonymousRequires =
            new(SymbolEqualityComparer.Default);

    internal void Report(ContractClauseInventory inventory, Action<Diagnostic> reportDiagnostic)
    {
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        if (inventory.Callable.MethodKind == MethodKind.AnonymousFunction)
        {
            ReportAnonymousRequires(inventory, reportDiagnostic);
        }
        foreach (var owner in GetNestedOwners(inventory, _compilation))
        {
            var nested = ContractClauseInventoryBuilder.ForCompilation(_compilation)
                .Create(owner, implementationBody: null, cancellationToken: _cancellationToken);
            ReportInvalidClauses(nested.Clauses, reportDiagnostic);
        }
    }

    internal void ReportAnonymousRequires(
        ContractClauseInventory inventory,
        Action<Diagnostic> reportDiagnostic)
    {
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        var method = inventory.Callable;
        if (method.MethodKind != MethodKind.AnonymousFunction ||
            !_reportedAnonymousRequires.TryAdd(
                ContractClauseInventoryBuilder.NormalizeCallable(method), 0))
        {
            return;
        }

        foreach (var clause in inventory.Clauses)
        {
            if (clause.Kind != BoundContractKind.Requires ||
                clause.Placement != ContractClausePlacement.ValidPrologue)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract.Requires",
                "<placement>",
                "preconditions inside lambda and anonymous methods cannot be enforced at delegate invocation sites",
                clause.Location));
        }
    }

    private static IEnumerable<IMethodSymbol> GetNestedOwners(
        ContractClauseInventory inventory,
        Compilation compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        return inventory.Clauses
            .Where(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement == ContractClausePlacement.NestedCallable))
            .Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, clause.Invocation.Syntax.SyntaxTree)
                .GetEnclosingSymbol(clause.Invocation.Syntax.SpanStart)))
            .OfType<IMethodSymbol>()
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    private static void ReportInvalidClauses(
        ImmutableArray<ContractClauseOccurrence> clauses,
        Action<Diagnostic> reportDiagnostic)
    {

        RepositoryParameterGuard.ThrowIfInvalid(clauses.IsDefault, nameof(clauses));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        foreach (var clause in clauses)
        {
            if (clause.IsValid ||
                clause.Placement == ContractClausePlacement.NestedCallable)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract." + clause.Kind,
                "<placement>",
                AnalyzerDiagnosticCatalog.DescribePlacement(clause.Placement),
                clause.Location));
        }
    }

}
