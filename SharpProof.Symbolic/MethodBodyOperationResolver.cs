using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic
{
    internal static class MethodBodyOperationResolver
    {
        internal static IOperation? GetMethodBodyRootOperation(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeConversionOperators = true)
        {
            return methodNode switch
            {
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null =>
                    semanticModel.GetOperation(methodDeclaration.Body, cancellationToken),
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.Body != null =>
                    semanticModel.GetOperation(constructorDeclaration.Body, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(constructorDeclaration.ExpressionBody.Expression, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.Body != null =>
                    semanticModel.GetOperation(operatorDeclaration.Body, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(operatorDeclaration.ExpressionBody.Expression, cancellationToken),
                ConversionOperatorDeclarationSyntax conversionOperatorDeclaration when includeConversionOperators && conversionOperatorDeclaration.Body != null =>
                    semanticModel.GetOperation(conversionOperatorDeclaration.Body, cancellationToken),
                ConversionOperatorDeclarationSyntax conversionOperatorDeclaration when includeConversionOperators && conversionOperatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(conversionOperatorDeclaration.ExpressionBody.Expression, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.Body != null =>
                    semanticModel.GetOperation(accessorDeclaration.Body, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(accessorDeclaration.ExpressionBody.Expression, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.Body != null =>
                    semanticModel.GetOperation(localFunction.Body, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null =>
                    semanticModel.GetOperation(localFunction.ExpressionBody.Expression, cancellationToken),
                _ => semanticModel.GetOperation(methodNode, cancellationToken),
            };
        }
    }
}
