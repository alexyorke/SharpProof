namespace SharpProof.Symbolic;
internal static class MethodBodyOperationResolver {
    internal static IOperation? GetMethodBodyRootOperation(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeConversionOperators = true) {
        var useDeclarationFallback = methodNode is DestructorDeclarationSyntax ||
                                     methodNode is ConversionOperatorDeclarationSyntax && !includeConversionOperators;
        var operationNode = useDeclarationFallback
            ? methodNode
            : CSharpSyntaxFacts.GetBlockBody(methodNode) ??
              (CSharpSyntaxFacts.TryGetExpressionBody(methodNode, out var expressionBody)
                  ? expressionBody
                  : methodNode);
        var operation = semanticModel.GetOperation(operationNode, cancellationToken);
        if (operation != null) return operation;
        return semanticModel.GetOperation(methodNode, cancellationToken) switch {
            ILocalFunctionOperation { Body: { } body } => body,
            IAnonymousFunctionOperation { Body: { } body } => body,
            IMethodBodyOperation methodBody => methodBody,
            _ => null
        };
    }
}
