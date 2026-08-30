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
        var delegateTargets = GetDirectDelegateTargets(operationRoot);
        foreach (var operation in
                 ExecutableDescendantsAndSelf(operationRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = GetCalls(
                operation,
                operationFacts,
                semanticModel,
                delegateTargets,
                cancellationToken: cancellationToken);
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
                origin.Syntax,
                baseConstructor,
                Instance: null,
                Arguments: [],
                ImmutableDictionary<int, IOperation>.Empty,
                CanReplay: true,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded));
        }
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var delegateTargets = GetDirectDelegateTargets(operationRoot!);
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
                    semanticModel,
                    delegateTargets,
                    flowResult,
                    cancellationToken);
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
                    operation is not IListPatternOperation)
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
                    var candidate = new RequiresCallSiteCandidate(
                        operation,
                        operation.Syntax,
                        call.TargetMethod,
                        call.Instance,
                        call.Arguments,
                        call.ExplicitArguments,
                        call.CanReplay && HasReplayableCallEvaluation(
                            operation,
                            call,
                            operationFacts,
                            hasFlowState,
                            flowAnalysis.IsComplete),
                        hasFlowState ? flowResult : null,
                        flowAnalysis.Status);
                    var existingIndex = callSites.FindIndex(existing =>
                        existing.Syntax.SyntaxTree ==
                            operation.Syntax.SyntaxTree &&
                        existing.Syntax.Span ==
                            operation.Syntax.Span &&
                        SymbolEqualityComparer.Default.Equals(
                            existing.TargetMethod,
                            candidate.TargetMethod));
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
                        existing.Syntax.SyntaxTree ==
                            property.Syntax.SyntaxTree &&
                        existing.Syntax.Span == property.Syntax.Span &&
                        SymbolEqualityComparer.Default.Equals(
                            existing.TargetMethod,
                            call.TargetMethod)))
                {
                    continue;
                }

                callSites.Add(new RequiresCallSiteCandidate(
                    property,
                    property.Syntax,
                    call.TargetMethod,
                    call.Instance,
                    call.Arguments,
                    call.ExplicitArguments,
                    CanReplay: false,
                    Flow: null,
                    ManagedFlowStatus.BudgetExceeded));
            }
        }

        foreach (var operation in ExecutableDescendantsAndSelf(
                     operationRoot!).Where(static candidate =>
                         candidate is IForEachLoopOperation or
                             IUsingOperation or
                             IUsingDeclarationOperation or
                             IRecursivePatternOperation))
        {
            if (!operation.DescendantsAndSelf().Any(candidate =>
                    reachableOperationSites.Contains((
                        candidate.Syntax.SyntaxTree,
                        candidate.Syntax.SpanStart,
                        candidate.Syntax.Span.Length))))
            {
                continue;
            }

            foreach (var call in GetCalls(
                         operation,
                         operationFacts,
                         semanticModel,
                         delegateTargets,
                         flowResult,
                         cancellationToken))
            {
                if (callSites.Any(existing =>
                        existing.Syntax.SyntaxTree ==
                            operation.Syntax.SyntaxTree &&
                        (existing.Syntax.Span == operation.Syntax.Span ||
                         existing.Operation?.IsImplicit == true &&
                         operation.Syntax.Span.Contains(
                             existing.Syntax.Span)) &&
                        SymbolEqualityComparer.Default.Equals(
                            existing.TargetMethod,
                            call.TargetMethod)))
                {
                    continue;
                }

                callSites.Add(new RequiresCallSiteCandidate(
                    operation,
                    operation.Syntax,
                    call.TargetMethod,
                    call.Instance,
                    call.Arguments,
                    call.ExplicitArguments,
                    call.CanReplay && HasReplayableCallEvaluation(
                        operation,
                        call,
                        operationFacts,
                        hasFlowState: false,
                        flowAnalysisIsComplete:
                            flowAnalysis.IsComplete),
                    Flow: null,
                    flowAnalysis.Status));
            }
        }

        return [
            .. callSites.OrderBy(
                static candidate => candidate.Syntax.SpanStart)
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

    private static bool IsRecordCopyConstructor(
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
        DefiniteOperationFacts operationFacts)
    {
        return (call.Instance == null ||
                operationFacts.CompletesNormally(call.Instance)) &&
            call.Arguments.All(argument =>
                operationFacts.CompletesNormally(argument.Value)) &&
            call.ExplicitArguments.Values.All(
                operationFacts.CompletesNormally);
    }

    private bool HasReplayableCallEvaluation(
        IOperation operation,
        RequiresCallTarget call,
        DefiniteOperationFacts operationFacts,
        bool hasFlowState,
        bool flowAnalysisIsComplete)
    {
        if (IsAccessorCall(call.TargetMethod) ||
            operation is IListPatternOperation)
        {
            return HasReplayableAccessorEvaluation(call, operationFacts);
        }
        if (operation is IForEachLoopOperation)
        {
            return HasReplayableAccessorEvaluation(call, operationFacts);
        }
        if (operation is IUsingOperation or IUsingDeclarationOperation)
        {
            return operationFacts.MayCompleteNormally(
                operation is IUsingOperation usingOperation
                    ? usingOperation.Resources
                    : ((IUsingDeclarationOperation)operation)
                        .DeclarationGroup);
        }
        if (operation is IRecursivePatternOperation)
        {
            return HasReplayableAccessorEvaluation(call, operationFacts);
        }
        if (operation is IInvocationOperation invocation &&
            invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
            !SymbolEqualityComparer.Default.Equals(
                call.TargetMethod,
                invocation.TargetMethod))
        {
            return HasReplayableAccessorEvaluation(call, operationFacts);
        }
        if (operation.IsImplicit)
        {
            return HasReplayableAccessorEvaluation(call, operationFacts);
        }
        return (hasFlowState || !flowAnalysisIsComplete) &&
            HasReplayablePrefix(operation, operationFacts);
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
        SemanticModel? semanticModel = null,
        IReadOnlyDictionary<ILocalSymbol,
            (IMethodSymbol Method, IOperation? Instance)>?
            delegateTargets = null,
        ManagedFlowResult? flowResult = null,
        CancellationToken cancellationToken = default)
    {
        return operation switch
        {
            IInvocationOperation invocation => GetInvocationCalls(
                invocation,
                delegateTargets),
            IObjectCreationOperation
            {
                Constructor: { } constructor
            } creation => [new(
                constructor,
                null,
                creation.Arguments,
                ImmutableDictionary<int, IOperation>.Empty,
                true)],
            IPropertyReferenceOperation property =>
                GetPropertyCalls(property),
            IEventReferenceOperation eventReference =>
                GetEventCalls(eventReference),
            ICompoundAssignmentOperation
            {
                OperatorMethod: { } method
            } compound => [CreateImplicitOperatorCall(
                method,
                compound.Target,
                compound.Value)],
            IIncrementOrDecrementOperation
            {
                OperatorMethod: { } method
            } increment => [CreateImplicitOperatorCall(
                method,
                increment.Target)],
            IBinaryOperation
            {
                OperatorMethod: { } method
            } binary => [CreateImplicitOperatorCall(
                method,
                binary.LeftOperand,
                binary.RightOperand)],
            IUnaryOperation
            {
                OperatorMethod: { } method
            } unary => [CreateImplicitOperatorCall(
                method,
                unary.Operand)],
            IConversionOperation
            {
                OperatorMethod: { } method
            } conversion => [CreateImplicitOperatorCall(
                method,
                conversion.Operand)],
            IForEachLoopOperation forEach => GetForEachCalls(
                forEach,
                operationFacts,
                semanticModel,
                cancellationToken),
            IUsingOperation usingOperation => GetUsingCalls(
                usingOperation.Resources,
                usingOperation.IsAsynchronous,
                semanticModel?.Compilation,
                operationFacts,
                flowResult),
            IUsingDeclarationOperation usingDeclaration => GetUsingCalls(
                usingDeclaration.DeclarationGroup,
                usingDeclaration.IsAsynchronous,
                semanticModel?.Compilation,
                operationFacts,
                flowResult),
            IRecursivePatternOperation
            {
                DeconstructSymbol: IMethodSymbol deconstruct
            } recursivePattern => GetRecursivePatternCalls(
                recursivePattern,
                deconstruct,
                flowResult),
            IListPatternOperation listPattern => GetListPatternCalls(
                listPattern,
                operationFacts,
                semanticModel?.Compilation,
                cancellationToken),
            _ => []
        };
    }

    private static ImmutableArray<RequiresCallTarget> GetInvocationCalls(
        IInvocationOperation invocation,
        IReadOnlyDictionary<ILocalSymbol,
            (IMethodSymbol Method, IOperation? Instance)>?
            delegateTargets)
    {
        var ordinary = new RequiresCallTarget(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            true);
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            !TryResolveDirectDelegateTarget(
                invocation.Instance,
                delegateTargets,
                out var target))
        {
            return [ordinary];
        }

        return [ordinary, new RequiresCallTarget(
            target.Method,
            target.Instance,
            invocation.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            true)];
    }

    private static RequiresCallTarget CreateImplicitOperatorCall(
        IMethodSymbol method,
        params IOperation[] operands)
    {
        var arguments = ImmutableDictionary.CreateBuilder<int, IOperation>();
        var count = Math.Min(method.Parameters.Length, operands.Length);
        for (var index = 0; index < count; index++)
        {
            arguments.Add(index, operands[index]);
        }
        return new RequiresCallTarget(
            method,
            Instance: null,
            Arguments: [],
            arguments.ToImmutable(),
            CanReplay: true);
    }

    private static Dictionary<ILocalSymbol,
        (IMethodSymbol Method, IOperation? Instance)>
        GetDirectDelegateTargets(IOperation operationRoot)
    {
        var targets = new Dictionary<ILocalSymbol,
            (IMethodSymbol Method, IOperation? Instance)>(
            SymbolEqualityComparer.Default);
        var ambiguous = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var declarator in operationRoot.DescendantsAndSelf()
                     .OfType<IVariableDeclaratorOperation>())
        {
            if (declarator.Initializer?.Value is not { } value ||
                !TryGetMethodReference(value, out var reference) ||
                ambiguous.Contains(declarator.Symbol))
            {
                continue;
            }

            if (targets.ContainsKey(declarator.Symbol))
            {
                targets.Remove(declarator.Symbol);
                ambiguous.Add(declarator.Symbol);
            }
            else
            {
                targets.Add(
                    declarator.Symbol,
                    (reference.Method, reference.Instance));
            }
        }

        foreach (var operation in operationRoot.DescendantsAndSelf())
        {
            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation
                {
                    Parameter.RefKind: not RefKind.None
                } argument => argument.Value,
                _ => null
            };
            if (TryGetLocalReference(target, out var local))
            {
                targets.Remove(local);
                ambiguous.Add(local);
            }
        }
        return targets;
    }

    private static bool TryResolveDirectDelegateTarget(
        IOperation? instance,
        IReadOnlyDictionary<ILocalSymbol,
            (IMethodSymbol Method, IOperation? Instance)>? targets,
        out (IMethodSymbol Method, IOperation? Instance) target)
    {
        if (instance != null &&
            TryGetMethodReference(instance, out var reference))
        {
            target = (reference.Method, reference.Instance);
            return true;
        }
        if (targets != null &&
            TryGetLocalReference(instance, out var local) &&
            targets.TryGetValue(local, out target))
        {
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryGetMethodReference(
        IOperation operation,
        out IMethodReferenceOperation reference)
    {
        while (true)
        {
            switch (operation)
            {
                case IMethodReferenceOperation methodReference:
                    reference = methodReference;
                    return true;
                case IDelegateCreationOperation delegateCreation:
                    operation = delegateCreation.Target;
                    continue;
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                default:
                    reference = null!;
                    return false;
            }
        }
    }

    private static bool TryGetLocalReference(
        IOperation? operation,
        out ILocalSymbol local)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        if (operation is ILocalReferenceOperation reference)
        {
            local = reference.Local;
            return true;
        }

        local = null!;
        return false;
    }

    private static ImmutableArray<RequiresCallTarget> GetForEachCalls(
        IForEachLoopOperation loop,
        DefiniteOperationFacts? operationFacts,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel == null ||
            loop.Syntax is not CommonForEachStatementSyntax syntax)
        {
            return [];
        }

        var info = semanticModel.GetForEachStatementInfo(syntax);
        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        if (info.GetEnumeratorMethod == null)
        {
            return [];
        }

        Add(info.GetEnumeratorMethod, loop.Collection);
        if (operationFacts != null &&
            !operationFacts.MethodCanCompleteNormally(
                info.GetEnumeratorMethod))
        {
            return calls.ToImmutable();
        }

        if (info.MoveNextMethod != null)
        {
            Add(info.MoveNextMethod, instance: null);
        }
        if (info.CurrentProperty?.GetMethod is { } current &&
            (info.MoveNextMethod == null ||
             operationFacts == null ||
             operationFacts.MethodCanCompleteNormally(
                 info.MoveNextMethod)))
        {
            Add(current, instance: null);
        }
        Add(
            ResolveDisposeMethod(
                info.GetEnumeratorMethod.ReturnType,
                isAsynchronous: false,
                semanticModel.Compilation) ?? info.DisposeMethod,
            instance: null);
        return calls.ToImmutable();

        void Add(IMethodSymbol? method, IOperation? instance)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (method == null)
            {
                return;
            }
            calls.Add(new RequiresCallTarget(
                method,
                instance,
                [],
                ImmutableDictionary<int, IOperation>.Empty,
                true));
        }
    }

    private static ImmutableArray<RequiresCallTarget> GetUsingCalls(
        IOperation resources,
        bool isAsynchronous,
        Compilation? compilation,
        DefiniteOperationFacts? operationFacts,
        ManagedFlowResult? flowResult)
    {
        if (compilation == null)
        {
            return [];
        }

        var acquired = new List<(
            ITypeSymbol Type,
            IOperation Resource,
            IOperation Origin)>();
        if (resources is IVariableDeclarationGroupOperation group)
        {
            foreach (var declarator in group.Declarations.SelectMany(
                         static declaration => declaration.Declarators))
            {
                var resource = declarator.Initializer?.Value;
                if (resource == null ||
                    operationFacts != null &&
                    !operationFacts.MayCompleteNormally(resource))
                {
                    break;
                }
                acquired.Add((
                    declarator.Symbol.Type,
                    resource,
                    declarator));
            }
        }
        else if ((operationFacts == null ||
                  operationFacts.MayCompleteNormally(resources)) &&
                 resources.Type is { } resourceType)
        {
            acquired.Add((resourceType, resources, resources));
        }

        var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>();
        foreach (var item in acquired.AsEnumerable().Reverse())
        {
            if (DefiniteOperationFacts.IsDefinitelyNull(item.Resource) ||
                flowResult?.ProvesNull(
                    item.Origin,
                    item.Resource) == true)
            {
                continue;
            }
            var method = ResolveDisposeMethod(
                item.Type,
                isAsynchronous,
                compilation);
            if (method != null)
            {
                calls.Add(new RequiresCallTarget(
                    method,
                    item.Resource,
                    Arguments: [],
                    ImmutableDictionary<int, IOperation>.Empty,
                    CanReplay: true));
            }
        }
        return calls.ToImmutable();
    }

    private static IMethodSymbol? ResolveDisposeMethod(
        ITypeSymbol resourceType,
        bool isAsynchronous,
        Compilation compilation)
    {
        var interfaceName = isAsynchronous
            ? "System.IAsyncDisposable"
            : "System.IDisposable";
        var methodName = isAsynchronous
            ? "DisposeAsync"
            : "Dispose";
        var disposable = compilation.GetTypeByMetadataName(interfaceName);
        var interfaceMethod = disposable?.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(static method => method.Parameters.IsEmpty);
        if (interfaceMethod != null &&
            resourceType is INamedTypeSymbol named &&
            named.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    disposable!.OriginalDefinition)))
        {
            return named.FindImplementationForInterfaceMember(
                    interfaceMethod) as IMethodSymbol ??
                interfaceMethod;
        }

        return resourceType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method =>
                !method.IsStatic && method.Parameters.IsEmpty);
    }

    private static ImmutableArray<RequiresCallTarget>
        GetRecursivePatternCalls(
            IRecursivePatternOperation pattern,
            IMethodSymbol deconstruct,
            ManagedFlowResult? flowResult)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (instance == null ||
            DefiniteOperationFacts.IsDefinitelyNull(instance) ||
            flowResult?.ProvesNull(pattern, instance) == true)
        {
            return [];
        }

        return [new RequiresCallTarget(
            deconstruct,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            true)];
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
        if (length != null)
        {
            calls.Add(CreateImplicitListPatternCall(length, instance));
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
        if (compilation != null &&
            TryGetKnownListLength(
                pattern,
                instance,
                compilation,
                cancellationToken,
                out var knownLength) &&
            (hasSlice
                ? knownLength < requiredLength
                : knownLength != requiredLength))
        {
            return calls.ToImmutable();
        }

        foreach (var item in pattern.Patterns)
        {
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
            calls.Add(CreateImplicitListPatternCall(member, instance));
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
        IOperation? instance)
    {
        return new RequiresCallTarget(
            method,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
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
        CreateUnflowedCandidates(
            IOperation operation,
            SemanticModel? semanticModel = null)
    {
        return [.. GetCalls(
            operation,
            semanticModel: semanticModel).Select(call =>
            new RequiresCallSiteCandidate(
                operation,
                operation.Syntax,
                call.TargetMethod,
                call.Instance,
                call.Arguments,
                call.ExplicitArguments,
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
        IPropertyReferenceOperation property)
    {
        var getter = property.Property.GetMethod;
        var setter = property.Property.SetMethod;
        if (property.Parent is ISimpleAssignmentOperation assignment &&
            ReferenceEquals(assignment.Target, property))
        {
            return setter == null
                ? []
                : [CreateSetterCall(property, setter, assignment.Value, true)];
        }
        if (property.Parent is ICoalesceAssignmentOperation coalesce &&
            ReferenceEquals(coalesce.Target, property))
        {
            var calls = ImmutableArray.CreateBuilder<RequiresCallTarget>(2);
            if (getter != null)
            {
                calls.Add(CreateGetterCall(property, getter));
            }
            if (setter != null)
            {
                calls.Add(CreateSetterCall(
                    property,
                    setter,
                    coalesce.Value,
                    canReplay: false));
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
                calls.Add(CreateGetterCall(property, getter));
            }
            if (setter != null)
            {
                calls.Add(CreateSetterCall(property, setter, null, false));
            }
            return calls.ToImmutable();
        }
        if (property.Parent is INameOfOperation || getter == null)
        {
            return [];
        }
        return [CreateGetterCall(property, getter)];
    }

    private static RequiresCallTarget CreateGetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol getter)
    {
        return new RequiresCallTarget(
            getter,
            property.Instance,
            property.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            true);
    }

    private static RequiresCallTarget CreateSetterCall(
        IPropertyReferenceOperation property,
        IMethodSymbol setter,
        IOperation? value,
        bool canReplay)
    {
        var explicitArguments = value == null
            ? ImmutableDictionary<int, IOperation>.Empty
            : ImmutableDictionary<int, IOperation>.Empty.Add(
                setter.Parameters.Length - 1,
                value);
        return new RequiresCallTarget(
            setter,
            property.Instance,
            property.Arguments,
            explicitArguments,
            canReplay);
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
