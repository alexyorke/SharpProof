using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardKnownGuardFactory
{
    internal static bool TryCreateArgumentOutOfRangeGuardCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicOperationLowerer.TryLowerKnownArgumentGuardHazard(
                invocation,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
        return true;
    }
}
