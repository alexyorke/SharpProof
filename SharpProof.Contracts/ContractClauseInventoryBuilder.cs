namespace SharpProof.Contracts;

public sealed class ContractClauseInventoryBuilder(Compilation compilation)
{
    private static readonly ConditionalWeakTable<Compilation, ContractClauseInventoryBuilder> Cache = new();
    private readonly Compilation _compilation =
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
    private readonly ContractApiIdentityResolver _identity =
        ContractApiIdentityResolver.ForCompilation(compilation);
    private readonly ContractClauseSymbols? _api = ContractClauseSymbols.TryCreate(compilation);
    private readonly Dictionary<SyntaxTree, int> _treeOrdinals = compilation.SyntaxTrees
        .Select(static (tree, ordinal) => (tree, ordinal))
        .ToDictionary(static item => item.tree, static item => item.ordinal);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractClauseInventory> _cache =
        new(SymbolEqualityComparer.IncludeNullability);

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
        return Create(callable, implementationBody, CancellationToken.None);
    }

    internal ContractClauseInventory Create(
        IMethodSymbol callable,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        callable = ArgumentNullGuard.NotNull(callable, nameof(callable));

        callable = NormalizeCallable(callable);
        if (implementationBody != null &&
            !IsCallableBodyRoot(
                callable,
                implementationBody,
                cancellationToken))
        {
            return new ContractClauseInventory(
                callable,
                _api != null,
                hasRejectedContractApiUsage: true,
                implementationBody: null,
                clauses: []);
        }
        return implementationBody == null
            ? _cache.GetOrAdd(
                callable,
                value => CreateUncached(value, cancellationToken))
            : CreateCore(callable, implementationBody, cancellationToken);
    }

    private ContractClauseInventory CreateUncached(
        IMethodSymbol callable,
        CancellationToken cancellationToken)
    {
        return CreateCore(callable, null, cancellationToken);
    }

    private bool IsCallableBodyRoot(
        IMethodSymbol callable,
        IOperation implementationBody,
        CancellationToken cancellationToken)
    {
        var candidate = GetBody(implementationBody.Syntax) ??
            implementationBody.Syntax;
        if (GetDeclaredBodies(callable, cancellationToken).Any(body =>
                HasSameSite(body, candidate)))
        {
            return true;
        }

        return candidate is CompilationUnitSyntax &&
            _compilation.GetEntryPoint(cancellationToken) is { } entryPoint &&
            HaveSameDefinition(callable, entryPoint) &&
            entryPoint.Locations.Any(location =>
                location.SourceTree == candidate.SyntaxTree &&
                candidate.Span.Contains(location.SourceSpan));
    }

    private ContractClauseInventory CreateCore(
        IMethodSymbol callable,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        var found = new List<(
            BoundContractKind Kind,
            ContractClausePlacement Placement,
            IInvocationOperation Invocation,
            int TreeOrdinal)>();
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
                if (_api?.GetClauseKind(targetMethod) is not { } kind)
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
                found.Add((kind, Classify(
                    invocation,
                    model,
                    body,
                    ownedByCallable,
                    cancellationToken), invocation,
                    GetTreeOrdinal(invocation.Syntax.SyntaxTree)));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
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

    private bool TryGetSemanticModel(
        SyntaxTree tree,
        out SemanticModel model)
    {
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
        IInvocationOperation invocation,
        SemanticModel model,
        SyntaxNode body,
        bool ownedByCallable,
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
                cancellationToken,
                out var placement))
        {
            return placement;
        }

        return invocation.Syntax.Ancestors()
            .TakeWhile(ancestor => !HasSameSite(ancestor, body))
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
        CancellationToken cancellationToken,
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

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDirectClause(model, prior, cancellationToken))
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

    private bool IsDirectClause(
        SemanticModel model,
        StatementSyntax statement,
        CancellationToken cancellationToken)
    {
        return statement is ExpressionStatementSyntax expression &&
        model.GetOperation(
            expression.Expression,
            cancellationToken) is IInvocationOperation invocation &&
        _api!.GetClauseKind(invocation.TargetMethod).HasValue;
    }

    private static bool IsReachable(
        SyntaxNode syntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        if (callable.OriginalDefinition.AssociatedSymbol is
                IPropertySymbol property &&
            property.PartialImplementationPart != null)
        {
            return GetPropertyAccessor(callable, property, useImplementation: true);
        }
        return null;
    }

    private static ImmutableArray<SyntaxNode> GetDeclaredBodies(
        IMethodSymbol callable,
        CancellationToken cancellationToken)
    {
        var bodies = ImmutableArray.CreateBuilder<SyntaxNode>();
        foreach (var reference in callable.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetBody(reference.GetSyntax(cancellationToken)) is { } body)
            {
                bodies.Add(body);
            }
        }

        return bodies.ToImmutable();
    }

    internal static SyntaxNode? GetBody(SyntaxNode syntax)
    {
        return syntax switch
        {
            BaseMethodDeclarationSyntax { Body: { } body } => body,
            BaseMethodDeclarationSyntax { ExpressionBody.Expression: { } expression } method =>
                GetExpressionBodyOperationRoot(method, expression),
            AccessorDeclarationSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
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
        SyntaxNode declaration,
        ExpressionSyntax expression)
    {
        // Roslyn exposes no operation for an isolated `ref value` syntax.
        // The declaration owns the corresponding method-body operation.
        return expression is RefExpressionSyntax ? declaration : expression;
    }

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right)
    {
        return SyntaxSite.IsSame(left, right);
    }

    internal static IMethodSymbol NormalizeCallable(IMethodSymbol method)
    {
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
        return SymbolEqualityComparer.Default.Equals(
            GetPartialDefinition(left),
            GetPartialDefinition(right));
    }

    private static IMethodSymbol GetPartialDefinition(IMethodSymbol method)
    {
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
        var part = useImplementation
            ? property.PartialImplementationPart
            : property.PartialDefinitionPart;
        return method.MethodKind == MethodKind.PropertyGet
            ? part?.GetMethod
            : part?.SetMethod;
    }
}
