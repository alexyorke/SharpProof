namespace SharpProof.Contracts;

public sealed class ContractClauseInventoryBuilder(Compilation compilation)
{
    private static readonly ConditionalWeakTable<Compilation, ContractClauseInventoryBuilder> Cache = new();
    private readonly Compilation _compilation =
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
    private readonly ContractApiIdentityResolver _identity =
        ContractApiIdentityResolver.ForCompilation(compilation);
    private readonly ContractClauseSymbols? _api = ContractClauseSymbols.TryCreate(compilation);
    private readonly ImmutableDictionary<SyntaxTree, int> _treeOrdinals = compilation.SyntaxTrees
        .Select(static (tree, ordinal) => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((tree, ordinal)))
        .ToImmutableDictionary(static item => item.tree, static item => item.ordinal);
    private ImmutableDictionary<IMethodSymbol, ContractClauseInventory> _cache =
        ImmutableDictionary.Create<IMethodSymbol, ContractClauseInventory>(SymbolEqualityComparer.IncludeNullability);

    internal static ContractClauseInventoryBuilder ForCompilation(Compilation compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        return Cache.GetValue(compilation, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new(value)));
    }

    internal int GetTreeOrdinal(SyntaxTree tree)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        return _treeOrdinals.TryGetValue(tree, out var ordinal) ? ordinal : int.MaxValue;
    }

    public ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody = null)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        return Create(callable, implementationBody, alreadyNormalized: false,
            cancellationToken: cancellation.Token);
    }

    public ContractClauseInventory Create(IMethodSymbol callable, CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        return Create(callable, null, alreadyNormalized: false, cancellationToken: cancellationToken);
    }

    public ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        return Create(callable, implementationBody, alreadyNormalized: false,
            cancellationToken: cancellationToken);
    }

    internal ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody,
        bool alreadyNormalized,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        callable = ArgumentNullGuard.NotNull(callable, nameof(callable));

        if (!alreadyNormalized)
        {
            callable = NormalizeCallable(callable);
        }
        if ((callable.DeclaringSyntaxReferences.Any(reference =>
                 reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (_treeOrdinals.ContainsKey(reference.SyntaxTree))) &&
             !SymbolEqualityComparer.Default.Equals(callable.ContainingAssembly, _compilation.Assembly)) ||
            (implementationBody != null &&
             (!ReferenceEquals(implementationBody.SemanticModel?.Compilation, _compilation) ||
              !IsCallableBodyRoot(
                callable,
                implementationBody,
                cancellationToken))))
        {
            return new ContractClauseInventory(
                callable,
                _api != null,
                hasRejectedContractApiUsage: true,
                implementationBody: null,
                clauses: []);
        }
        return implementationBody == null
            ? ImmutableInterlocked.GetOrAdd(ref _cache,
                callable,
                value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (CreateUncached(value, cancellationToken)))
            : CreateCore(callable, implementationBody, cancellationToken);
    }

    private ContractClauseInventory CreateUncached(
        IMethodSymbol callable,
        CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        return CreateCore(callable, null, cancellationToken);
    }

    private bool IsCallableBodyRoot(
        IMethodSymbol callable,
        IOperation implementationBody,
        CancellationToken cancellationToken)
    {
        implementationBody = RepositoryParameterGuard.NotNull(implementationBody, nameof(implementationBody));
        var candidate = GetBody(implementationBody.Syntax) ??
            implementationBody.Syntax;
        if (GetDeclaredBodies(callable, cancellationToken).Any(body =>
                body is null ? throw new System.ArgumentNullException(nameof(body)) : (HasSameSite(body, candidate))))
        {
            return true;
        }

        return candidate is CompilationUnitSyntax &&
            _compilation.GetEntryPoint(cancellationToken) is { } entryPoint &&
            HaveSameDefinition(callable, entryPoint) &&
            entryPoint.DeclaringSyntaxReferences.Any(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.SyntaxTree == candidate.SyntaxTree &&
                reference.Span == candidate.Span));
    }

    private ContractClauseInventory CreateCore(
        IMethodSymbol callable,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        var found = ImmutableList<(
            ContractClauseClassification Classification,
            ContractClausePlacement Placement,
            IInvocationOperation Invocation,
            int TreeOrdinal)>.Empty;
        var directClauseCache = ImmutableDictionary<(
            SyntaxTree Tree,
            int Start,
            int Length), bool>.Empty;
        IOperation? resolvedBody = null;
        var hasRejectedContractApiUsage = false;
        foreach (var body in GetBodies(
                     callable,
                     implementationBody,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetSemanticModel(body.SyntaxTree, out var model))
            {
                hasRejectedContractApiUsage = true;
                continue;
            }

            resolvedBody ??= implementationBody;
            var root = model.GetOperation(body, cancellationToken);
            if (root == null)
            {
                continue;
            }

            resolvedBody ??= root;
            foreach (var invocation in root.DescendantsAndSelf().OfType<IInvocationOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetMethod = invocation.TargetMethod;
                if (_api?.GetClauseClassification(targetMethod) is not { } classification)
                {
                    hasRejectedContractApiUsage |=
                        _identity.IsRejectedClauseMethod(targetMethod) &&
                        IsOwnedByCallable(
                            callable,
                            invocation,
                            model,
                            cancellationToken);
                    continue;
                }

                var ownedByCallable = IsOwnedByCallable(
                    callable,
                    invocation,
                    model,
                    cancellationToken);
                found = found.Add((classification, Classify(
                    invocation,
                    model,
                    body,
                    ownedByCallable,
                    ref directClauseCache,
                    cancellationToken), invocation,
                    GetTreeOrdinal(invocation.Syntax.SyntaxTree)));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var ordinals = ImmutableList.Create(0, 0, 0);
        var sourceOrdinal = 0;
        var clauses = found
            .OrderBy(static clause => clause.TreeOrdinal)
            .ThenBy(static clause => clause.Invocation.Syntax.SpanStart)
            .ThenBy(static clause => clause.Invocation.Syntax.Span.Length)
            .Select(clause =>
            {
                var ordinal = ordinals[clause.Classification.OrdinalIndex];
                ordinals = ordinals.SetItem(clause.Classification.OrdinalIndex, ordinal + 1);
                return new ContractClauseOccurrence(
                    clause.Classification.Kind, clause.Placement, ordinal,
                    sourceOrdinal++, clause.Invocation);
            })
            .ToImmutableArray();
        return new ContractClauseInventory(
            callable,
            _api != null,
            hasRejectedContractApiUsage,
            resolvedBody,
            clauses);
    }

    private bool TryGetSemanticModel(
        SyntaxTree tree,
        out SemanticModel model)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        try
        {
            model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, tree);
            return true;
        }
        catch (ArgumentException exception) when (exception.ParamName == "tree")
        {
            model = null!;
            return false;
        }
    }

    private ContractClausePlacement Classify(
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body,
        bool ownedByCallable,
        ref ImmutableDictionary<(SyntaxTree Tree, int Start, int Length), bool>
            directClauseCache,
        CancellationToken cancellationToken)
    {
        if (!ownedByCallable)
        {
            return ContractClausePlacement.NestedCallable;
        }

        if (!IsReachable(invocation.Syntax, model, cancellationToken))
        {
            return ContractClausePlacement.Unreachable;
        }

        if (TryGetDirectPlacement(
                invocation,
                model,
                body,
                ref directClauseCache,
                out var placement,
                cancellationToken))
        {
            return placement;
        }

        return invocation.Syntax.Ancestors()
            .TakeWhile(ancestor => ancestor is null ? throw new System.ArgumentNullException(nameof(ancestor)) : (!HasSameSite(ancestor, body)))
            .Any(IsConditional)
            ? ContractClausePlacement.Conditional
            : ContractClausePlacement.Misplaced;
    }

    private static bool IsOwnedByCallable(
        IMethodSymbol callable,
        IInvocationOperation invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        return model.GetEnclosingSymbol(
                invocation.Syntax.SpanStart,
                cancellationToken) is
                IMethodSymbol method &&
            HaveSameDefinition(callable, method);
    }

    private bool TryGetDirectPlacement(
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body,
        ref ImmutableDictionary<(SyntaxTree Tree, int Start, int Length), bool>
            directClauseCache,
        out ContractClausePlacement placement,
        CancellationToken cancellationToken)
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

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDirectClause(
                    model,
                    prior,
                    ref directClauseCache,
                    cancellationToken))
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
            global.Parent is { } globalParent &&
            HasSameSite(globalParent, unit))
        {
            statements = unit.Members.OfType<GlobalStatementSyntax>()
                .Select(static member => member is null ? throw new System.ArgumentNullException(nameof(member)) : (member.Statement));
            return true;
        }
        statements = [];
        return false;
    }

    private bool IsDirectClause(
        SemanticModel model,
        StatementSyntax statement,
        ref ImmutableDictionary<(SyntaxTree Tree, int Start, int Length), bool>
            directClauseCache,
        CancellationToken cancellationToken)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        cancellationToken.ThrowIfCancellationRequested();
        if (statement is not ExpressionStatementSyntax expression)
        {
            return false;
        }

        var key = (
            expression.SyntaxTree,
            expression.Span.Start,
            expression.Span.Length);
        if (directClauseCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var isDirect = model.GetOperation(
                expression.Expression,
                cancellationToken) is IInvocationOperation invocation &&
            _api?.GetClauseKind(invocation.TargetMethod).HasValue == true;
        directClauseCache = directClauseCache.AddUnique(key, isDirect);
        return isDirect;
    }

    private static bool IsReachable(
        SyntaxNode syntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        cancellationToken.ThrowIfCancellationRequested();
        var statement = syntax.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null)
        {
            return true;
        }

        var flow = model.AnalyzeControlFlow(statement);
        return flow == null ||
            !flow.Succeeded ||
            flow.StartPointIsReachable;
    }

    private static bool IsConditional(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        return syntax is
        IfStatementSyntax or ConditionalExpressionSyntax or
        SwitchStatementSyntax or SwitchExpressionSyntax or
        WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or
        CommonForEachStatementSyntax;
    }

    private static ImmutableArray<SyntaxNode> GetBodies(
        IMethodSymbol callable,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        if (implementationBody != null)
        {
            return [GetBody(implementationBody.Syntax) ?? implementationBody.Syntax];
        }

        var bodies = GetDeclaredBodies(callable, cancellationToken);
        if (!bodies.IsDefaultOrEmpty ||
            GetPartialImplementation(callable) is not { } implementation)
        {
            return bodies;
        }

        return GetDeclaredBodies(implementation, cancellationToken);
    }

    private static IMethodSymbol? GetPartialImplementation(
        IMethodSymbol callable)
    {
        if (callable.OriginalDefinition.PartialImplementationPart is
            { } methodImplementation)
        {
            return methodImplementation;
        }
        return null;
    }

    private static ImmutableArray<SyntaxNode> GetDeclaredBodies(
        IMethodSymbol callable,
        CancellationToken cancellationToken)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        var bodies = ImmutableList<SyntaxNode>.Empty;
        foreach (var reference in callable.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetBody(reference.GetSyntax(cancellationToken)) is { } body)
            {
                bodies = bodies.Add(body);
            }
        }

        return bodies.ToImmutableArray();
    }

    internal static SyntaxNode? GetBody(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        return syntax switch
        {
            BaseMethodDeclarationSyntax { Body: { } body } => body,
            BaseMethodDeclarationSyntax { ExpressionBody.Expression: { } expression } method =>
                GetExpressionBodyOperationRoot(method, expression),
            ArrowExpressionClauseSyntax
            {
                Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax,
                Expression: var expression
            } => GetExpressionBodyOperationRoot(null, expression),
            AccessorDeclarationSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression } => GetExpressionBodyOperationRoot(null, expression),
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            LocalFunctionStatementSyntax { Body: { } body } => body,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } local =>
                GetExpressionBodyOperationRoot(local, expression),
            ParenthesizedLambdaExpressionSyntax { Body: { } body } => body,
            SimpleLambdaExpressionSyntax { Body: { } body } => body,
            AnonymousMethodExpressionSyntax { Block: { } block } => block,
            BlockSyntax or ExpressionSyntax or CompilationUnitSyntax => syntax,
            _ => null
        };
    }

    private static SyntaxNode GetExpressionBodyOperationRoot(
        SyntaxNode? declaration,
        ExpressionSyntax expression)
    {
        // These syntax wrappers have no IOperation of their own. The inner
        // operation retains Roslyn's checked arithmetic flags and body owner.
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        while (expression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
        {
            expression = expression is ParenthesizedExpressionSyntax parenthesized
                ? parenthesized.Expression
                : ((CheckedExpressionSyntax)expression).Expression;
        }
        // Roslyn exposes no operation for an isolated `ref value` syntax.
        // The declaration owns the corresponding method-body operation.
        return expression is RefExpressionSyntax ? declaration ?? expression : expression;
    }

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return SyntaxSite.IsSame(left, right);
    }

    internal static IMethodSymbol NormalizeCallable(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        if (method.AssociatedSymbol is IPropertySymbol property &&
            property.PartialImplementationPart != null)
        {
            return GetPropertyAccessor(method, property, useImplementation: true) ?? method;
        }
        return method.PartialImplementationPart ?? method;
    }

    internal static bool HaveSameDefinition(
        IMethodSymbol left,
        IMethodSymbol right)
    {
        left = ArgumentNullGuard.NotNull(left, nameof(left));
        right = ArgumentNullGuard.NotNull(right, nameof(right));
        return SymbolEqualityComparer.Default.Equals(
            GetPartialDefinition(left),
            GetPartialDefinition(right));
    }

    private static IMethodSymbol GetPartialDefinition(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var definition = method.OriginalDefinition;
        if (definition.AssociatedSymbol is IPropertySymbol property &&
            property.PartialDefinitionPart != null)
        {
            return GetPropertyAccessor(
                definition,
                property,
                useImplementation: false) ?? definition;
        }
        return definition.PartialDefinitionPart ?? definition;
    }

    private static IMethodSymbol? GetPropertyAccessor(
        IMethodSymbol method,
        IPropertySymbol property,
        bool useImplementation)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        var part = useImplementation
            ? property.PartialImplementationPart
            : property.PartialDefinitionPart;
        return method.MethodKind == MethodKind.PropertyGet
            ? part?.GetMethod
            : part?.SetMethod;
    }
}
