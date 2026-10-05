using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Roslyn;

internal static class RoslynCfgFactory
{
    internal static ControlFlowGraph? TryCreateMethodOrConstructorGraph(
        IOperation operation,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            IMethodBodyOperation method =>
                ControlFlowGraph.Create(method, cancellationToken),
            IConstructorBodyOperation constructor =>
                ControlFlowGraph.Create(constructor, cancellationToken),
            _ => null
        };
    }

    internal static bool OverridesMethod(
        IMethodSymbol? candidate,
        IMethodSymbol target)
    {
        for (var current = candidate;
             current != null;
             current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    target.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }
}
