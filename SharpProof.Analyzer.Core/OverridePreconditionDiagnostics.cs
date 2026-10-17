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
        if (method is null)
        {
            throw new System.ArgumentNullException(nameof(method));
        }
        if (session is null)
        {
            throw new System.ArgumentNullException(nameof(session));
        }
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
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (left is null)
        {
            throw new System.ArgumentNullException(nameof(left));
        }
        if (right is null)
        {
            throw new System.ArgumentNullException(nameof(right));
        }
        var identities = right.Variables.ToDictionary(
            variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : ((variable.Role, variable.Ordinal, factory.GetVariableInfo(variable.Variable).Type)),
            static variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable));
        return NormalizeRequires(left, factory, identities).SequenceEqual(
            NormalizeRequires(right, factory, identities));
    }

    private static ImmutableArray<IrTerm> NormalizeRequires(
        BoundMethodContracts contracts,
        IrFactory factory,
        Dictionary<(BoundContractVariableRole, int, IrTypeId), IrVarId> identities)
    {
        if (contracts is null)
        {
            throw new System.ArgumentNullException(nameof(contracts));
        }
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (identities is null)
        {
            throw new System.ArgumentNullException(nameof(identities));
        }
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
            .Where(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires))
            .Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (IrSubstitution.Substitute(factory, clause.Condition, variables)))
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Id.Value))];
    }

    private static bool HasDispatchContract(IMethodSymbol method)
    {
        if (method is null)
        {
            throw new System.ArgumentNullException(nameof(method));
        }
        return method.OverriddenMethod != null ||
            GetInheritedMethods(method).Any();
    }

    private static IEnumerable<IMethodSymbol> GetInheritedMethods(
        IMethodSymbol method)
    {
        if (method is null)
        {
            throw new System.ArgumentNullException(nameof(method));
        }
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
