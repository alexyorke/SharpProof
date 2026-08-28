namespace SharpProof.Analyzer;

internal sealed partial class RequiresCallSiteDiscovery(
    IMethodSymbol caller,
    SyntaxNode declaration,
    SemanticModel semanticModel,
    CancellationToken cancellationToken,
    ControlFlowGraph? suppliedGraph = null,
    IOperation? suppliedOperationRoot = null)
{
    private readonly InvocationEmissionPolicy _invocationEmission =
        new(semanticModel.Compilation);

    internal bool HasPotentialCallSite(
        Func<IMethodSymbol, bool> hasPotentialPreconditions)
    {
        var owners = GetPotentialCallOwners(
            hasPotentialPreconditions);
        return owners == null ||
            owners.Contains(
                ContractClauseInventoryBuilder
                    .NormalizeCallable(caller));
    }

    internal ImmutableHashSet<IMethodSymbol>?
        GetPotentialCallOwners(
            Func<IMethodSymbol, bool>
                hasPotentialPreconditions)
    {
        hasPotentialPreconditions = ArgumentNullGuard.NotNull(
            hasPotentialPreconditions, nameof(hasPotentialPreconditions));

        if (!TryGetOperationRoot(out var operationRoot))
        {
            return null;
        }

        var owners = ImmutableHashSet.CreateBuilder<
            IMethodSymbol>(
            SymbolEqualityComparer.Default);
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        foreach (var operation in
                 ExecutableDescendantsAndSelf(operationRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = GetCalls(
                operation,
                operationFacts,
                semanticModel.Compilation,
                cancellationToken,
                semanticModel,
                caller);
            if (calls.IsDefaultOrEmpty)
            {
                continue;
            }
            foreach (var call in calls)
            {
                var target =
                    call.TargetMethod.ReducedFrom ??
                    call.TargetMethod;
                if (hasPotentialPreconditions(target))
                {
                    var owner = semanticModel.GetEnclosingSymbol(
                        operation.Syntax.SpanStart,
                        cancellationToken) as IMethodSymbol;
                    if (owner == null)
                    {
                        return null;
                    }

                    owners.Add(
                        ContractClauseInventoryBuilder
                            .NormalizeCallable(owner));
                }
            }
        }

        if (TryGetImplicitParameterlessBaseConstructor(out var baseConstructor) &&
            hasPotentialPreconditions(baseConstructor))
        {
            owners.Add(
                ContractClauseInventoryBuilder
                    .NormalizeCallable(caller));
        }

        return owners.ToImmutable();
    }

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
        var callSites = new List<RequiresCallSiteCandidate>();
        var reachableOperationSites = new HashSet<(
            SyntaxTree Tree, int Start, int Length)>();
        var initializer = (operationRoot as IConstructorBodyOperation)?.Initializer;
        if (TryGetImplicitParameterlessBaseConstructor(out var baseConstructor))
        {
            var constructorBody = operationRoot as IConstructorBodyOperation;
            var origin = (IOperation?)constructorBody?.BlockBody ??
                constructorBody?.ExpressionBody ??
                operationRoot!;
            callSites.Add(new RequiresCallSiteCandidate(
                origin,
                baseConstructor,
                Instance: null,
                Arguments: [],
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                CanReplay: true,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded));
        }
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
                         ExecutableDescendantsAndSelf))
            {
                reachableOperationSites.Add((
                    operation.Syntax.SyntaxTree,
                    operation.Syntax.SpanStart,
                    operation.Syntax.Span.Length));
                var calls = GetCalls(
                    operation,
                    operationFacts,
                    semanticModel.Compilation,
                    cancellationToken,
                    semanticModel,
                    caller);
                var isSynthesizedDispose =
                    operation is IInvocationOperation invocation &&
                    UsingDisposalEffectResolver
                        .IsSynthesizedSynchronousDispose(invocation);
                if (calls.IsDefaultOrEmpty ||
                    !SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetEnclosingSymbol(
                            operation.Syntax.SpanStart,
                            cancellationToken),
                        caller))
                {
                    continue;
                }

                var hasFlowState =
                    flowResult?.TryGetState(operation, out _) == true;
                if (flowAnalysis.IsComplete &&
                    !hasFlowState &&
                    !IsInsideExceptionHandler(operation) &&
                    operation is not IListPatternOperation &&
                    !isSynthesizedDispose)
                {
                    continue;
                }

                foreach (var call in calls)
                {
                    if (call.TargetMethod.MethodKind == MethodKind.PropertySet &&
                        operation is IPropertyReferenceOperation property &&
                        property.Parent is ICoalesceAssignmentOperation coalesce &&
                        ReferenceEquals(coalesce.Target, property) &&
                        !CanCoalesceGetterComplete(property, operationFacts))
                    {
                        continue;
                    }
                    var canReplay = isSynthesizedDispose
                        ? call.Instance == null ||
                            operationFacts.CompletesNormally(
                                call.Instance)
                        : (IsAccessorCall(call.TargetMethod) ||
                            operation is IListPatternOperation)
                            ? HasReplayableAccessorEvaluation(
                                call,
                                operation,
                                operationFacts,
                                flowResult,
                                semanticModel,
                                cancellationToken) &&
                                HasReplayableBlockPrefix(
                                    operation,
                                    operationFacts)
                            : (hasFlowState || !flowAnalysis.IsComplete) &&
                                HasReplayablePrefix(
                                    operation,
                                    operationFacts);
                    var candidate = new RequiresCallSiteCandidate(
                        operation,
                        call.TargetMethod,
                        call.Instance,
                        call.Arguments,
                        call.ExplicitArguments,
                        call.SyntheticArguments,
                        call.CanReplay && canReplay,
                        hasFlowState ? flowResult : null,
                        flowAnalysis.Status);
                    var existingIndex = callSites.FindIndex(existing =>
                        existing.Operation.Syntax.SyntaxTree ==
                            operation.Syntax.SyntaxTree &&
                        existing.Operation.Syntax.Span ==
                            operation.Syntax.Span &&
                        SymbolEqualityComparer.Default.Equals(
                            existing.TargetMethod,
                            candidate.TargetMethod) &&
                        existing.SyntheticArguments.Count ==
                            candidate.SyntheticArguments.Count &&
                        existing.SyntheticArguments.All(pair =>
                            candidate.SyntheticArguments.TryGetValue(
                                pair.Key, out var value) &&
                            value == pair.Value));
                    if (existingIndex < 0)
                    {
                        callSites.Add(candidate);
                    }
                    else if (!callSites[existingIndex].CanReplay &&
                             candidate.CanReplay)
                    {
                        callSites[existingIndex] = candidate;
                    }
                }
            }
        }

        foreach (var property in ExecutableDescendantsAndSelf(operationRoot!)
                     .OfType<IPropertyReferenceOperation>()
                     .Where(static property =>
                         property.Parent is ICoalesceAssignmentOperation coalesce &&
                         ReferenceEquals(coalesce.Target, property))
                     .Where(property => reachableOperationSites.Contains((
                         property.Syntax.SyntaxTree,
                         property.Syntax.SpanStart,
                         property.Syntax.Span.Length)))
                     .Where(property =>
                         SymbolEqualityComparer.Default.Equals(
                             semanticModel.GetEnclosingSymbol(
                                 property.Syntax.SpanStart,
                             cancellationToken),
                             caller))
                     .Where(property =>
                         CanCoalesceGetterComplete(property, operationFacts)))
        {
            foreach (var call in GetPropertyCalls(property).Where(static call =>
                         call.TargetMethod.MethodKind == MethodKind.PropertySet))
            {
                if (callSites.Any(existing =>
                        existing.Operation.Syntax.SyntaxTree ==
                            property.Syntax.SyntaxTree &&
                        existing.Operation.Syntax.Span == property.Syntax.Span &&
                        SymbolEqualityComparer.Default.Equals(
                            existing.TargetMethod,
                            call.TargetMethod)))
                {
                    continue;
                }

                callSites.Add(new RequiresCallSiteCandidate(
                    property,
                    call.TargetMethod,
                    call.Instance,
                    call.Arguments,
                    call.ExplicitArguments,
                    call.SyntheticArguments,
                    CanReplay: false,
                    Flow: null,
                    ManagedFlowStatus.BudgetExceeded));
            }
        }

        return [
            .. callSites.OrderBy(
                static candidate => candidate.Operation.Syntax.SpanStart)
        ];
    }

    private IEnumerable<IOperation> ExecutableDescendantsAndSelf(
        IOperation operation)
    {
        if (operation is IInvocationOperation invocation &&
            _invocationEmission.IsElided(invocation))
        {
            yield break;
        }
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in ExecutableDescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    internal bool TryCreateGraph(
        out IOperation? operationRoot,
        out ControlFlowGraph graph)
    {
        if (suppliedGraph != null)
        {
            operationRoot =
                suppliedOperationRoot ??
                suppliedGraph.OriginalOperation;
            graph = suppliedGraph;
            return true;
        }

        if (!TryGetOperationRoot(out operationRoot))
        {
            graph = null!;
            return false;
        }

        try
        {
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
            graph = null!;
            return false;
        }
    }

    private bool TryGetOperationRoot(
        out IOperation operationRoot)
    {
        if (suppliedOperationRoot != null)
        {
            operationRoot = suppliedOperationRoot;
            return true;
        }

        try
        {
            var flowSyntax =
                GetPropertyExpression(declaration) ??
                declaration;
            var operation = semanticModel.GetOperation(
                flowSyntax,
                cancellationToken);
            while (operation?.Parent != null)
            {
                operation = operation.Parent;
            }

            operationRoot = operation!;
            return operation != null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException)
        {
            operationRoot = null!;
            return false;
        }
    }

    private bool TryGetImplicitParameterlessBaseConstructor(
        out IMethodSymbol baseConstructor)
    {
        baseConstructor = null!;
        if (declaration is not ConstructorDeclarationSyntax
            {
                Initializer: null
            } ||
            caller is not
            {
                MethodKind: MethodKind.Constructor,
                IsStatic: false
            } ||
            caller.ContainingType.TypeKind != TypeKind.Class ||
            IsRecordCopyConstructor(caller))
        {
            return false;
        }

        var candidates = caller.ContainingType.BaseType?
            .InstanceConstructors
            .Where(static constructor =>
                constructor.Parameters.IsEmpty)
            .ToImmutableArray() ?? [];
        if (candidates.Length != 1)
        {
            return false;
        }

        baseConstructor = candidates[0];
        return true;
    }

    internal static bool IsRecordCopyConstructor(
        IMethodSymbol constructor)
    {
        return constructor.ContainingType.IsRecord &&
            constructor.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(
                constructor.Parameters[0].Type,
                constructor.ContainingType);
    }

    private bool HasReplayablePrefix(
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        var body =
            ContractClauseInventoryBuilder.GetBody(
                declaration);
        if (body is ExpressionSyntax expression)
        {
            return IsOwnedCallSiteExpression(
                expression,
                callSite.Syntax);
        }

        if (declaration is ConstructorDeclarationSyntax constructor &&
            callSite.Syntax is ConstructorInitializerSyntax initializer &&
            ReferenceEquals(initializer.Parent, constructor))
        {
            return true;
        }

        if (body is not BlockSyntax block)
        {
            return false;
        }

        var statement = callSite.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate => ReferenceEquals(
                candidate.Parent,
                block));
        return statement != null &&
               IsDirectReplayableStatement(
                   statement,
                   callSite,
                   operationFacts) &&
               block.Statements
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

    private static bool IsAccessorCall(IMethodSymbol method)
    {
        return method.MethodKind is
            MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove;
    }

    private static bool HasReplayableAccessorEvaluation(
        RequiresCallTarget call,
        IOperation operation,
        DefiniteOperationFacts operationFacts,
        ManagedFlowResult? flowResult,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return (call.Instance == null ||
                operationFacts.CompletesNormally(call.Instance)) &&
            HasDefinitelyExecutingConditionalAccess(
                operation,
                call.Instance,
                flowResult,
                semanticModel,
                cancellationToken) &&
            call.Arguments.All(argument =>
                operationFacts.CompletesNormally(argument.Value)) &&
            call.ExplicitArguments.Values.All(
                operationFacts.CompletesNormally);
    }

    private static bool HasDefinitelyExecutingConditionalAccess(
        IOperation operation,
        IOperation? instance,
        ManagedFlowResult? flowResult,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (instance == null)
        {
            return true;
        }

        var conditional = operation.Syntax.Ancestors()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Select(access =>
                semanticModel.GetOperation(access, cancellationToken)
                    as IConditionalAccessOperation)
            .FirstOrDefault(access => access != null &&
                access.WhenNotNull.Syntax.Span == operation.Syntax.Span);
        if (conditional == null)
        {
            return true;
        }

        return DefiniteOperationFacts.IsDefinitelyNonNull(
                   conditional.Operation) ||
            flowResult?.ProvesNonNull(
                conditional,
                conditional.Operation) == true;
    }

    private bool HasReplayableBlockPrefix(
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        var body = ContractClauseInventoryBuilder.GetBody(declaration);
        if (body is not BlockSyntax block)
        {
            return true;
        }

        var statement = callSite.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate => ReferenceEquals(
                candidate.Parent,
                block));
        return statement != null &&
            IsTransparentConditionalAccessorStatement(
                statement,
                callSite) &&
            block.Statements
                .TakeWhile(candidate => !ReferenceEquals(
                    candidate,
                    statement))
                .All(prior =>
                    prior is EmptyStatementSyntax or
                        LocalFunctionStatementSyntax ||
                    operationFacts.CompletesNormally(
                        semanticModel.GetOperation(
                            prior,
                            cancellationToken)) ||
                    IsConditionalAccessorPrefixStatement(
                        prior,
                        callSite));
    }

    private bool IsConditionalAccessorPrefixStatement(
        StatementSyntax statement,
        IOperation callSite)
    {
        if (!IsConditionalAccessorOperation(callSite))
        {
            return false;
        }

        var operation = semanticModel.GetOperation(
            statement,
            cancellationToken);
        return operation is IConditionalOperation &&
            new DefiniteOperationFacts(
                semanticModel.Compilation,
                cancellationToken).MayCompleteNormally(operation);
    }

    private bool IsTransparentConditionalAccessorStatement(
        StatementSyntax statement,
        IOperation callSite)
    {
        if (!IsConditionalAccessorOperation(callSite))
        {
            return true;
        }

        var statementOperation = semanticModel.GetOperation(
            statement,
            cancellationToken);
        var expression = statementOperation switch
        {
            IReturnOperation { ReturnedValue: { } value } => value,
            IExpressionStatementOperation { Operation: { } value } => value,
            _ => null
        };
        if (expression == null)
        {
            return false;
        }

        var property = expression.DescendantsAndSelf()
            .OfType<IPropertyReferenceOperation>()
            .FirstOrDefault(candidate =>
                candidate.Syntax.Span == callSite.Syntax.Span);
        if (property?.Parent is not IConditionalAccessOperation access ||
            access.WhenNotNull.Syntax.Span != property.Syntax.Span)
        {
            return false;
        }

        IOperation current = access;
        while (current.Parent is IParenthesizedOperation parenthesized &&
               parenthesized.Syntax.Span.Contains(current.Syntax.Span))
        {
            current = parenthesized;
        }

        if (current.Parent is ICoalesceOperation coalesce &&
            ReferenceEquals(coalesce.Value, current))
        {
            current = coalesce;
        }

        return current.Syntax.Span == expression.Syntax.Span;
    }

    private bool IsConditionalAccessorOperation(IOperation callSite)
    {
        if (callSite is not IPropertyReferenceOperation)
        {
            return false;
        }

        return callSite.Syntax.Ancestors()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Select(access =>
                semanticModel.GetOperation(access, cancellationToken)
                    as IConditionalAccessOperation)
            .Any(access => access != null &&
                access.WhenNotNull.Syntax.Span == callSite.Syntax.Span);
    }

    private static bool CanCoalesceGetterComplete(
        IPropertyReferenceOperation property,
        DefiniteOperationFacts operationFacts)
    {
        return (property.Instance == null ||
                operationFacts.MayCompleteNormally(property.Instance)) &&
            property.Arguments.All(argument =>
                operationFacts.MayCompleteNormally(argument.Value)) &&
            property.Parent is ICoalesceAssignmentOperation coalesce &&
            operationFacts.MayCompleteNormally(coalesce.Value) &&
            property.Property.GetMethod is { } getter &&
            operationFacts.MethodCanCompleteNormally(getter);
    }

    private bool IsDirectReplayableStatement(
        StatementSyntax statement,
        IOperation callSite,
        DefiniteOperationFacts operationFacts)
    {
        return statement switch
        {
            ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } when assignment.IsKind(
                SyntaxKind.SimpleAssignmentExpression) =>
                IsOwnedCallSiteExpression(
                    assignment.Right,
                    callSite.Syntax) &&
                operationFacts.CompletesNormally(
                    semanticModel.GetOperation(
                        assignment.Left,
                        cancellationToken)),
            ExpressionStatementSyntax expression =>
                IsOwnedCallSiteExpression(
                    expression.Expression,
                    callSite.Syntax),
            LocalDeclarationStatementSyntax local =>
                local.Declaration.Variables.Count == 1 &&
                IsOwnedCallSiteExpression(
                    local.Declaration.Variables[0]
                        .Initializer?.Value,
                    callSite.Syntax),
            ReturnStatementSyntax returned =>
                IsOwnedCallSiteExpression(
                    returned.Expression,
                    callSite.Syntax),
            ThrowStatementSyntax thrown =>
                IsOwnedCallSiteExpression(
                    thrown.Expression,
                    callSite.Syntax),
            _ => false
        };
    }

    private static bool IsOwnedCallSiteExpression(
        ExpressionSyntax? expression,
        SyntaxNode callSiteSyntax)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression?.Span == callSiteSyntax.Span;
    }

    private static ImmutableArray<RequiresCallTarget> GetCalls(
        IOperation operation,
        DefiniteOperationFacts? operationFacts = null,
        Compilation? compilation = null,
        CancellationToken cancellationToken = default,
        SemanticModel? semanticModel = null,
        IMethodSymbol? caller = null)
    {
        return operation switch
        {
            IUsingOperation @using when
                compilation != null && caller != null &&
                !@using.IsAsynchronous =>
                GetUsingCalls(
                    @using.Resources,
                    compilation,
                    caller),
            IUsingDeclarationOperation declaration when
                compilation != null && caller != null &&
                !declaration.IsAsynchronous =>
                GetUsingCalls(
                    declaration.DeclarationGroup,
                    compilation,
                    caller),
            IInvocationOperation invocation when
                compilation != null &&
                caller != null &&
                semanticModel != null &&
                UsingDisposalEffectResolver.IsSynthesizedSynchronousDispose(
                    invocation) =>
                GetSynthesizedDisposeCalls(
                    invocation,
                    compilation,
                    caller,
                    semanticModel,
                    cancellationToken),
            IInvocationOperation invocation => [new(
                invocation.TargetMethod,
                invocation.Instance,
                invocation.Arguments,
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true)],
            IObjectCreationOperation
            {
                Constructor: { } constructor
            } creation => [new(
                constructor,
                null,
                creation.Arguments,
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true)],
            IPropertyReferenceOperation property =>
                GetPropertyCalls(property, semanticModel, cancellationToken),
            IEventReferenceOperation eventReference =>
                GetEventCalls(eventReference),
            IListPatternOperation listPattern => GetListPatternCalls(
                listPattern,
                operationFacts,
                compilation,
                cancellationToken),
            _ => []
        };
    }

    private static ImmutableArray<RequiresCallTarget> GetUsingCalls(
        IOperation resources,
        Compilation compilation,
        IMethodSymbol caller)
    {
        var candidates = new List<(ITypeSymbol Type, IOperation Resource)>();
        if (resources is IVariableDeclarationGroupOperation group)
        {
            foreach (var declarator in group.Declarations.SelectMany(
                         static declaration => declaration.Declarators))
            {
                if (declarator.Initializer?.Value is { } resource)
                {
                    candidates.Add((
                        GetConcreteResourceType(
                            declarator.Symbol.Type,
                            resource),
                        resource));
                }
            }
        }
        else if (resources.Type is { } resourceType)
        {
            candidates.Add((
                GetConcreteResourceType(resourceType, resources),
                resources));
        }

        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        foreach (var candidate in candidates)
        {
            var dispose = UsingDisposalEffectResolver.ResolveDispose(
                compilation,
                caller,
                candidate.Type);
            if (dispose == null)
            {
                continue;
            }

            calls.Add(new RequiresCallTarget(
                dispose,
                candidate.Resource,
                [],
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true));
        }
        return calls.ToImmutable();
    }

    private static ImmutableArray<RequiresCallTarget>
        GetSynthesizedDisposeCalls(
            IInvocationOperation invocation,
            Compilation compilation,
            IMethodSymbol caller,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
    {
        var resource = GetUsingResource(
            invocation,
            semanticModel,
            cancellationToken);
        var resourceType = resource?.Type ??
            semanticModel.GetSymbolInfo(
                    invocation.Syntax,
                    cancellationToken)
                .Symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                _ => invocation.Instance?.Type
            };
        if (resourceType == null)
        {
            return [];
        }

        var resourceOperation = resource ?? invocation.Instance;
        if (resourceOperation == null ||
            DefiniteOperationFacts.IsDefinitelyNull(resourceOperation))
        {
            return [];
        }

        var dispose = UsingDisposalEffectResolver.ResolveDispose(
            compilation,
            caller,
            GetConcreteResourceType(
                resourceType,
                resourceOperation));
        return dispose == null
            ? []
            : [new RequiresCallTarget(
                dispose,
                resourceOperation,
                [],
                ImmutableDictionary<int, IOperation>.Empty,
                ImmutableDictionary<int, long>.Empty,
                true)];
    }

    private static IOperation? GetUsingResource(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var usingSyntax = invocation.Syntax.AncestorsAndSelf()
            .FirstOrDefault(static syntax =>
                syntax is UsingStatementSyntax ||
                syntax is LocalDeclarationStatementSyntax
                {
                    UsingKeyword.RawKind: not 0
                });
        var usingOperation = usingSyntax == null
            ? null
            : semanticModel.GetOperation(
                usingSyntax,
                cancellationToken);
        return usingOperation switch
        {
            IUsingOperation @using => @using.Resources,
            IUsingDeclarationOperation declaration =>
                declaration.DeclarationGroup.Declarations
                    .SelectMany(static group => group.Declarators)
                    .Select(static declarator => declarator.Initializer?.Value)
                    .FirstOrDefault(static value => value != null),
            _ => null
        };
    }

    private static ITypeSymbol GetConcreteResourceType(
        ITypeSymbol declaredType,
        IOperation resource)
    {
        resource = DefiniteOperationFacts.UnwrapHarmlessValue(resource);
        return declaredType is INamedTypeSymbol
        {
            TypeKind: TypeKind.Interface
        } &&
        resource.Type is INamedTypeSymbol
        {
            TypeKind: not TypeKind.Interface
        } concrete
            ? concrete
            : declaredType;
    }

    private static ImmutableArray<RequiresCallTarget> GetListPatternCalls(
        IListPatternOperation pattern,
        DefiniteOperationFacts? operationFacts,
        Compilation? compilation,
        CancellationToken cancellationToken)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (instance != null &&
            DefiniteOperationFacts.IsDefinitelyNull(instance))
        {
            return [];
        }

        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        var length = SwitchExpressionFacts.GetCallableListPatternMember(
            pattern.LengthSymbol);
        var knownLength = 0L;
        var hasKnownLength = compilation != null &&
            TryGetKnownListLength(
                pattern,
                instance,
                compilation,
                cancellationToken,
                out knownLength);
        if (length != null)
        {
            calls.Add(CreateImplicitListPatternCall(
                length,
                instance,
                ImmutableDictionary<int, long>.Empty));
            if (operationFacts != null &&
                !operationFacts.MethodCanCompleteNormally(length))
            {
                return calls.ToImmutable();
            }
        }

        var requiredLength = pattern.Patterns.Count(
            static item => item is not ISlicePatternOperation);
        var hasSlice = pattern.Patterns.Any(
            static item => item is ISlicePatternOperation);
        if (hasKnownLength &&
            (hasSlice
                ? knownLength < requiredLength
                : knownLength != requiredLength))
        {
            return calls.ToImmutable();
        }

        for (var index = 0; index < pattern.Patterns.Length; index++)
        {
            var item = pattern.Patterns[index];
            var member = item is ISlicePatternOperation slice
                ? slice.Pattern == null
                    ? null
                    : SwitchExpressionFacts.GetCallableListPatternMember(
                        slice.SliceSymbol)
                : SwitchExpressionFacts.GetCallableListPatternMember(
                    pattern.IndexerSymbol);
            if (member == null)
            {
                continue;
            }
            var syntheticArguments = ImmutableDictionary<int, long>.Empty;
            if (item is ISlicePatternOperation && hasKnownLength)
            {
                var prefixLength = pattern.Patterns
                    .Take(index)
                    .Count(static candidate =>
                        candidate is not ISlicePatternOperation);
                var sliceLength = knownLength - requiredLength;
                if (member.Parameters.Length > 0)
                {
                    syntheticArguments = syntheticArguments.Add(0, prefixLength);
                }
                if (member.Parameters.Length > 1)
                {
                    syntheticArguments = syntheticArguments.Add(1, sliceLength);
                }
            }
            else if (item is not ISlicePatternOperation &&
                     member.Parameters.Length > 0)
            {
                var hasSliceBefore = pattern.Patterns
                    .Take(index)
                    .Any(static candidate =>
                        candidate is ISlicePatternOperation);
                var argument = hasSliceBefore && hasKnownLength
                    ? knownLength - pattern.Patterns
                        .Skip(index + 1)
                        .Count(static candidate =>
                            candidate is not ISlicePatternOperation) - 1
                    : pattern.Patterns
                        .Take(index)
                        .Count(static candidate =>
                            candidate is not ISlicePatternOperation);
                if (!hasSliceBefore || hasKnownLength)
                {
                    syntheticArguments = syntheticArguments.Add(0, argument);
                }
            }
            calls.Add(CreateImplicitListPatternCall(
                member,
                instance,
                syntheticArguments));
            if (operationFacts != null &&
                !operationFacts.MethodCanCompleteNormally(member))
            {
                break;
            }
        }
        return calls.ToImmutable();
    }

    private static RequiresCallTarget CreateImplicitListPatternCall(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableDictionary<int, long> syntheticArguments)
    {
        return new RequiresCallTarget(
            method,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            syntheticArguments,
            true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1508:Avoid dead conditional code",
        Justification = "The analyzer misreads the multi-branch nullable " +
            "assignment above the null check as unreachable.")]
    private static bool TryGetKnownListLength(
        IListPatternOperation pattern,
        IOperation? instance,
        Compilation compilation,
        CancellationToken cancellationToken,
        out long length)
    {
        instance = instance == null
            ? null
            : DefiniteOperationFacts.UnwrapHarmlessValue(instance);
        if (instance is IArrayCreationOperation
            { DimensionSizes.Length: 1 } arrayCreation &&
            arrayCreation.DimensionSizes[0].ConstantValue is
            { HasValue: true, Value: int arrayLength })
        {
            length = arrayLength;
            return true;
        }
        if (pattern.LengthSymbol is not IPropertySymbol
            { GetMethod: { } getter } ||
            getter.IsVirtual && !getter.IsSealed ||
            getter.DeclaringSyntaxReferences.Length != 1)
        {
            length = 0;
            return false;
        }

        var declaration = getter.DeclaringSyntaxReferences[0]
            .GetSyntax(cancellationToken);
        var expression = declaration switch
        {
            PropertyDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax
            { ExpressionBody.Expression: { } body } => body,
            ArrowExpressionClauseSyntax
            { Expression: { } body } => body,
            AccessorDeclarationSyntax
            { Body.Statements.Count: 1 } accessor
                when accessor.Body!.Statements[0] is ReturnStatementSyntax
                { Expression: { } body } => body,
            _ => null
        };
        if (expression == null)
        {
            length = 0;
            return false;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue || constant.Value == null)
        {
            length = 0;
            return false;
        }
        try
        {
            length = Convert.ToInt64(
                constant.Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return length >= 0;
        }
        catch (Exception exception) when (exception is
            FormatException or InvalidCastException or OverflowException)
        {
            length = 0;
            return false;
        }
    }

    internal static ImmutableArray<RequiresCallSiteCandidate>
        CreateUnflowedCandidates(IOperation operation)
    {
        return [.. GetCalls(operation).Select(call =>
            new RequiresCallSiteCandidate(
                operation,
                call.TargetMethod,
                call.Instance,
                call.Arguments,
                call.ExplicitArguments,
                call.SyntheticArguments,
                call.CanReplay,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded))];
    }

    internal static IEnumerable<IOperation>
        ExecutableUnflowedDescendantsAndSelf(IOperation operation)
    {
        return ExecutableUnflowedDescendantsAndSelfCore(
            operation,
            operationFacts: null);
    }

    internal static IEnumerable<IOperation>
        ExecutableUnflowedDescendantsAndSelf(
            IOperation operation,
            DefiniteOperationFacts operationFacts)
    {
        return ExecutableUnflowedDescendantsAndSelfCore(
            operation,
            operationFacts);
    }

    private static IEnumerable<IOperation>
        ExecutableUnflowedDescendantsAndSelfCore(
            IOperation operation,
            DefiniteOperationFacts? operationFacts)
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
        {
            yield break;
        }

        if (operationFacts != null && operation is IInvocationOperation invocation)
        {
            if (invocation.Instance is { } instance)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             instance,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(instance))
                {
                    yield break;
                }
            }
            foreach (var argument in invocation.Arguments)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             argument.Value,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
            yield return invocation;
            yield break;
        }

        if (operationFacts != null &&
            operation is IObjectCreationOperation creation)
        {
            foreach (var argument in creation.Arguments)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             argument.Value,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
            yield return creation;
            if (creation.Constructor is { } constructor &&
                !operationFacts.MethodCanCompleteNormally(constructor))
            {
                yield break;
            }
            if (creation.Initializer != null)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             creation.Initializer,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operationFacts != null &&
            operation is IObjectOrCollectionInitializerOperation initializer)
        {
            yield return initializer;
            foreach (var item in initializer.Initializers)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             item,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(item))
                {
                    yield break;
                }
            }
            yield break;
        }

        if (operationFacts != null &&
            operation is ISimpleAssignmentOperation
            {
                Target: IPropertyReferenceOperation property
            } assignment)
        {
            yield return assignment;
            if (property.Instance is { } propertyInstance)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             propertyInstance,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(propertyInstance))
                {
                    yield break;
                }
            }
            foreach (var argument in property.Arguments)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             argument.Value,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         assignment.Value,
                         operationFacts))
            {
                yield return descendant;
            }
            if (operationFacts.MayCompleteNormally(assignment.Value))
            {
                yield return property;
            }
            yield break;
        }

        if (operationFacts != null &&
            operation is IPropertyReferenceOperation propertyReference)
        {
            if (propertyReference.Instance is { } propertyInstance)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             propertyInstance,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(propertyInstance))
                {
                    yield break;
                }
            }
            foreach (var argument in propertyReference.Arguments)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             argument.Value,
                             operationFacts))
                {
                    yield return descendant;
                }
                if (!operationFacts.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
            yield return propertyReference;
            yield break;
        }

        if (operationFacts != null &&
            operation is IConditionalOperation factConditional)
        {
            yield return factConditional;
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         factConditional.Condition,
                         operationFacts))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(
                    factConditional.Condition))
            {
                yield break;
            }
            if (factConditional.Condition.ConstantValue is
                { HasValue: true, Value: bool factCondition })
            {
                var branch = factCondition
                    ? factConditional.WhenTrue
                    : factConditional.WhenFalse;
                if (branch != null)
                {
                    foreach (var descendant in
                             ExecutableUnflowedDescendantsAndSelfCore(
                                 branch,
                                 operationFacts))
                    {
                        yield return descendant;
                    }
                }
                yield break;
            }
            foreach (var branch in new[]
                     {
                         factConditional.WhenTrue,
                         factConditional.WhenFalse
                     })
            {
                if (branch == null)
                {
                    continue;
                }
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             branch,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operationFacts != null && operation is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd or
                    BinaryOperatorKind.ConditionalOr
            } factBinary)
        {
            yield return factBinary;
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         factBinary.LeftOperand,
                         operationFacts))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factBinary.LeftOperand))
            {
                yield break;
            }
            var skipRight = factBinary.LeftOperand.ConstantValue is
            { HasValue: true, Value: bool leftValue } &&
                leftValue == (factBinary.OperatorKind ==
                    BinaryOperatorKind.ConditionalOr);
            if (!skipRight)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             factBinary.RightOperand,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operationFacts != null && operation is ICoalesceOperation factCoalesce)
        {
            yield return factCoalesce;
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         factCoalesce.Value,
                         operationFacts))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factCoalesce.Value))
            {
                yield break;
            }
            if (!factCoalesce.Value.ConstantValue.HasValue ||
                factCoalesce.Value.ConstantValue.Value == null)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             factCoalesce.WhenNull,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operationFacts != null &&
            operation is IConditionalAccessOperation factAccess)
        {
            yield return factAccess;
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         factAccess.Operation,
                         operationFacts))
            {
                yield return descendant;
            }
            if (!operationFacts.MayCompleteNormally(factAccess.Operation) ||
                factAccess.Operation.ConstantValue is
                { HasValue: true, Value: null })
            {
                yield break;
            }
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         factAccess.WhenNotNull,
                         operationFacts))
            {
                yield return descendant;
            }
            yield break;
        }

        yield return operation;

        if (operation is IConditionalOperation conditional &&
            conditional.Condition.ConstantValue is
            { HasValue: true, Value: bool condition })
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         conditional.Condition,
                         operationFacts))
            {
                yield return descendant;
            }
            var branch = condition
                ? conditional.WhenTrue
                : conditional.WhenFalse;
            if (branch != null)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             branch,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is IBinaryOperation binary &&
            (binary.OperatorKind is
                BinaryOperatorKind.ConditionalAnd or
                BinaryOperatorKind.ConditionalOr) &&
            binary.LeftOperand.ConstantValue is
            { HasValue: true, Value: bool left })
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         binary.LeftOperand,
                         operationFacts))
            {
                yield return descendant;
            }
            if (left != (binary.OperatorKind ==
                    BinaryOperatorKind.ConditionalOr))
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             binary.RightOperand,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is ICoalesceOperation coalesce &&
            coalesce.Value.ConstantValue.HasValue)
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         coalesce.Value,
                         operationFacts))
            {
                yield return descendant;
            }
            if (coalesce.Value.ConstantValue.Value == null)
            {
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             coalesce.WhenNull,
                             operationFacts))
                {
                    yield return descendant;
                }
            }
            yield break;
        }

        if (operation is IConditionalAccessOperation access &&
            access.Operation.ConstantValue is
            { HasValue: true, Value: null })
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         access.Operation,
                         operationFacts))
            {
                yield return descendant;
            }
            yield break;
        }

        if (operation is ISwitchExpressionOperation switchExpression &&
            switchExpression.Value.ConstantValue.HasValue)
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         switchExpression.Value,
                         operationFacts))
            {
                yield return descendant;
            }
            if (operationFacts != null &&
                !operationFacts.MayCompleteNormally(switchExpression.Value))
            {
                yield break;
            }
            var input = switchExpression.Value.ConstantValue.Value;
            var switchCompilation = switchExpression.SemanticModel?.Compilation;
            foreach (var arm in switchExpression.Arms)
            {
                var match = switchCompilation == null
                    ? ConstantPatternMatch.Unknown
                    : GetConstantPatternMatch(
                        switchCompilation,
                        arm.Pattern,
                        input,
                        switchExpression.Value.Type);
                if (match == ConstantPatternMatch.No)
                {
                    continue;
                }
                if (arm.Guard != null)
                {
                    foreach (var descendant in
                             ExecutableUnflowedDescendantsAndSelfCore(
                                 arm.Guard,
                                 operationFacts))
                    {
                        yield return descendant;
                    }
                    if (operationFacts != null &&
                        !operationFacts.MayCompleteNormally(arm.Guard))
                    {
                        if (match == ConstantPatternMatch.Yes)
                        {
                            yield break;
                        }
                        continue;
                    }
                    if (arm.Guard.ConstantValue is
                        { HasValue: true, Value: false })
                    {
                        continue;
                    }
                }
                foreach (var descendant in
                         ExecutableUnflowedDescendantsAndSelfCore(
                             arm.Value,
                             operationFacts))
                {
                    yield return descendant;
                }
                var guardIsTrue = arm.Guard == null ||
                    arm.Guard.ConstantValue is { HasValue: true, Value: true };
                if (match == ConstantPatternMatch.Yes && guardIsTrue)
                {
                    break;
                }
            }
            yield break;
        }

        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in
                     ExecutableUnflowedDescendantsAndSelfCore(
                         child,
                         operationFacts))
            {
                yield return descendant;
            }
            if (operationFacts != null &&
                !operationFacts.MayCompleteNormally(child))
            {
                yield break;
            }
        }
    }

    private static ConstantPatternMatch GetConstantPatternMatch(
        Compilation compilation,
        IPatternOperation pattern,
        object? input,
        ITypeSymbol? inputType)
    {
        return pattern switch
        {
            IDiscardPatternOperation => ConstantPatternMatch.Yes,
            ITypePatternOperation typePattern =>
                MatchTypePattern(
                    compilation,
                    typePattern.MatchedType,
                    input,
                    inputType,
                    matchesNull: false),
            IDeclarationPatternOperation
            { MatchedType: { } declarationMatchedType } declarationPattern =>
                MatchTypePattern(
                    compilation,
                    declarationMatchedType,
                    input,
                    inputType,
                    declarationPattern.MatchesNull),
            IDeclarationPatternOperation => ConstantPatternMatch.Unknown,
            IConstantPatternOperation
            {
                Value.ConstantValue: { HasValue: true } constant
            } => Equals(constant.Value, input)
                ? ConstantPatternMatch.Yes
                : ConstantPatternMatch.No,
            IRelationalPatternOperation relational =>
                MatchRelationalPattern(relational, input),
            INegatedPatternOperation negated =>
                Negate(GetConstantPatternMatch(
                    compilation,
                    negated.Pattern,
                    input,
                    inputType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And =>
                And(
                    GetConstantPatternMatch(
                        compilation,
                        binary.LeftPattern,
                        input,
                        inputType),
                    GetConstantPatternMatch(
                        compilation,
                        binary.RightPattern,
                        input,
                        inputType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or =>
                Or(
                    GetConstantPatternMatch(
                        compilation,
                        binary.LeftPattern,
                        input,
                        inputType),
                    GetConstantPatternMatch(
                        compilation,
                        binary.RightPattern,
                        input,
                        inputType)),
            _ => ConstantPatternMatch.Unknown
        };
    }

    private static ConstantPatternMatch MatchTypePattern(
        Compilation compilation,
        ITypeSymbol matchedType,
        object? input,
        ITypeSymbol? inputType,
        bool matchesNull)
    {
        if (input == null)
        {
            return matchesNull
                ? ConstantPatternMatch.Yes
                : ConstantPatternMatch.No;
        }
        var actualType = inputType?.TypeKind == TypeKind.Enum
            ? inputType
            : input switch
            {
                bool => compilation.GetSpecialType(
                    SpecialType.System_Boolean),
                byte => compilation.GetSpecialType(
                    SpecialType.System_Byte),
                sbyte => compilation.GetSpecialType(
                    SpecialType.System_SByte),
                short => compilation.GetSpecialType(
                    SpecialType.System_Int16),
                ushort => compilation.GetSpecialType(
                    SpecialType.System_UInt16),
                int => compilation.GetSpecialType(
                    SpecialType.System_Int32),
                uint => compilation.GetSpecialType(
                    SpecialType.System_UInt32),
                long => compilation.GetSpecialType(
                    SpecialType.System_Int64),
                ulong => compilation.GetSpecialType(
                    SpecialType.System_UInt64),
                char => compilation.GetSpecialType(
                    SpecialType.System_Char),
                float => compilation.GetSpecialType(
                    SpecialType.System_Single),
                double => compilation.GetSpecialType(
                    SpecialType.System_Double),
                decimal => compilation.GetSpecialType(
                    SpecialType.System_Decimal),
                string => compilation.GetSpecialType(
                    SpecialType.System_String),
                _ => null
            };
        if (actualType == null || actualType.TypeKind == TypeKind.Error)
        {
            return ConstantPatternMatch.Unknown;
        }
        return compilation
            .ClassifyCommonConversion(actualType, matchedType)
            .IsImplicit
            ? ConstantPatternMatch.Yes
            : ConstantPatternMatch.No;
    }

    private static ConstantPatternMatch MatchRelationalPattern(
        IRelationalPatternOperation pattern,
        object? input)
    {
        var constantValue = pattern.Value.ConstantValue;
        if (input is not IComparable comparable ||
            !constantValue.HasValue ||
            constantValue.Value == null)
        {
            return ConstantPatternMatch.No;
        }
        var constant = constantValue.Value;
        if (input is double inputDouble && double.IsNaN(inputDouble) ||
            constant is double constantDouble && double.IsNaN(constantDouble) ||
            input is float inputFloat && float.IsNaN(inputFloat) ||
            constant is float constantFloat && float.IsNaN(constantFloat))
        {
            return ConstantPatternMatch.No;
        }

        int comparison;
        try
        {
            comparison = comparable.CompareTo(constant);
        }
        catch (ArgumentException)
        {
            return ConstantPatternMatch.Unknown;
        }

        var matches = pattern.OperatorKind switch
        {
            BinaryOperatorKind.LessThan => comparison < 0,
            BinaryOperatorKind.LessThanOrEqual => comparison <= 0,
            BinaryOperatorKind.GreaterThan => comparison > 0,
            BinaryOperatorKind.GreaterThanOrEqual => comparison >= 0,
            _ => false
        };
        return matches
            ? ConstantPatternMatch.Yes
            : ConstantPatternMatch.No;
    }

    private static ConstantPatternMatch Negate(ConstantPatternMatch value)
    {
        return value switch
        {
            ConstantPatternMatch.Yes => ConstantPatternMatch.No,
            ConstantPatternMatch.No => ConstantPatternMatch.Yes,
            _ => ConstantPatternMatch.Unknown
        };
    }

    private static ConstantPatternMatch And(
        ConstantPatternMatch left,
        ConstantPatternMatch right)
    {
        if (left == ConstantPatternMatch.No ||
            right == ConstantPatternMatch.No)
        {
            return ConstantPatternMatch.No;
        }
        return left == ConstantPatternMatch.Yes &&
               right == ConstantPatternMatch.Yes
            ? ConstantPatternMatch.Yes
            : ConstantPatternMatch.Unknown;
    }

    private static ConstantPatternMatch Or(
        ConstantPatternMatch left,
        ConstantPatternMatch right)
    {
        if (left == ConstantPatternMatch.Yes ||
            right == ConstantPatternMatch.Yes)
        {
            return ConstantPatternMatch.Yes;
        }
        return left == ConstantPatternMatch.No &&
               right == ConstantPatternMatch.No
            ? ConstantPatternMatch.No
            : ConstantPatternMatch.Unknown;
    }

    private enum ConstantPatternMatch
    {
        No,
        Yes,
        Unknown
    }

    private static ImmutableArray<RequiresCallTarget> GetPropertyCalls(
        IPropertyReferenceOperation property,
        SemanticModel? semanticModel = null,
        CancellationToken cancellationToken = default)
    {
        var getter = property.Property.GetMethod;
        var setter = property.Property.SetMethod;
        if (property.Parent is ISimpleAssignmentOperation assignment &&
            ReferenceEquals(assignment.Target, property))
        {
            return setter == null
                ? []
                : [CreateSetterCall(
                    property,
                    setter,
                    assignment.Value,
                    true,
                    semanticModel,
                    cancellationToken)];
        }
        if (property.Parent is ICoalesceAssignmentOperation coalesce &&
            ReferenceEquals(coalesce.Target, property))
        {
            var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>(2);
            if (getter != null)
            {
                calls.Add(CreateGetterCall(
                    property, getter, semanticModel, cancellationToken));
            }
            if (setter != null)
            {
                calls.Add(CreateSetterCall(
                    property,
                    setter,
                    coalesce.Value,
                    canReplay: false,
                    semanticModel,
                    cancellationToken));
            }
            return calls.ToImmutable();
        }
        if (property.Parent is ICompoundAssignmentOperation compound &&
            ReferenceEquals(compound.Target, property) ||
            property.Parent is IIncrementOrDecrementOperation increment &&
            ReferenceEquals(increment.Target, property))
        {
            var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>(2);
            if (getter != null)
            {
                calls.Add(CreateGetterCall(
                    property, getter, semanticModel, cancellationToken));
            }
            if (setter != null)
            {
                calls.Add(CreateSetterCall(
                    property,
                    setter,
                    null,
                    false,
                    semanticModel,
                    cancellationToken));
            }
            return calls.ToImmutable();
        }
        if (property.Parent is INameOfOperation || getter == null)
        {
            return [];
        }
        return [CreateGetterCall(
            property, getter, semanticModel, cancellationToken)];
    }

    private static RequiresCallTarget CreateGetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol getter,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        return new RequiresCallTarget(
            getter,
            GetAccessorInstance(property, semanticModel, cancellationToken),
            property.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true);
    }

    private static RequiresCallTarget CreateSetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol setter,
        IOperation? value,
        bool canReplay,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        var explicitArguments = value == null
            ? ImmutableDictionary<int, IOperation>.Empty
            : ImmutableDictionary<int, IOperation>.Empty.Add(
                setter.Parameters.Length - 1,
                value);
        return new RequiresCallTarget(
            setter,
            GetAccessorInstance(property, semanticModel, cancellationToken),
            property.Arguments,
            explicitArguments,
            ImmutableDictionary<int, long>.Empty,
            canReplay);
    }

    private static IOperation? GetAccessorInstance(
        IPropertyReferenceOperation property,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (property.Instance is not IConditionalAccessInstanceOperation &&
            property.Instance is not IFlowCaptureReferenceOperation)
        {
            return property.Instance;
        }

        for (var ancestor = property.Parent;
             ancestor != null;
             ancestor = ancestor.Parent)
        {
            if (ancestor is IConditionalAccessOperation conditional)
            {
                return conditional.Operation;
            }
        }

        if (semanticModel != null &&
            property.Syntax.SpanStart >= 0 &&
            property.Syntax.Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .Select(conditional =>
                    (Syntax: conditional,
                        Operation: semanticModel.GetOperation(
                            conditional,
                            cancellationToken) as IConditionalAccessOperation))
                .FirstOrDefault(candidate =>
                    candidate.Operation != null &&
                    candidate.Operation.WhenNotNull.Syntax.Span ==
                        property.Syntax.Span)
                .Operation is { } conditionalAccess)
        {
            return conditionalAccess.Operation;
        }

        return property.Instance;
    }


    private static ImmutableArray<RequiresCallTarget> GetEventCalls(
        IEventReferenceOperation eventReference)
    {
        if (eventReference.Parent is not IEventAssignmentOperation assignment ||
            !ReferenceEquals(assignment.EventReference, eventReference))
        {
            return [];
        }
        var target = assignment.Adds
            ? eventReference.Event.AddMethod
            : eventReference.Event.RemoveMethod;
        if (target == null)
        {
            return [];
        }
        return [new RequiresCallTarget(
            target,
            eventReference.Instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty.Add(
                0,
                assignment.HandlerValue),
            ImmutableDictionary<int, long>.Empty,
            true)];
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

}
