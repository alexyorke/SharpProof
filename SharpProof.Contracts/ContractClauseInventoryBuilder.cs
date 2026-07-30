namespace SharpProof.Contracts;

public sealed class ContractClauseInventoryBuilder(Compilation compilation)
{
    private static readonly ConditionalWeakTable<Compilation, ContractClauseInventoryBuilder> Cache = new();
    private readonly Compilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly ContractApiIdentityResolver _identity =
        ContractApiIdentityResolver.ForCompilation(compilation);
    private readonly ContractClauseSymbols? _api = ContractClauseSymbols.TryCreate(compilation);
    private readonly Dictionary<SyntaxTree, int> _treeOrdinals = compilation.SyntaxTrees
        .Select(static (tree, ordinal) => (tree, ordinal))
        .ToDictionary(static item => item.tree, static item => item.ordinal);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractClauseInventory> _cache =
        new(SymbolEqualityComparer.Default);

    internal static ContractClauseInventoryBuilder ForCompilation(Compilation compilation)
    {
        return Cache.GetValue(compilation, static value => new(value));
    }

    internal int GetTreeOrdinal(SyntaxTree tree)
    {
        return _treeOrdinals.TryGetValue(tree, out var ordinal) ? ordinal : int.MaxValue;
    }

    public ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody = null)
    {
        if (callable == null)
        {
            throw new ArgumentNullException(nameof(callable));
        }

        callable = NormalizeCallable(callable);
        return implementationBody == null
            ? _cache.GetOrAdd(callable, CreateUncached)
            : CreateCore(callable, implementationBody);
    }

    private ContractClauseInventory CreateUncached(IMethodSymbol callable)
    {
        return CreateCore(callable, null);
    }

    private ContractClauseInventory CreateCore(
        IMethodSymbol callable,
        IOperation? implementationBody)
    {
        var found = new List<(
            BoundContractKind Kind,
            ContractClausePlacement Placement,
            IInvocationOperation Invocation,
            int TreeOrdinal)>();
        var resolvedBody = implementationBody;
        var hasRejectedContractApiUsage = false;
        foreach (var body in GetBodies(callable, implementationBody))
        {
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, body.SyntaxTree);
            var root = model.GetOperation(body);
            if (root == null)
            {
                continue;
            }

            resolvedBody ??= root;
            foreach (var invocation in root.DescendantsAndSelf().OfType<IInvocationOperation>())
            {
                if (_api?.GetClauseKind(invocation.TargetMethod) is not { } kind)
                {
                    hasRejectedContractApiUsage |=
                        _identity.IsRejectedClauseMethod(
                            invocation.TargetMethod);
                    continue;
                }

                found.Add((kind, Classify(callable, invocation, model, body), invocation,
                    GetTreeOrdinal(invocation.Syntax.SyntaxTree)));
            }
        }
        var requiresOrdinal = 0;
        var ensuresOrdinal = 0;
        var assumeOrdinal = 0;
        var sourceOrdinal = 0;
        var clauses = found
            .OrderBy(static clause => clause.TreeOrdinal)
            .ThenBy(static clause => clause.Invocation.Syntax.SpanStart)
            .ThenBy(static clause => clause.Invocation.Syntax.Span.Length)
            .Select(clause => new ContractClauseOccurrence(
                clause.Kind, clause.Placement, NextOrdinal(
                    clause.Kind,
                    ref requiresOrdinal,
                    ref ensuresOrdinal,
                    ref assumeOrdinal),
                sourceOrdinal++, clause.Invocation))
            .ToImmutableArray();
        return new ContractClauseInventory(
            callable,
            _api != null,
            hasRejectedContractApiUsage,
            resolvedBody,
            clauses);
    }

    private static int NextOrdinal(
        BoundContractKind kind,
        ref int requiresOrdinal,
        ref int ensuresOrdinal,
        ref int assumeOrdinal)
    {
        return kind switch
        {
            BoundContractKind.Requires => requiresOrdinal++,
            BoundContractKind.Ensures => ensuresOrdinal++,
            BoundContractKind.Assume => assumeOrdinal++,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown contract kind.")
        };
    }

    private ContractClausePlacement Classify(
        IMethodSymbol callable,
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body)
    {
        var enclosing = model.GetEnclosingSymbol(invocation.Syntax.SpanStart);
        if (enclosing is not IMethodSymbol method ||
            !SymbolEqualityComparer.Default.Equals(
                callable.OriginalDefinition, method.OriginalDefinition))
        {
            return ContractClausePlacement.NestedCallable;
        }

        if (!IsReachable(invocation.Syntax, model))
        {
            return ContractClausePlacement.Unreachable;
        }

        if (TryGetDirectPlacement(invocation, model, body, out var placement))
        {
            return placement;
        }

        return invocation.Syntax.Ancestors()
            .TakeWhile(ancestor => !HasSameSite(ancestor, body))
            .Any(IsConditional)
            ? ContractClausePlacement.Conditional
            : ContractClausePlacement.Misplaced;
    }

    private bool TryGetDirectPlacement(
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body,
        out ContractClausePlacement placement)
    {
        if (body is not BlockSyntax and not CompilationUnitSyntax)
        {
            placement = ContractClausePlacement.ValidPrologue;
            return HasSameSite(invocation.Syntax, body);
        }
        if (invocation.Syntax.Parent is not ExpressionStatementSyntax statement ||
            !TryGetStatements(body, statement, out var statements))
        {
            placement = default;
            return false;
        }
        foreach (var prior in statements)
        {
            if (HasSameSite(prior, statement))
            {
                break;
            }

            if (!IsDirectClause(model, prior))
            {
                placement = ContractClausePlacement.Late;
                return true;
            }
        }
        placement = ContractClausePlacement.ValidPrologue;
        return true;
    }

    private static bool TryGetStatements(
        SyntaxNode body,
        ExpressionStatementSyntax statement,
        out IEnumerable<StatementSyntax> statements)
    {
        if (body is BlockSyntax block &&
            statement.Parent is BlockSyntax parent &&
            HasSameSite(parent, block))
        {
            statements = block.Statements;
            return true;
        }
        if (body is CompilationUnitSyntax unit &&
            statement.Parent is GlobalStatementSyntax global &&
            HasSameSite(global.Parent!, unit))
        {
            statements = unit.Members.OfType<GlobalStatementSyntax>()
                .Select(static member => member.Statement);
            return true;
        }
        statements = [];
        return false;
    }

    private bool IsDirectClause(SemanticModel model, StatementSyntax statement)
    {
        return statement is ExpressionStatementSyntax expression &&
        model.GetOperation(expression.Expression) is IInvocationOperation invocation &&
        _api!.GetClauseKind(invocation.TargetMethod).HasValue;
    }

    private static bool IsReachable(SyntaxNode syntax, SemanticModel model)
    {
        var statement = syntax.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null)
        {
            return true;
        }

        try
        {
            var flow = model.AnalyzeControlFlow(statement);
            return flow == null ||
                !flow.Succeeded ||
                flow.StartPointIsReachable;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool IsConditional(SyntaxNode syntax)
    {
        return syntax is
        IfStatementSyntax or ConditionalExpressionSyntax or
        SwitchStatementSyntax or SwitchExpressionSyntax or
        WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or
        CommonForEachStatementSyntax;
    }

    private static ImmutableArray<SyntaxNode> GetBodies(
        IMethodSymbol callable,
        IOperation? implementationBody)
    {
        return implementationBody != null
            ? [GetBody(implementationBody.Syntax) ?? implementationBody.Syntax]
            : [.. callable.DeclaringSyntaxReferences
                .Select(static reference => GetBody(reference.GetSyntax()))
                .Where(static body => body != null)
                .Select(static body => body!)];
    }

    internal static SyntaxNode? GetBody(SyntaxNode syntax)
    {
        return syntax switch
        {
            BaseMethodDeclarationSyntax { Body: { } body } => body,
            BaseMethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            AccessorDeclarationSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            LocalFunctionStatementSyntax { Body: { } body } => body,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            ParenthesizedLambdaExpressionSyntax { Body: { } body } => body,
            SimpleLambdaExpressionSyntax { Body: { } body } => body,
            AnonymousMethodExpressionSyntax { Block: { } block } => block,
            BlockSyntax or ExpressionSyntax or CompilationUnitSyntax => syntax,
            _ => null
        };
    }

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right)
    {
        return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
    }

    internal static IMethodSymbol NormalizeCallable(IMethodSymbol method)
    {
        return method.PartialImplementationPart ?? method;
    }
}
