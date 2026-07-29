namespace SharpProof.Analyzer;

internal sealed class RequiresCallSiteDiscovery(
    IMethodSymbol caller,
    SyntaxNode declaration,
    SemanticModel semanticModel,
    CancellationToken cancellationToken)
{
    internal ImmutableArray<RequiresCallSiteCandidate>? Get(
        BoundMethodContracts? callerContracts)
    {
        if (!TryCreateGraph(out var operationRoot, out var graph))
        {
            return null;
        }

        var managedFlow = ManagedAbstractFlow.ForCompilation(semanticModel.Compilation);
        var entryState = ManagedContractFacts.ApplyRequires(
            managedFlow.CreateEntryState(caller),
            callerContracts);
        var flowAnalysis = managedFlow.Analyze(
            caller,
            graph,
            entryState,
            cancellationToken);
        var flowResult = flowAnalysis.Result;
        var callSites = new Dictionary<
            (SyntaxTree Tree, TextSpan Span),
            RequiresCallSiteCandidate>();
        var initializer = (operationRoot as IConstructorBodyOperation)?.Initializer;
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!block.IsReachable)
            {
                continue;
            }

            var roots = block.Operations
                .Concat(block.BranchValue == null ? [] : [block.BranchValue])
                .Concat(
                    block.Ordinal == graph.Blocks[0].Ordinal &&
                    initializer != null
                        ? [initializer]
                        : []);
            foreach (var operation in roots.SelectMany(
                         static root => root.DescendantsAndSelf()))
            {
                var call = GetCall(operation);
                if (call == null)
                {
                    continue;
                }

                var hasFlowState =
                    flowResult?.TryGetState(operation, out _) == true;
                if (flowAnalysis.IsComplete &&
                    !hasFlowState &&
                    !IsInsideExceptionHandler(operation))
                {
                    continue;
                }

                var candidate = new RequiresCallSiteCandidate(
                    operation,
                    call.Value.TargetMethod,
                    call.Value.Instance,
                    call.Value.Arguments,
                    (hasFlowState || !flowAnalysis.IsComplete) &&
                    HasReplayablePrefix(operation, operationFacts),
                    hasFlowState ? flowResult : null,
                    flowAnalysis.Status);
                var key = (
                    operation.Syntax.SyntaxTree,
                    operation.Syntax.Span);
                if (!callSites.TryGetValue(key, out var existing) ||
                    !existing.CanReplay && candidate.CanReplay)
                {
                    callSites[key] = candidate;
                }
            }
        }

        return [
            .. callSites.Values.OrderBy(
                static candidate => candidate.Operation.Syntax.SpanStart)
        ];
    }

    private bool TryCreateGraph(
        out IOperation? operationRoot,
        out ControlFlowGraph graph)
    {
        try
        {
            var flowSyntax = GetPropertyExpression(declaration) ?? declaration;
            operationRoot = semanticModel.GetOperation(
                flowSyntax,
                cancellationToken);
            while (operationRoot?.Parent != null)
            {
                operationRoot = operationRoot.Parent;
            }

            var created = operationRoot switch
            {
                IMethodBodyOperation method =>
                    ControlFlowGraph.Create(method, cancellationToken),
                IConstructorBodyOperation constructor =>
                    ControlFlowGraph.Create(constructor, cancellationToken),
                IBlockOperation block =>
                    ControlFlowGraph.Create(block, cancellationToken),
                _ => ControlFlowGraph.Create(
                    declaration,
                    semanticModel,
                    cancellationToken)
            };
            if (created == null)
            {
                graph = null!;
                return false;
            }
            graph = created;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            operationRoot = null;
            graph = null!;
            return false;
        }
    }

    private bool HasReplayablePrefix(
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        if (declaration is BaseMethodDeclarationSyntax
            {
                ExpressionBody.Expression: { } expressionBody
            })
        {
            return expressionBody.Span == callSite.Syntax.Span;
        }

        if (declaration is AccessorDeclarationSyntax
            {
                ExpressionBody.Expression: { } accessorExpression
            })
        {
            return accessorExpression.Span == callSite.Syntax.Span;
        }

        var propertyExpression = GetPropertyExpression(declaration);
        if (propertyExpression != null)
        {
            return propertyExpression.Span == callSite.Syntax.Span;
        }

        if (declaration is ConstructorDeclarationSyntax constructor &&
            callSite.Syntax is ConstructorInitializerSyntax initializer &&
            ReferenceEquals(initializer.Parent, constructor))
        {
            return true;
        }

        var body = declaration switch
        {
            BaseMethodDeclarationSyntax method => method.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
        if (body == null)
        {
            return false;
        }

        var statement = callSite.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate => ReferenceEquals(
                candidate.Parent,
                body));
        return statement != null &&
               IsDirectReplayableStatement(
                   statement,
                   callSite,
                   operationFacts) &&
               body.Statements
                   .TakeWhile(candidate => !ReferenceEquals(
                       candidate,
                       statement))
                   .All(prior =>
                       prior is EmptyStatementSyntax or
                           LocalFunctionStatementSyntax ||
                       operationFacts.CompletesNormally(
                           semanticModel.GetOperation(
                               prior,
                               cancellationToken)));
    }

    private bool IsDirectReplayableStatement(
        StatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        var span = callSite.Syntax.Span;
        return statement switch
        {
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } when assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression) =>
                assignment.Right.Span == span &&
                operationFacts.CompletesNormally(
                    semanticModel.GetOperation(
                        assignment.Left,
                        cancellationToken)),
            ExpressionStatementSyntax expression =>
                expression.Expression.Span == span,
            LocalDeclarationStatementSyntax local =>
                local.Declaration.Variables.Count == 1 &&
                local.Declaration.Variables[0]
                    .Initializer?.Value.Span == span,
            ReturnStatementSyntax returned =>
                returned.Expression?.Span == span,
            ThrowStatementSyntax thrown =>
                thrown.Expression?.Span == span,
            _ => false
        };
    }

    private static RequiresCallTarget? GetCall(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => new(
                invocation.TargetMethod,
                invocation.Instance,
                invocation.Arguments),
            IObjectCreationOperation
            {
                Constructor: { } constructor
            } creation => new(
                constructor,
                null,
                creation.Arguments),
            _ => null
        };
    }

    private static ExpressionSyntax? GetPropertyExpression(
        SyntaxNode declaration)
    {
        return declaration switch
        {
            PropertyDeclarationSyntax property =>
                property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer =>
                indexer.ExpressionBody?.Expression,
            _ => null
        };
    }

    private static bool IsInsideExceptionHandler(IOperation operation)
    {
        return operation.Syntax.AncestorsAndSelf().Any(
            static syntax =>
                syntax is CatchClauseSyntax or
                    CatchFilterClauseSyntax or
                    FinallyClauseSyntax);
    }

    private readonly record struct RequiresCallTarget(
        IMethodSymbol TargetMethod,
        IOperation? Instance,
        ImmutableArray<IArgumentOperation> Arguments);
}

internal readonly record struct RequiresCallSiteCandidate(
    IOperation Operation,
    IMethodSymbol TargetMethod,
    IOperation? Instance,
    ImmutableArray<IArgumentOperation> Arguments,
    bool CanReplay,
    ManagedFlowResult? Flow,
    ManagedFlowStatus FlowStatus);
