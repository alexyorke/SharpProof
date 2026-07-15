using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class MethodBodyOperationResolver
{
    internal static IOperation? GetMethodBodyRootOperation(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeConversionOperators = true)
    {
        var useDeclarationFallback = methodNode is DestructorDeclarationSyntax ||
                                     methodNode is ConversionOperatorDeclarationSyntax && !includeConversionOperators;
        var operationNode = useDeclarationFallback
            ? methodNode
            : CSharpSyntaxFacts.GetBlockBody(methodNode) ??
              (CSharpSyntaxFacts.TryGetExpressionBody(methodNode, out var expressionBody)
                  ? expressionBody
                  : methodNode);

        return semanticModel.GetOperation(operationNode, cancellationToken);
    }
}
