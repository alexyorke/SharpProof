using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IEnumerable<SyntaxNode> GetPropertyAccessNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (node is MemberAccessExpressionSyntax memberAccess)
            {
                if (IsWriteOnlyTarget(memberAccess)) continue;

                if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol)
                    yield return memberAccess;
            }
            else if (node is IdentifierNameSyntax identifierName)
            {
                if (identifierName.Parent is MemberAccessExpressionSyntax ||
                    IsWriteOnlyTarget(identifierName))
                    continue;

                if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is IPropertySymbol)
                    yield return identifierName;
            }
            else if (node is ElementAccessExpressionSyntax elementAccess)
            {
                if (IsWriteOnlyTarget(elementAccess)) continue;

                if (semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol is IPropertySymbol)
                    yield return elementAccess;
            }
    }

    private static IEnumerable<SyntaxNode> GetPropertyWriteNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (TryGetPropertySetterMethod(node, semanticModel, cancellationToken, out _))
                yield return node;
    }

    private static bool IsWriteOnlyTarget(SyntaxNode node)
    {
        return node.Parent is AssignmentExpressionSyntax assignment &&
               assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
               ReferenceEquals(assignment.Left, node);
    }

    private static bool TryResolveExactConcreteType(
        IOperation? operation,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol exactReceiverType)
    {
        exactReceiverType = null!;
        var current = PurityAnalysisEngine.SkipImplicitConversions(operation);
        if (current == null) return false;

        if (PurityConcreteReceiverResolver.TryResolveKnownSystemTypeRuntimeReceiver(
                current,
                semanticModel.Compilation,
                out exactReceiverType))
            return true;

        if (current.Syntax is not ExpressionSyntax expression ||
            !SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                expression,
                useNode,
                semanticModel,
                cancellationToken,
                out var exactType) ||
            exactType is not INamedTypeSymbol namedType)
            return false;

        exactReceiverType = namedType;
        return true;
    }

    private static bool TryGetPropertySetterMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol? setterMethod)
    {
        setterMethod = null;
        if (!IsWriteOnlyTarget(node)) return false;

        if (semanticModel.GetSymbolInfo(node, cancellationToken).Symbol is not IPropertySymbol propertySymbol ||
            propertySymbol.SetMethod == null)
            return false;

        setterMethod = propertySymbol.SetMethod;
        return true;
    }
}
