namespace SharpProof.Symbolic;

internal static class SymbolicSourceInputDispatcher
{
    internal static TResult Execute<TResult>(
        SymbolicSourceInput source,
        SharpProofTarget target,
        SymbolicQueryOptions? options,
        SymbolicSourceCompilationKind compilationKind,
        string unsupportedSourceMessage,
        Func<SyntaxTree, Compilation, SharpProofTarget, CancellationToken, TResult> querySyntaxTree,
        Func<SyntaxNode, SemanticModel, SharpProofTarget, CancellationToken, TResult> queryNode,
        CancellationToken cancellationToken)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        options ??= SymbolicQueryOptions.Default;
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                return SymbolicSourceFile.WithFile(source.FilePath!, (sourceText, sourcePath) => QuerySource(
                    sourceText,
                    sourcePath,
                    target,
                    options,
                    source.CompilationProfile,
                    compilationKind,
                    querySyntaxTree,
                    cancellationToken));
            case SymbolicSourceInputKind.Text:
                return QuerySource(
                    source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                    target,
                    options,
                    source.CompilationProfile,
                    compilationKind,
                    querySyntaxTree,
                    cancellationToken);
            case SymbolicSourceInputKind.SyntaxTree:
                return querySyntaxTree(
                    source.SyntaxTree!,
                    source.Compilation!,
                    target,
                    cancellationToken);
            case SymbolicSourceInputKind.Node:
                return queryNode(source.Node!, source.SemanticModel!, target, cancellationToken);
            default:
                throw new NotSupportedException(unsupportedSourceMessage);
        }
    }

    private static TResult QuerySource<TResult>(
        string sourceText,
        string filePath,
        SharpProofTarget target,
        SymbolicQueryOptions options,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicSourceCompilationKind compilationKind,
        Func<SyntaxTree, Compilation, SharpProofTarget, CancellationToken, TResult> querySyntaxTree,
        CancellationToken cancellationToken)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            compilationKind,
            options.References,
            cancellationToken,
            compilationProfile);
        return querySyntaxTree(syntaxTree, compilation, target, cancellationToken);
    }
}

internal static class SymbolicMethodLikeQueryDispatcher
{
    internal static TResult Execute<TResult, TTarget>(
        SymbolicQueryContext request,
        SymbolicSourceCompilationKind compilationKind,
        string unsupportedSourceMessage,
        string unsupportedTargetMessage,
        string nodeTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        Func<TTarget, Compilation, CancellationToken, TResult> executeAnalysis,
        CancellationToken cancellationToken)
    {
        return SymbolicSourceInputDispatcher.Execute(
            request.Source,
            request.Target,
            request.Options,
            compilationKind,
            unsupportedSourceMessage,
            QuerySyntaxTree,
            QueryNode,
            cancellationToken);

        TResult QuerySyntaxTree(
            SyntaxTree syntaxTree,
            Compilation compilation,
            SharpProofTarget queryTarget,
            CancellationToken queryCancellationToken)
        {
            if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
            if (compilation == null) throw new ArgumentNullException(nameof(compilation));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var resolvedTarget = SymbolicMethodLikeTargetResolver.Resolve(
                syntaxTree,
                semanticModel,
                queryTarget,
                unsupportedTargetMessage,
                isMethodLikeDeclaration,
                createTarget,
                queryCancellationToken);
            return executeAnalysis(resolvedTarget, compilation, queryCancellationToken);
        }

        TResult QueryNode(
            SyntaxNode node,
            SemanticModel semanticModel,
            SharpProofTarget queryTarget,
            CancellationToken queryCancellationToken)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
            if (queryTarget.Kind != SharpProofTargetKind.Node)
                throw new NotSupportedException(nodeTargetMessage);

            var resolvedTarget = SymbolicMethodLikeTargetResolver.ResolveNode(
                node,
                semanticModel,
                isMethodLikeDeclaration,
                createTarget,
                queryCancellationToken);
            return executeAnalysis(resolvedTarget, semanticModel.Compilation, queryCancellationToken);
        }
    }
}

internal static class SymbolicMethodLikeTargetResolver
{
    internal static TTarget Resolve<TTarget>(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SharpProofTarget target,
        string unsupportedTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        var root = syntaxTree.GetRoot(cancellationToken);
        switch (target.Kind)
        {
            case SharpProofTargetKind.Point:
                var position = SymbolicSourceLocation.GetPosition(
                    syntaxTree,
                    target.Line!.Value,
                    target.Column ?? 1,
                    cancellationToken);
                return ResolvePosition(
                    root,
                    syntaxTree,
                    semanticModel,
                    position,
                    isMethodLikeDeclaration,
                    createTarget,
                    cancellationToken);
            case SharpProofTargetKind.Position:
                return ResolvePosition(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.Position!.Value,
                    isMethodLikeDeclaration,
                    createTarget,
                    cancellationToken);
            case SharpProofTargetKind.Line:
                return ResolveLine(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.Line!.Value,
                    isMethodLikeDeclaration,
                    createTarget,
                    cancellationToken);
            default:
                throw new NotSupportedException(unsupportedTargetMessage);
        }
    }

    internal static TTarget ResolveNode<TTarget>(
        SyntaxNode node,
        SemanticModel semanticModel,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        return isMethodLikeDeclaration(node)
            ? createTarget(node, semanticModel, cancellationToken)
            : ResolveContaining(node, semanticModel, isMethodLikeDeclaration, createTarget, cancellationToken);
    }

    private static TTarget ResolvePosition<TTarget>(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int position,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var node = root.FindToken(position).Parent;
        if (node == null)
            throw new ArgumentException("Could not resolve a method-like body at the requested position.",
                nameof(position));

        return ResolveContaining(node, semanticModel, isMethodLikeDeclaration, createTarget, cancellationToken);
    }

    private static TTarget ResolveLine<TTarget>(
        SyntaxNode root,
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int line,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
        var declaration = root
            .DescendantNodes(static candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .Where(isMethodLikeDeclaration)
            .Where(candidate => candidate.Span.OverlapsWith(lineSpan))
            .OrderBy(candidate => candidate.Span.Length)
            .ThenBy(candidate => candidate.SpanStart)
            .FirstOrDefault();
        if (declaration != null) return createTarget(declaration, semanticModel, cancellationToken);

        var node = root.FindToken(lineSpan.Start).Parent;
        if (node == null)
            throw new ArgumentException("Could not resolve a method-like body on the requested line.", nameof(line));

        return ResolveContaining(node, semanticModel, isMethodLikeDeclaration, createTarget, cancellationToken);
    }

    private static TTarget ResolveContaining<TTarget>(
        SyntaxNode node,
        SemanticModel semanticModel,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isMethodLikeDeclaration(ancestor))
                return createTarget(ancestor, semanticModel, cancellationToken);
        }

        throw new ArgumentException("Could not resolve a containing method-like body for the requested target.",
            nameof(node));
    }
}

internal static class SymbolicMethodSourceResolver
{
    internal static bool IsBackedBySource(IMethodSymbol method)
    {
        return method.OriginalDefinition.DeclaringSyntaxReferences.Length != 0;
    }

    internal static bool TryResolve(
        Compilation compilation,
        IMethodSymbol method,
        Func<SyntaxNode, bool> acceptsDeclaration,
        bool allowBodylessFallback,
        CancellationToken cancellationToken,
        out SyntaxNode declaration,
        out SyntaxNode? body,
        out SemanticModel semanticModel)
    {
        SyntaxNode? fallbackDeclaration = null;
        SemanticModel? fallbackSemanticModel = null;
        foreach (var syntaxReference in method.OriginalDefinition.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = syntaxReference.GetSyntax(cancellationToken);
            if (!acceptsDeclaration(candidate)) continue;

            var candidateBody = GetBodyNode(candidate);
            var candidateSemanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            if (candidateBody != null)
            {
                declaration = candidate;
                body = candidateBody;
                semanticModel = candidateSemanticModel;
                return true;
            }

            if (allowBodylessFallback)
            {
                fallbackDeclaration ??= candidate;
                fallbackSemanticModel ??= candidateSemanticModel;
            }
        }

        if (fallbackDeclaration != null && fallbackSemanticModel != null)
        {
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

    internal static SyntaxNode? GetBodyNode(SyntaxNode declaration)
    {
        return declaration switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            PropertyDeclarationSyntax property => GetGetterBody(property.ExpressionBody, property.AccessorList),
            IndexerDeclarationSyntax indexer => GetGetterBody(indexer.ExpressionBody, indexer.AccessorList),
            LocalFunctionStatementSyntax localFunction =>
                (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
            AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.Body,
            _ => null
        };
    }

    private static SyntaxNode? GetGetterBody(
        ArrowExpressionClauseSyntax? expressionBody,
        AccessorListSyntax? accessorList)
    {
        var getter = accessorList?.Accessors.FirstOrDefault(static accessor => accessor.Keyword.ValueText == "get");
        return expressionBody?.Expression ?? (SyntaxNode?)getter?.Body ?? getter?.ExpressionBody?.Expression;
    }
}
