namespace SharpProof.Symbolic;
internal sealed class ResolvedQueryTarget {
    private readonly CancellationToken _cancellationToken;
    private readonly Func<SyntaxNode, bool> _isMethodLikeDeclaration;
    private readonly Lazy<ResolvedMethodLikeTarget> _methodLike;
    private readonly Lazy<IReadOnlyList<SyntaxNode>> _programPointNodes;
    private readonly Lazy<SyntaxNode> _positionContainer;
    private ResolvedQueryTarget(
        SymbolicSourceInput source,
        SharpProofTarget target,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        CancellationToken cancellationToken) {
        Source = source;
        Target = target;
        _cancellationToken = cancellationToken;
        _isMethodLikeDeclaration = isMethodLikeDeclaration;
        Text = source.SyntaxTree.GetText(cancellationToken);
        Root = source.SyntaxTree.GetRoot(cancellationToken);
        SemanticModel = source.Compilation.GetSemanticModel(source.SyntaxTree);
        Position = target.Kind switch {
            SharpProofTargetKind.Point => Text.Lines[target.Line!.Value - 1].Start + (target.Column ?? 1) - 1,
            SharpProofTargetKind.Position => target.Position,
            _ => null
        };
        Span = target.Kind switch {
            SharpProofTargetKind.Line => Text.Lines[target.Line!.Value - 1].Span,
            SharpProofTargetKind.Span => TextSpan.FromBounds(target.SpanStart!.Value, target.SpanEnd!.Value),
            _ => null
        };
        _methodLike = new(ResolveMethodLike);
        _programPointNodes = new(SelectProgramPointNodes);
        _positionContainer = new(SelectPositionContainer);
    }
    internal SymbolicSourceInput Source { get; }
    internal SharpProofTarget Target { get; }
    internal SourceText Text { get; }
    internal SyntaxNode Root { get; }
    internal SemanticModel SemanticModel { get; }
    internal int? Position { get; }
    internal TextSpan? Span { get; }
    internal IReadOnlyList<SyntaxNode> ProgramPointNodes => _programPointNodes.Value;
    internal SyntaxNode PositionContainer => _positionContainer.Value;
    internal bool HasProgramPointOnTargetLine => SymbolicSourceTargetSelector.FindOnLine(
        Source.SyntaxTree, Target.Line!.Value, _cancellationToken).Count != 0;
    internal static ResolvedQueryTarget Create(
        SymbolicSourceInput source,
        SharpProofTarget target,
        CancellationToken cancellationToken,
        string parameterName = "target",
        Func<SyntaxNode, bool>? isMethodLikeDeclaration = null) {
        if (!Enum.IsDefined(typeof(SharpProofTargetKind), target.Kind))
            throw new ArgumentException("The target kind is not defined.", parameterName);
        var text = source.SyntaxTree.GetText(cancellationToken);
        switch (target.Kind) {
            case SharpProofTargetKind.Point:
            case SharpProofTargetKind.Line:
                RequirePositive(target.Line, target.Kind == SharpProofTargetKind.Point
                    ? "Point targets require a positive line."
                    : "Line targets require a positive line.");
                ValidateLine(target.Line!.Value);
                if (target.Kind == SharpProofTargetKind.Line) break;
                if (target.Column is { } column && column <= 0)
                    throw new ArgumentOutOfRangeException(parameterName, "Point target columns must be positive.");
                if ((target.Column ?? 1) > text.Lines[target.Line.Value - 1].Span.Length + 1)
                    throw new ArgumentOutOfRangeException(parameterName,
                        "Point target columns must be within the selected line.");
                break;
            case SharpProofTargetKind.Position:
                if (target.Position is not { } position || position < 0 || position > text.Length)
                    throw new ArgumentOutOfRangeException(parameterName,
                        "Position targets require a position within the source text span.");
                break;
            case SharpProofTargetKind.Span:
                if (target.SpanStart is not { } start || start < 0 ||
                    target.SpanEnd is not { } end || end <= start || end > text.Length)
                    throw new ArgumentOutOfRangeException(parameterName,
                        "Span targets require non-negative bounds with span-end greater than span-start.");
                break;
            case SharpProofTargetKind.AllLines:
                if (text.Length == 0)
                    throw new ArgumentOutOfRangeException(parameterName,
                        "All-lines targets require nonempty source text.");
                break;
        }
        return new(source, target, isMethodLikeDeclaration ??
            (static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true)), cancellationToken);
        void RequirePositive(int? value, string message) {
            if (value is not { } actual || actual <= 0)
                throw new ArgumentOutOfRangeException(parameterName, message);
        }
        void ValidateLine(int line) {
            if (line > text.Lines.Count)
                throw new ArgumentOutOfRangeException(parameterName, "Target lines must be within the source text.");
        }
    }
    internal ResolvedMethodLikeTarget ResolveMethodLike(string unsupportedTargetMessage) {
        if (Target.Kind is not (SharpProofTargetKind.Point or SharpProofTargetKind.Position or SharpProofTargetKind.Line))
            throw new NotSupportedException(unsupportedTargetMessage);
        return _methodLike.Value;
    }
    internal TResult Execute<TResult>(
        string unsupportedTargetMessage,
        Func<ResolvedMethodLikeTarget, Compilation, CancellationToken, TResult> analysis) =>
        analysis(ResolveMethodLike(unsupportedTargetMessage), Source.Compilation, _cancellationToken);
    private ResolvedMethodLikeTarget ResolveMethodLike() {
        SyntaxNode? declaration = null;
        if (Target.Kind == SharpProofTargetKind.Line)
            declaration = Root.DescendantNodes(static candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                .Where(_isMethodLikeDeclaration)
                .Where(candidate => candidate.Span.OverlapsWith(Span!.Value))
                .OrderBy(static candidate => candidate.Span.Length)
                .ThenBy(static candidate => candidate.SpanStart)
                .FirstOrDefault();
        if (declaration != null) return ResolvedMethodLikeTarget.Create(declaration, SemanticModel, _cancellationToken);
        var node = Root.FindToken(Target.Kind == SharpProofTargetKind.Line ? Span!.Value.Start : Position!.Value).Parent;
        if (node == null)
            throw new ArgumentException(Target.Kind == SharpProofTargetKind.Line
                ? "Could not resolve a method-like body on the requested line."
                : "Could not resolve a method-like body at the requested position.",
                Target.Kind == SharpProofTargetKind.Line ? "line" : "position");
        _cancellationToken.ThrowIfCancellationRequested();
        declaration = node.AncestorsAndSelf().FirstOrDefault(_isMethodLikeDeclaration) ??
                      throw new ArgumentException(
                          "Could not resolve a containing method-like body for the requested target.", nameof(node));
        return ResolvedMethodLikeTarget.Create(declaration, SemanticModel, _cancellationToken);
    }
    private IReadOnlyList<SyntaxNode> SelectProgramPointNodes() {
        if (Target.Kind == SharpProofTargetKind.AllLines) {
            var nodes = new List<SyntaxNode>();
            for (var line = 1; line <= Text.Lines.Count; line++)
                nodes.AddRange(SymbolicSourceTargetSelector.FindOnLine(Source.SyntaxTree, line, _cancellationToken));
            return nodes;
        }
        return Target.Kind switch {
            SharpProofTargetKind.Point or SharpProofTargetKind.Position =>
                [SymbolicSourceTargetSelector.FindNarrowestAtPosition(Root, Position!.Value)],
            SharpProofTargetKind.Line or SharpProofTargetKind.Span =>
                SymbolicSourceTargetSelector.FindInSpan(Source.SyntaxTree, Span!.Value, _cancellationToken),
            _ => throw new NotSupportedException("Target kind is not supported for syntax tree queries.")
        };
    }
    private SyntaxNode SelectPositionContainer() {
        var narrow = SymbolicSourceTargetSelector.FindNarrowestAtPosition(Root, Position!.Value);
        return narrow.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault() ??
               narrow.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault()?.Expression ??
               narrow;
    }
}
internal static class SymbolicMethodLikeQueryDispatcher {
    internal static TResult Execute<TResult>(
        SymbolicSourceInput source,
        SharpProofTarget target,
        string unsupportedTargetMessage,
        Func<SyntaxNode, bool> isMethodLikeDeclaration,
        Func<ResolvedMethodLikeTarget, Compilation, CancellationToken, TResult> executeAnalysis,
        CancellationToken cancellationToken) {
        var resolved = ResolvedQueryTarget.Create(
            source, target, cancellationToken, isMethodLikeDeclaration: isMethodLikeDeclaration);
        return resolved.Execute(unsupportedTargetMessage, executeAnalysis);
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
        declaration = null!;
        body = null;
        semanticModel = null!;
        foreach (var syntaxReference in method.OriginalDefinition.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = syntaxReference.GetSyntax(cancellationToken);
            if (!acceptsDeclaration(candidate)) continue;
            var candidateBody = GetBodyNode(candidate);
            if (candidateBody == null && (!allowBodylessFallback || declaration != null)) continue;
            declaration = candidate;
            body = candidateBody;
            semanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            if (body != null) return true;
        }
        return declaration != null;
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
