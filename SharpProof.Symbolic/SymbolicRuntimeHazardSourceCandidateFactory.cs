using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardSourceCandidateFactory
{
    internal static IEnumerable<RuntimeHazardCandidate> CreateThrowCandidates(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var exceptionType = SymbolicRuntimeExceptionFacts.GetThrownExceptionType(
            throwNode,
            semanticModel,
            cancellationToken,
            false);
        var isRethrow = throwNode is ThrowStatementSyntax { Expression: null };
        var exceptionTypeName =
            exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty) ??
            (isRethrow ? ExceptionTypes.Unknown : ExceptionTypes.Exception);
        foreach (var hazard in SymbolicOperationLowerer.LowerThrowHazards(
                     throwNode,
                     isRethrow,
                     exceptionTypeName,
                     new SymbolicLoweringContext(semanticModel, cancellationToken)))
            yield return new RuntimeHazardCandidate(throwNode, hazard);
    }
}
