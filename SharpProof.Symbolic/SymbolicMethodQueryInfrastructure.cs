namespace SharpProof.Symbolic;

internal static class SymbolicMethodLikeQueryDispatcher {
    internal static TResult Execute<TResult>(
        SymbolicQueryContext request,
        string unsupportedTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<ResolvedMethodLikeTarget, Compilation, CancellationToken, TResult> executeAnalysis,
        CancellationToken cancellationToken) {
        var semanticModel = request.Source.Compilation.GetSemanticModel(request.Source.SyntaxTree);
        var resolvedTarget = SymbolicMethodLikeTargetResolver.Resolve(
            request.Source.SyntaxTree,
            semanticModel,
            request.Target,
            unsupportedTargetMessage,
            isMethodLikeDeclaration,
            cancellationToken);
        return executeAnalysis(resolvedTarget, request.Source.Compilation, cancellationToken);
    }
}
internal static class SymbolicMethodLikeTargetResolver {
    internal static ResolvedMethodLikeTarget Resolve(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SharpProofTarget target,
        string unsupportedTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        CancellationToken cancellationToken) {
        var root = syntaxTree.GetRoot(cancellationToken);
        switch (target.Kind) {
            case SharpProofTargetKind.Point:
                var position = SymbolicSourceLocation.GetPosition(syntaxTree, target.Line!.Value, target.Column ?? 1, cancellationToken);
                return ResolvePosition(root, syntaxTree, semanticModel, position, isMethodLikeDeclaration, cancellationToken);
            case SharpProofTargetKind.Position:
                return ResolvePosition(root, syntaxTree, semanticModel, target.Position!.Value, isMethodLikeDeclaration, cancellationToken);
            case SharpProofTargetKind.Line:
                return ResolveLine(root, syntaxTree, semanticModel, target.Line!.Value, isMethodLikeDeclaration, cancellationToken);
            default:
                throw new NotSupportedException(unsupportedTargetMessage);
        }
    }
    private static ResolvedMethodLikeTarget ResolvePosition(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int position,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        CancellationToken cancellationToken) {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var node = root.FindToken(position).Parent;
        if (node == null)
            throw new ArgumentException("Could not resolve a method-like body at the requested position.", nameof(position));

        return ResolveContaining(node, semanticModel, isMethodLikeDeclaration, cancellationToken);
    }
    private static ResolvedMethodLikeTarget ResolveLine(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int line,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        CancellationToken cancellationToken) {
        var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
        var declaration = root
            .DescendantNodes(static candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .Where(isMethodLikeDeclaration)
            .Where(candidate => candidate.Span.OverlapsWith(lineSpan))
            .OrderBy(candidate => candidate.Span.Length)
            .ThenBy(candidate => candidate.SpanStart)
            .FirstOrDefault();
        if (declaration != null) return ResolvedMethodLikeTarget.Create(declaration, semanticModel, cancellationToken);

        var node = root.FindToken(lineSpan.Start).Parent;
        if (node == null)
            throw new ArgumentException("Could not resolve a method-like body on the requested line.", nameof(line));

        return ResolveContaining(node, semanticModel, isMethodLikeDeclaration, cancellationToken);
    }
    private static ResolvedMethodLikeTarget ResolveContaining(
        SyntaxNode node,
        SemanticModel semanticModel,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        CancellationToken cancellationToken) {
        foreach (var ancestor in node.AncestorsAndSelf()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (isMethodLikeDeclaration(ancestor))
                return ResolvedMethodLikeTarget.Create(ancestor, semanticModel, cancellationToken);
        }
        throw new ArgumentException("Could not resolve a containing method-like body for the requested target.", nameof(node));
    }
}
internal sealed record ResolvedMethodLikeTarget(
    SemanticModel SemanticModel,
    SyntaxNode Declaration,
    SyntaxNode? BodyNode,
    IMethodSymbol? MethodSymbol) {
    internal static ResolvedMethodLikeTarget Create(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        new(
            semanticModel,
            declaration,
            SymbolicMethodSourceResolver.GetBodyNode(declaration),
            SymbolicMethodLikeDeclaration.GetMethodSymbol(declaration, semanticModel, cancellationToken));
}
internal static class SymbolicMethodSourceResolver {
    internal static bool IsBackedBySource(IMethodSymbol method) =>
        method.OriginalDefinition.DeclaringSyntaxReferences.Length != 0;

    internal static bool TryResolve(
        Compilation compilation,
        IMethodSymbol method,
        Func<SyntaxNode, bool> acceptsDeclaration,
        bool allowBodylessFallback,
        CancellationToken cancellationToken,
        out SyntaxNode declaration,
        out SyntaxNode? body,
        out SemanticModel semanticModel) {
        SyntaxNode? fallbackDeclaration = null;
        SemanticModel? fallbackSemanticModel = null;
        foreach (var syntaxReference in method.OriginalDefinition.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = syntaxReference.GetSyntax(cancellationToken);
            if (!acceptsDeclaration(candidate)) continue;

            var candidateBody = GetBodyNode(candidate);
            var candidateSemanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            if (candidateBody != null) {
                declaration = candidate;
                body = candidateBody;
                semanticModel = candidateSemanticModel;
                return true;
            }
            if (allowBodylessFallback) {
                fallbackDeclaration ??= candidate;
                fallbackSemanticModel ??= candidateSemanticModel;
            }
        }
        if (fallbackDeclaration != null && fallbackSemanticModel != null) {
            declaration = fallbackDeclaration;
            body = null;
            semanticModel = fallbackSemanticModel;
            return true;
        }
        declaration = null!;
        body = null;
        semanticModel = null!;
        return false;
    }
    internal static SyntaxNode? GetBodyNode(SyntaxNode declaration) => declaration switch {
        BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
        PropertyDeclarationSyntax property => GetGetterBody(property.ExpressionBody, property.AccessorList),
        IndexerDeclarationSyntax indexer => GetGetterBody(indexer.ExpressionBody, indexer.AccessorList),
        LocalFunctionStatementSyntax localFunction =>
            (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
        AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.Body,
        _ => null
    };
    private static SyntaxNode? GetGetterBody(ArrowExpressionClauseSyntax? expressionBody, AccessorListSyntax? accessorList) {
        var getter = accessorList?.Accessors.FirstOrDefault(static accessor => accessor.Keyword.ValueText == "get");
        return expressionBody?.Expression ?? (SyntaxNode?)getter?.Body ?? getter?.ExpressionBody?.Expression;
    }
}
