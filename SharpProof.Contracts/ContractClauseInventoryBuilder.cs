namespace SharpProof.Contracts;

public sealed class ContractClauseInventoryBuilder(Compilation compilation) {
    private readonly Compilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly ContractClauseSymbols? _api =
        ContractClauseSymbols.TryCreate(compilation);
    private readonly Dictionary<SyntaxTree, int> _treeOrdinals =
        compilation.SyntaxTrees
            .Select(static (tree, ordinal) => (tree, ordinal))
            .ToDictionary(static item => item.tree, static item => item.ordinal);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractClauseInventory>
        _cache = new(SymbolEqualityComparer.Default);

    public ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody = null) {
        if (callable == null)
            throw new ArgumentNullException(nameof(callable));
        return implementationBody == null
            ? _cache.GetOrAdd(callable, CreateUncached)
            : CreateCore(callable, implementationBody);
    }

    private ContractClauseInventory CreateUncached(IMethodSymbol callable) =>
        CreateCore(callable, null);

    private ContractClauseInventory CreateCore(
        IMethodSymbol callable,
        IOperation? implementationBody) {
        var bodies = GetBodies(callable, implementationBody);
        IOperation? resolvedBody = implementationBody;
        var seen = new HashSet<(SyntaxTree Tree, int Start, int Length)>();
        var found = new List<(
            BoundContractKind Kind,
            ContractClausePlacement Placement,
            IInvocationOperation Invocation,
            int TreeOrdinal)>();
        foreach (var body in bodies) {
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, body.SyntaxTree);
            var root = model.GetOperation(body);
            if (root == null) continue;
            resolvedBody ??= root;
            foreach (var invocation in root.DescendantsAndSelf()
                         .OfType<IInvocationOperation>()) {
                var kind = _api?.GetClauseKind(invocation.TargetMethod);
                if (!kind.HasValue ||
                    !seen.Add(Site(invocation.Syntax))) continue;
                var syntax = invocation.Syntax;
                found.Add((
                    kind.Value,
                    Classify(
                        callable,
                        invocation,
                        model,
                        body),
                    invocation,
                    _treeOrdinals.TryGetValue(
                        syntax.SyntaxTree,
                        out var ordinal)
                        ? ordinal
                        : int.MaxValue));
            }
        }

        var kindOrdinals = new int[3];
        var sourceOrdinal = 0;
        var clauses = found
            .OrderBy(static clause => clause.TreeOrdinal)
            .ThenBy(static clause => clause.Invocation.Syntax.SpanStart)
            .ThenBy(static clause => clause.Invocation.Syntax.Span.Length)
            .Select(clause => new ContractClauseOccurrence(
                clause.Kind,
                clause.Placement,
                kindOrdinals[(int)clause.Kind]++,
                sourceOrdinal++,
                clause.Invocation))
            .ToImmutableArray();
        return new ContractClauseInventory(
            callable,
            _api != null,
            resolvedBody,
            clauses);
    }

    private bool TryGetDirectClause(
        SemanticModel model,
        StatementSyntax statement,
        out IInvocationOperation invocation) {
        var candidate = statement is ExpressionStatementSyntax expression
            ? model.GetOperation(expression.Expression) as IInvocationOperation
            : null;
        if (candidate == null ||
            !_api!.GetClauseKind(candidate.TargetMethod).HasValue) {
            invocation = null!;
            return false;
        }
        invocation = candidate;
        return true;
    }

    private ContractClausePlacement Classify(
        IMethodSymbol callable,
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body) {
        var enclosing = model.GetEnclosingSymbol(invocation.Syntax.SpanStart);
        if (enclosing is not IMethodSymbol method ||
            !SymbolEqualityComparer.Default.Equals(
                callable.OriginalDefinition,
                method.OriginalDefinition))
            return ContractClausePlacement.NestedCallable;
        if (!IsReachable(invocation.Syntax, model))
            return ContractClausePlacement.Unreachable;
        if (TryGetDirectPlacement(
                invocation,
                model,
                body,
                out var placement))
            return placement;
        return invocation.Syntax.Ancestors()
            .TakeWhile(ancestor =>
                ancestor.SyntaxTree != body.SyntaxTree ||
                ancestor.Span != body.Span)
            .Any(IsConditional)
            ? ContractClausePlacement.Conditional
            : ContractClausePlacement.Misplaced;
    }

    private bool TryGetDirectPlacement(
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body,
        out ContractClausePlacement placement) {
        var syntax = invocation.Syntax;
        if (body is not BlockSyntax block) {
            placement = ContractClausePlacement.ValidPrologue;
            return HasSameSite(syntax, body);
        }
        if (syntax.Parent is not ExpressionStatementSyntax statement ||
            statement.Parent is not BlockSyntax parent ||
            !HasSameSite(parent, block)) {
            placement = default;
            return false;
        }
        foreach (var prior in block.Statements) {
            if (HasSameSite(prior, statement)) break;
            if (prior is not EmptyStatementSyntax &&
                !TryGetDirectClause(model, prior, out _)) {
                placement = ContractClausePlacement.Late;
                return true;
            }
        }
        placement = ContractClausePlacement.ValidPrologue;
        return true;
    }

    private static bool IsReachable(SyntaxNode syntax, SemanticModel model) {
        var statement = syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (statement == null) return true;
        try {
            var flow = model.AnalyzeControlFlow(statement);
            return !flow.Succeeded || flow.StartPointIsReachable;
        }
        catch (ArgumentException) {
            return true;
        }
    }

    private static bool IsConditional(SyntaxNode syntax) => syntax is
        IfStatementSyntax or
        ConditionalExpressionSyntax or
        SwitchStatementSyntax or
        SwitchExpressionSyntax or
        WhileStatementSyntax or
        DoStatementSyntax or
        ForStatementSyntax or
        CommonForEachStatementSyntax;

    private static ImmutableArray<SyntaxNode> GetBodies(
        IMethodSymbol callable,
        IOperation? implementationBody) {
        if (implementationBody != null)
            return [GetBody(implementationBody.Syntax) ??
                    implementationBody.Syntax];
        return [.. callable.DeclaringSyntaxReferences
            .Select(static reference => GetBody(reference.GetSyntax()))
            .Where(static body => body != null)
            .Select(static body => body!)];
    }

    private static SyntaxNode? GetBody(SyntaxNode syntax) => syntax switch {
        BaseMethodDeclarationSyntax { Body: { } body } => body,
        BaseMethodDeclarationSyntax {
            ExpressionBody.Expression: { } expression
        } => expression,
        AccessorDeclarationSyntax { Body: { } body } => body,
        AccessorDeclarationSyntax {
            ExpressionBody.Expression: { } expression
        } => expression,
        LocalFunctionStatementSyntax { Body: { } body } => body,
        LocalFunctionStatementSyntax {
            ExpressionBody.Expression: { } expression
        } => expression,
        ParenthesizedLambdaExpressionSyntax { Body: { } body } => body,
        SimpleLambdaExpressionSyntax { Body: { } body } => body,
        AnonymousMethodExpressionSyntax { Block: { } block } => block,
        BlockSyntax or ExpressionSyntax => syntax,
        _ => null
    };

    private static (SyntaxTree Tree, int Start, int Length) Site(
        SyntaxNode syntax) =>
        (syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length);

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right) =>
        left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
}
