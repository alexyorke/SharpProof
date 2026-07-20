namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer {
    private static IEnumerable<MethodCallCandidate> GetUsingDisposeNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var resource in GetUsingResources(methodNode, semanticModel, cancellationToken))
            foreach (var disposeMethod in GetDisposableMethods(
                         resource.Type,
                         semanticModel.Compilation,
                         resource.IsAsync))
                yield return new MethodCallCandidate(
                    resource.Site,
                    disposeMethod,
                    CreateUsingDisposeGuard(resource.Expression, resource.Type));
    }

    private static IEnumerable<UsingResource> GetUsingResources(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var usingStatement in GetRelevantDescendants<UsingStatementSyntax>(methodNode)) {
            var isAsync = usingStatement.AwaitKeyword.RawKind != 0;
            if (usingStatement.Expression != null) {
                var expressionType =
                    semanticModel.GetTypeInfo(usingStatement.Expression, cancellationToken).ConvertedType;
                if (expressionType != null)
                    yield return new UsingResource(
                        usingStatement,
                        usingStatement.Expression,
                        expressionType,
                        isAsync);

                continue;
            }

            if (usingStatement.Declaration == null) continue;
            foreach (var variable in usingStatement.Declaration.Variables) {
                var type = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                if (type != null)
                    yield return new UsingResource(
                        usingStatement,
                        variable.Initializer?.Value,
                        type,
                        isAsync);
            }
        }

        foreach (var usingDeclaration in GetRelevantDescendants<LocalDeclarationStatementSyntax>(methodNode)
                     .Where(statement => !statement.UsingKeyword.IsKind(SyntaxKind.None)))
            foreach (var variable in usingDeclaration.Declaration.Variables) {
                var type = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                if (type != null)
                    yield return new UsingResource(
                        usingDeclaration,
                        variable.Initializer?.Value,
                        type,
                        usingDeclaration.AwaitKeyword.RawKind != 0);
            }
    }

    private static ITypeSymbol? GetUsingDeclarationVariableType(
        VariableDeclaratorSyntax variable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol localSymbol)
            return localSymbol.Type;

        return variable.Initializer == null
            ? null
            : semanticModel.GetTypeInfo(variable.Initializer.Value, cancellationToken).ConvertedType;
    }

    private static UsingDisposeGuard? CreateUsingDisposeGuard(
        ExpressionSyntax? resourceExpression,
        ITypeSymbol resourceType) {
        return resourceExpression != null && ExceptionSiteClassifier.IsReferenceType(resourceType)
            ? new UsingDisposeGuard(resourceExpression)
            : null;
    }

    private static IEnumerable<MethodCallCandidate> GetForEachRuntimeMethodNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel) {
        foreach (var forEachStatement in GetRelevantDescendants<CommonForEachStatementSyntax>(methodNode)) {
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
        bool async) {
        var method = DisposalMemberClassifier.FindDisposalMethod(typeSymbol, compilation, async);
        if (method != null) yield return method;
    }

    private readonly record struct UsingResource(
        SyntaxNode Site,
        ExpressionSyntax? Expression,
        ITypeSymbol Type,
        bool IsAsync);
}
