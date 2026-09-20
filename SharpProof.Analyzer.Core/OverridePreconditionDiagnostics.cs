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
            .Where(static binding => binding != null)
            .Cast<BoundMethodContracts>()
            .Any(candidate => HaveSameRequires(
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
        var printer = new IrPrinter(factory);
        return NormalizeRequires(left, printer).SequenceEqual(
            NormalizeRequires(right, printer),
            StringComparer.Ordinal);
    }

    private static ImmutableArray<string> NormalizeRequires(
        BoundMethodContracts contracts,
        IrPrinter printer)
    {
        var variables = contracts.Variables.ToDictionary(
            static variable => variable.Variable.Value,
            static variable => variable.Role + ":" + variable.Ordinal);
        return [.. contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Requires)
            .Select(clause => System.Text.RegularExpressions.Regex.Replace(
                printer.Print(clause.Condition),
                @"\bv\d+\b",
                match => variables.TryGetValue(
                    int.Parse(match.Value.Substring(1),
                        CultureInfo.InvariantCulture),
                    out var role)
                        ? "var:" + role
                        : match.Value))
            .OrderBy(static value => value, StringComparer.Ordinal)];
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
