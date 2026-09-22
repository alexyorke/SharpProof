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
            .Select(candidate => GetRequires(candidate, session))
            .All(candidate => candidate != null && HaveSameRequires(
                local,
                candidate,
                session.IrFactory));
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
        var binding = session.BindRequires(method);
        return binding.IsSuccess &&
            binding.Contracts is { Clauses.Length: > 0 } contracts
                ? contracts
                : null;
    }

    private static bool HaveSameRequires(
        BoundMethodContracts left,
        BoundMethodContracts right,
        IrFactory factory)
    {
        var identities = right.Variables.ToDictionary(
            variable => (variable.Role, variable.Ordinal, factory.GetVariableInfo(variable.Variable).Type),
            static variable => variable.Variable);
        return NormalizeRequires(left, factory, identities).SequenceEqual(
            NormalizeRequires(right, factory, identities));
    }

    private static ImmutableArray<IrTerm> NormalizeRequires(
        BoundMethodContracts contracts,
        IrFactory factory,
        Dictionary<(BoundContractVariableRole, int, IrTypeId), IrVarId> identities)
    {
        var variables = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables)
        {
            var type = factory.GetVariableInfo(variable.Variable).Type;
            var key = (variable.Role, variable.Ordinal, type);
            if (!identities.TryGetValue(key, out var identity))
            {
                continue;
            }
            variables.Add(variable.Variable, factory.Variable(identity));
        }
        return [.. contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Requires)
            .Select(clause => IrSubstitution.Substitute(factory, clause.Condition, variables))
            .OrderBy(static value => value.Id.Value)];
    }

    private static bool HasDispatchContract(IMethodSymbol method)
    {
        return method.OverriddenMethod != null ||
            GetInheritedMethods(method).Any();
    }

    private static IEnumerable<IMethodSymbol> GetInheritedMethods(
        IMethodSymbol method)
    {
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
