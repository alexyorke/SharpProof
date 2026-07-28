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
        callable = NormalizeCallable(callable);
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
                if (!kind.HasValue) continue;
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
        invocation = statement is ExpressionStatementSyntax expression &&
                     model.GetOperation(expression.Expression) is
                         IInvocationOperation candidate &&
                     _api!.GetClauseKind(candidate.TargetMethod).HasValue
            ? candidate
            : null!;
        return invocation != null;
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
        if (body is not BlockSyntax and not CompilationUnitSyntax) {
            placement = ContractClausePlacement.ValidPrologue;
            return HasSameSite(syntax, body);
        }
        if (syntax.Parent is not ExpressionStatementSyntax statement) {
            placement = default;
            return false;
        }
        IEnumerable<StatementSyntax> statements;
        if (body is BlockSyntax block &&
            statement.Parent is BlockSyntax parent &&
            HasSameSite(parent, block))
            statements = block.Statements;
        else if (body is CompilationUnitSyntax unit &&
                 statement.Parent is GlobalStatementSyntax global &&
                 HasSameSite(global.Parent!, unit))
            statements = unit.Members.OfType<GlobalStatementSyntax>()
                .Select(static member => member.Statement);
        else {
            placement = default;
            return false;
        }
        foreach (var prior in statements) {
            if (HasSameSite(prior, statement)) break;
            if (!TryGetDirectClause(model, prior, out _)) {
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

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right) =>
        left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;

    internal static IMethodSymbol NormalizeCallable(IMethodSymbol method) =>
        method.PartialImplementationPart ?? method;
}
