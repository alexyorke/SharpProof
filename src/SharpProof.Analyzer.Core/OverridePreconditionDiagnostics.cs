using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Analyzer;

internal static class OverridePreconditionDiagnostics
{
    internal static void Validate(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        ArgumentNullGuard.NotNull(session, nameof(session));
        ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.Configuration.ContractsEnabled ||
            !HasDispatchContract(method))
        {
            return;
        }

        var local = GetRequires(method, session);
        if (local == null)
        {
            return;
        }

        var inherited = GetInheritedMethods(method)
            .Select(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (GetRequires(candidate, session)))
            .All(candidate => candidate != null && HaveSameRequires(
                local,
                candidate));
        if (inherited)
        {
            return;
        }

        reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
            "Contract.Requires",
            method.Name,
            "a precondition declared only on an override or interface " +
            "implementation is not visible through base or interface dispatch",
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                method,
                cancellationToken)));
    }

    private static BoundMethodContracts? GetRequires(
        IMethodSymbol method,
        AnalyzerSession session)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        var binding = session.BindRequires(method);
        return binding.IsSuccess &&
            binding.Contracts is { Clauses.Length: > 0 } contracts
                ? contracts
                : null;
    }

    private static bool HaveSameRequires(
        BoundMethodContracts left,
        BoundMethodContracts right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return RequiresKeys(left).SequenceEqual(RequiresKeys(right), StringComparer.Ordinal);
    }

    private static ImmutableArray<string> RequiresKeys(BoundMethodContracts contracts)
    {
        contracts = RepositoryParameterGuard.NotNull(contracts, nameof(contracts));
        return [.. contracts.Clauses
            .Where(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires))
            .Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.ComparisonKey))
            .OrderBy(static key => key is null ? throw new System.ArgumentNullException(nameof(key)) : (key), StringComparer.Ordinal)];
    }

    private static bool HasDispatchContract(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return method.OverriddenMethod != null ||
            GetInheritedMethods(method).Any();
    }

    private static IEnumerable<IMethodSymbol> GetInheritedMethods(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        if (method.OverriddenMethod is { } overridden)
        {
            yield return overridden;
        }

        if (method.ContainingType is not { } containingType)
        {
            yield break;
        }

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            foreach (var candidate in interfaceType.GetMembers()
                         .OfType<IMethodSymbol>())
            {
                var implementation = containingType
                    .FindImplementationForInterfaceMember(candidate);
                if (implementation is IMethodSymbol implementationMethod &&
                    SymbolEqualityComparer.Default.Equals(
                        implementationMethod,
                        method))
                {
                    yield return candidate;
                }
            }
        }
    }
}
