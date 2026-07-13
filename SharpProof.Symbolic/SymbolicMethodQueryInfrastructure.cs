using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class SymbolicSourceInputDispatcher
{
    internal static TResult Execute<TResult>(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions? options,
        string generatedFileName,
        string assemblyName,
        string unsupportedSourceMessage,
        Func<SyntaxTree, Compilation, SymbolicQueryTarget, CancellationToken, TResult> querySyntaxTree,
        Func<SyntaxNode, SemanticModel, SymbolicQueryTarget, CancellationToken, TResult> queryNode,
        CancellationToken cancellationToken)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        options ??= SymbolicQueryOptions.Default;
        switch (source.Kind)
        {
            case SymbolicSourceInputKind.File:
                var filePath = source.FilePath!;
                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("File path is required.", nameof(filePath));
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Source file does not exist.", filePath);

                return QuerySource(
                    File.ReadAllText(filePath),
                    Path.GetFullPath(filePath),
                    target,
                    options,
                    source.CompilationProfile,
                    generatedFileName,
                    assemblyName,
                    querySyntaxTree,
                    cancellationToken);
            case SymbolicSourceInputKind.Text:
                return QuerySource(
                    source.SourceText!,
                    source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                    target,
                    options,
                    source.CompilationProfile,
                    generatedFileName,
                    assemblyName,
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
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        SymbolicSourceCompilationProfile? compilationProfile,
        string generatedFileName,
        string assemblyName,
        Func<SyntaxTree, Compilation, SymbolicQueryTarget, CancellationToken, TResult> querySyntaxTree,
        CancellationToken cancellationToken)
    {
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            generatedFileName,
            assemblyName,
            options.References,
            cancellationToken,
            compilationProfile);
        return querySyntaxTree(syntaxTree, compilation, target, cancellationToken);
    }
}

internal static class SymbolicMethodLikeTargetResolver
{
    internal static TTarget Resolve<TTarget>(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        string unsupportedTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<SyntaxNode, SemanticModel, CancellationToken, TTarget> createTarget,
        CancellationToken cancellationToken)
    {
        var root = syntaxTree.GetRoot(cancellationToken);
        switch (target.Kind)
        {
            case SymbolicQueryTargetKind.Point:
                var position = SymbolicSourceLocation.GetPosition(
                    syntaxTree,
                    target.LineNumber!.Value,
                    target.ColumnNumber ?? 1,
                    cancellationToken);
                return ResolvePosition(
                    root,
                    syntaxTree,
                    semanticModel,
                    position,
                    isMethodLikeDeclaration,
                    createTarget,
                    cancellationToken);
            case SymbolicQueryTargetKind.Position:
                return ResolvePosition(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.PositionOffset!.Value,
                    isMethodLikeDeclaration,
                    createTarget,
                    cancellationToken);
            case SymbolicQueryTargetKind.Line:
                return ResolveLine(
                    root,
                    syntaxTree,
                    semanticModel,
                    target.LineNumber!.Value,
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
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction =>
                (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
            AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.Body,
            _ => null
        };
    }
}
