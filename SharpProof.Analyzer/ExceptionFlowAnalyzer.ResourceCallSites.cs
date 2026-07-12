using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine.Rules;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IEnumerable<MethodCallCandidate> GetUsingDisposeNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var usingStatement in GetRelevantDescendants<UsingStatementSyntax>(methodNode))
            foreach (var resource in GetUsingStatementResources(usingStatement, semanticModel, cancellationToken))
                foreach (var disposeMethod in GetDisposableMethods(
                             resource.Type,
                             semanticModel.Compilation,
                             usingStatement.AwaitKeyword.RawKind != 0))
                    yield return new MethodCallCandidate(
                        usingStatement,
                        disposeMethod,
                        CreateUsingDisposeGuard(resource.Expression, resource.Type));

        foreach (var usingDeclaration in GetRelevantDescendants<LocalDeclarationStatementSyntax>(methodNode)
                     .Where(statement => !statement.UsingKeyword.IsKind(SyntaxKind.None)))
            foreach (var variable in usingDeclaration.Declaration.Variables)
            {
                var resourceType = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                if (resourceType == null) continue;

                foreach (var disposeMethod in GetDisposableMethods(
                             resourceType,
                             semanticModel.Compilation,
                             usingDeclaration.AwaitKeyword.RawKind != 0))
                    yield return new MethodCallCandidate(
                        usingDeclaration,
                        disposeMethod,
                        CreateUsingDisposeGuard(
                            variable.Initializer?.Value,
                            resourceType));
            }
    }

    private static IEnumerable<UsingResource> GetUsingStatementResources(
        UsingStatementSyntax usingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (usingStatement.Expression != null)
        {
            var expressionType = semanticModel.GetTypeInfo(usingStatement.Expression, cancellationToken).ConvertedType;
            if (expressionType != null) yield return new UsingResource(usingStatement.Expression, expressionType);

            yield break;
        }

        if (usingStatement.Declaration == null) yield break;

        foreach (var variable in usingStatement.Declaration.Variables)
        {
            var type = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
            if (type != null) yield return new UsingResource(variable.Initializer?.Value, type);
        }
    }

    private static ITypeSymbol? GetUsingDeclarationVariableType(
        VariableDeclaratorSyntax variable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol localSymbol)
            return localSymbol.Type;

        return variable.Initializer == null
            ? null
            : semanticModel.GetTypeInfo(variable.Initializer.Value, cancellationToken).ConvertedType;
    }

    private static UsingDisposeGuard? CreateUsingDisposeGuard(
        ExpressionSyntax? resourceExpression,
        ITypeSymbol resourceType)
    {
        return resourceExpression != null && IsReferenceType(resourceType)
            ? new UsingDisposeGuard(resourceExpression)
            : null;
    }

    private static IEnumerable<MethodCallCandidate> GetForEachRuntimeMethodNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var forEachStatement in GetRelevantDescendants<CommonForEachStatementSyntax>(methodNode))
        {
            var statementInfo = semanticModel.GetForEachStatementInfo(forEachStatement);
            if (statementInfo.GetEnumeratorMethod is { } getEnumeratorMethod)
                yield return new MethodCallCandidate(forEachStatement.Expression, getEnumeratorMethod);
            if (statementInfo.MoveNextMethod is { } moveNextMethod)
                yield return new MethodCallCandidate(forEachStatement, moveNextMethod);
            if (statementInfo.CurrentProperty?.GetMethod is { } currentGetter)
                yield return new MethodCallCandidate(forEachStatement, currentGetter);
            if (statementInfo.DisposeMethod is { } disposeMethod)
                yield return new MethodCallCandidate(forEachStatement, disposeMethod);
        }
    }

    private static IEnumerable<IMethodSymbol> GetDisposableMethods(
        ITypeSymbol typeSymbol,
        Compilation compilation,
        bool async)
    {
        var method = DisposalMemberClassifier.FindDisposalMethod(typeSymbol, compilation, async);
        if (method != null) yield return method;
    }

    private readonly struct UsingResource
    {
        public UsingResource(ExpressionSyntax? expression, ITypeSymbol type)
        {
            Expression = expression;
            Type = type;
        }

        public ExpressionSyntax? Expression { get; }

        public ITypeSymbol Type { get; }
    }
}
