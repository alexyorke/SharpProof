using SharpProof.Roslyn;

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
                var target = RequiresCallSiteDispatch.ResolveExactTarget(
                    call.TargetMethod,
                    call.Instance,
                    cancellationToken);
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

        if (TryGetImplicitBaseConstructor(out var baseConstructor) &&
            hasPotentialPreconditions(baseConstructor))
        {
            owners.Add(
                ContractClauseInventoryBuilder
                    .NormalizeCallable(caller));
        }

        return owners.ToImmutable();
    }

    internal ImmutableArray<RequiresCallSiteCandidate>? Get(
        BoundMethodContracts? callerContracts,
        bool requireCallerOwnership = true)
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
        if (TryGetImplicitBaseConstructor(out var baseConstructor))
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
                ImmutableDictionary<int, long>.Empty,
                CanReplay: true,
                Flow: null,
                ManagedFlowStatus.BudgetExceeded));
        }
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var reachableInitializerSites = GetReachableInitializerSites(
            operationFacts);
        var delegateTargets = GetDirectDelegateTargets(operationRoot!);
        OperationEffectScanner? semanticReachability = null;
        foreach (var block in RoslynCfgThrowFacts.ReachableBlocks(
                     graph,
                     cancellationToken))
        {
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
                    reachableInitializerSites != null &&
                    !reachableInitializerSites.Contains((
                        operation.Syntax.SyntaxTree,
                        operation.Syntax.SpanStart,
                        operation.Syntax.Span.Length)) ||
                    requireCallerOwnership &&
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
                var hasReachableFlowState =
                    flowResult?.IsReachable(operation) == true &&
                    (hasFlowState || operation is IListPatternOperation);
                var isInsideExceptionHandler =
                    IsInsideExceptionHandler(operation);
                if (flowAnalysis.IsComplete &&
                    !hasReachableFlowState &&
                    (!isInsideExceptionHandler ||
                     !(semanticReachability ??=
                         OperationEffectScanner.CreateReachabilityProbe(
                             semanticModel.Compilation,
                             caller,
                             operationRoot!,
                             flowResult)).IsReachable(operation)))
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
                    var candidate = CreateCandidate(
                        operation,
                        call,
                        call.CanReplay && HasReplayableCallEvaluation(
                            operation,
                            call,
                            operationFacts,
                            hasFlowState,
                            flowAnalysis.IsComplete),
                        hasFlowState ? flowResult : null,
                        flowAnalysis.Status,
                        cancellationToken);
                    AddOrUpgrade(
                        callSites,
                        candidate,
                        skipDeduplication: operation is IListPatternOperation);
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
                AddOrUpgrade(callSites, CreateCandidate(
                    property,
                    call,
                    canReplay: false,
                    flow: null,
                    ManagedFlowStatus.BudgetExceeded,
                    cancellationToken));
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
                var candidate = CreateCandidate(
                    operation,
                    call,
                    call.CanReplay && HasReplayableCallEvaluation(
                        operation,
                        call,
                        operationFacts,
                        hasFlowState: false,
                        flowAnalysisIsComplete:
                            flowAnalysis.IsComplete),
                        flow: null,
                        flowAnalysis.Status,
                        cancellationToken);
                AddOrUpgrade(
                    callSites,
                    candidate,
                    allowImplicitContainment: true);
            }
        }

        return [
            .. callSites.OrderBy(
                static candidate => candidate.Syntax.SpanStart)
        ];
    }

    private static RequiresCallSiteCandidate CreateCandidate(
        IOperation operation,
        RequiresCallTarget call,
        bool canReplay,
        ManagedFlowResult? flow,
        ManagedFlowStatus flowStatus,
        CancellationToken cancellationToken)
    {
        var resolvedTarget = RequiresCallSiteDispatch.ResolveExactTarget(
            call.TargetMethod,
            call.Instance,
            cancellationToken);
        return new RequiresCallSiteCandidate(
            operation,
            operation.Syntax,
            call.TargetMethod,
            call.Instance,
            call.Arguments,
            call.ExplicitArguments,
            call.ImplicitIntegerArguments,
            canReplay,
            flow,
            flowStatus)
        {
            ResolvedTargetMethod = resolvedTarget
        };
    }

    private static void AddOrUpgrade(
        List<RequiresCallSiteCandidate> callSites,
        RequiresCallSiteCandidate candidate,
        bool allowImplicitContainment = false,
        bool skipDeduplication = false)
    {
        var existingIndex = skipDeduplication
            ? -1
            : callSites.FindIndex(existing =>
                existing.Syntax.SyntaxTree == candidate.Syntax.SyntaxTree &&
                (existing.Syntax.Span == candidate.Syntax.Span ||
                 allowImplicitContainment &&
                 existing.Operation?.IsImplicit == true &&
                 candidate.Syntax.Span.Contains(existing.Syntax.Span)) &&
                SymbolEqualityComparer.Default.Equals(
                    existing.TargetMethod,
                    candidate.TargetMethod));
        if (existingIndex < 0)
        {
            callSites.Add(candidate);
        }
        else if (!callSites[existingIndex].CanReplay && candidate.CanReplay)
        {
            callSites[existingIndex] = candidate;
        }
    }

    private HashSet<(SyntaxTree Tree, int Start, int Length)>?
        GetReachableInitializerSites(
            DefiniteOperationFacts operationFacts)
    {
        if (declaration is not EqualsValueClauseSyntax initializer)
        {
            return null;
        }

        var operation = semanticModel.GetOperation(
            initializer.Value,
            cancellationToken);
        return operation == null
            ? []
            : new HashSet<(SyntaxTree Tree, int Start, int Length)>(
                ExecutableUnflowedDescendantsAndSelf(
                        operation,
                        operationFacts)
                    .Select(static candidate => (
                        Tree: candidate.Syntax.SyntaxTree,
                        Start: candidate.Syntax.SpanStart,
                        Length: candidate.Syntax.Span.Length)));
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
                IMethodBodyOperation or IConstructorBodyOperation =>
                    RoslynCfgFactory.TryCreateMethodOrConstructorGraph(
                        operationRoot, cancellationToken),
                IFieldInitializerOperation field =>
                    ControlFlowGraph.Create(field, cancellationToken),
                IPropertyInitializerOperation property =>
                    ControlFlowGraph.Create(property, cancellationToken),
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

    private bool TryGetImplicitBaseConstructor(
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

        var candidate = RequiresCallSiteAnalyzer.TryGetImplicitBaseConstructor(caller);
        if (candidate == null)
        {
            return false;
        }

        baseConstructor = candidate;
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
        if (declaration is EqualsValueClauseSyntax)
        {
            return true;
        }

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
        if (operation is IUsingOperation or IUsingDeclarationOperation)
        {
            return operationFacts.MayCompleteNormally(
                operation is IUsingOperation usingOperation
                    ? usingOperation.Resources
                    : ((IUsingDeclarationOperation)operation)
                        .DeclarationGroup);
        }
        if (IsAccessorCall(call.TargetMethod) ||
            operation is IListPatternOperation or IForEachLoopOperation or
                IRecursivePatternOperation ||
            operation is IInvocationOperation invocation &&
                invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                !SymbolEqualityComparer.Default.Equals(
                    call.TargetMethod,
                    invocation.TargetMethod) ||
            operation.IsImplicit)
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
            DirectDelegateTarget>?
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
                ImmutableDictionary<int, long>.Empty,
                true)],
            IPropertyReferenceOperation property =>
                GetPropertyCalls(property),
            IEventReferenceOperation eventReference =>
                GetEventCalls(eventReference),
            ICompoundAssignmentOperation
            {
                OperatorMethod: { } method
            } compound => CreateImplicitOperatorCalls(
                method,
                compound,
                compound.IsLifted,
                flowResult,
                compound.Target,
                compound.Value),
            IIncrementOrDecrementOperation
            {
                OperatorMethod: { } method
            } increment => CreateImplicitOperatorCalls(
                method,
                increment,
                increment.IsLifted,
                flowResult,
                increment.Target),
            IBinaryOperation
            {
                OperatorMethod: { } method
            } binary => CreateImplicitOperatorCalls(
                method,
                binary,
                binary.IsLifted,
                flowResult,
                binary.LeftOperand,
                binary.RightOperand),
            IUnaryOperation
            {
                OperatorMethod: { } method
            } unary => CreateImplicitOperatorCalls(
                method,
                unary,
                unary.IsLifted,
                flowResult,
                unary.Operand),
            IConversionOperation
            {
                OperatorMethod: { } method
            } conversion => CreateImplicitOperatorCalls(
                method,
                conversion,
                IsLiftedUserDefinedConversion(conversion, method),
                flowResult,
                conversion.Operand),
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
            DirectDelegateTarget>?
            delegateTargets)
    {
        var ordinary = new RequiresCallTarget(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true);
        if (invocation.TargetMethod.MethodKind != MethodKind.DelegateInvoke ||
            !TryResolveDirectDelegateTarget(
                invocation,
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
            ImmutableDictionary<int, long>.Empty,
            true)];
    }

    private static ImmutableArray<RequiresCallTarget>
        CreateImplicitOperatorCalls(
            IMethodSymbol method,
            IOperation operation,
            bool isLifted,
            ManagedFlowResult? flowResult,
            IOperation firstOperand,
            IOperation? secondOperand = null)
    {
        if (isLifted &&
            (DefiniteOperationFacts.IsDefinitelyNull(firstOperand) ||
             flowResult?.ProvesNull(operation, firstOperand) == true ||
             secondOperand != null &&
             (DefiniteOperationFacts.IsDefinitelyNull(secondOperand) ||
              flowResult?.ProvesNull(operation, secondOperand) == true)))
        {
            return [];
        }

        return [CreateImplicitOperatorCall(method, firstOperand, secondOperand)];
    }

    private static bool IsLiftedUserDefinedConversion(
        IConversionOperation conversion,
        IMethodSymbol method)
    {
        var operandType = CompilerIdentityBridge.GetNullableUnderlyingType(
            conversion.Operand.Type);
        var resultType = CompilerIdentityBridge.GetNullableUnderlyingType(
            conversion.Type);
        return method.Parameters.Length == 1 &&
            operandType != null &&
            resultType != null &&
            SymbolEqualityComparer.Default.Equals(
                operandType,
                method.Parameters[0].Type) &&
            SymbolEqualityComparer.Default.Equals(
                resultType,
                method.ReturnType);
    }

    private static RequiresCallTarget CreateImplicitOperatorCall(
        IMethodSymbol method,
        IOperation firstOperand,
        IOperation? secondOperand = null)
    {
        var arguments = ImmutableDictionary.CreateBuilder<int, IOperation>();
        if (method.Parameters.Length > 0)
        {
            arguments.Add(0, firstOperand);
        }
        if (method.Parameters.Length > 1 && secondOperand != null)
        {
            arguments.Add(1, secondOperand);
        }
        return new RequiresCallTarget(
            method,
            Instance: null,
            Arguments: [],
            arguments.ToImmutable(),
            ImmutableDictionary<int, long>.Empty,
            CanReplay: true);
    }

    private sealed record DirectDelegateTarget(
        IMethodSymbol Method,
        IOperation? Instance,
        ImmutableArray<IOperation> Invalidations,
        bool HasGoto);

    private static Dictionary<ILocalSymbol, DirectDelegateTarget>
        GetDirectDelegateTargets(IOperation operationRoot)
    {
        var declarations = new List<(
            ILocalSymbol Symbol,
            IMethodSymbol Method,
            IOperation? Instance)>();
        var invalidations = new Dictionary<ILocalSymbol, List<IOperation>>(
            SymbolEqualityComparer.Default);
        var hasGoto = false;
        foreach (var operation in operationRoot.DescendantsAndSelf())
        {
            if (operation is IBranchOperation
                {
                    BranchKind: BranchKind.GoTo
                })
            {
                hasGoto = true;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                declarator.Initializer?.Value is { } value &&
                TryGetMethodReference(value, out var reference))
            {
                declarations.Add((
                    declarator.Symbol,
                    reference.Method,
                    reference.Instance));
            }

            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation
                {
                    Parameter.RefKind: not RefKind.None
                } argument => argument.Value,
                IVariableDeclaratorOperation
                {
                    Symbol.RefKind: not RefKind.None,
                    Initializer.Value: { } initializerValue
                } => initializerValue,
                _ => null
            };
            if (TryGetLocalReference(target, out var local))
            {
                if (!invalidations.TryGetValue(local, out var operations))
                {
                    operations = [];
                    invalidations.Add(local, operations);
                }
                operations.Add(operation);
            }
        }

        var targets = new Dictionary<ILocalSymbol, DirectDelegateTarget>(
            SymbolEqualityComparer.Default);
        var ambiguous = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var declaration in declarations)
        {
            if (ambiguous.Contains(declaration.Symbol))
            {
                continue;
            }

            if (targets.ContainsKey(declaration.Symbol))
            {
                targets.Remove(declaration.Symbol);
                ambiguous.Add(declaration.Symbol);
            }
            else
            {
                targets.Add(
                    declaration.Symbol,
                    new DirectDelegateTarget(
                        declaration.Method,
                        declaration.Instance,
                        [],
                        hasGoto));
            }
        }

        foreach (var local in targets.Keys.ToArray())
        {
            if (invalidations.TryGetValue(local, out var operations))
            {
                var known = targets[local];
                targets[local] = known with
                {
                    Invalidations = [.. operations]
                };
            }
        }
        return targets;
    }

    private static bool TryResolveDirectDelegateTarget(
        IInvocationOperation invocation,
        IReadOnlyDictionary<ILocalSymbol,
            DirectDelegateTarget>? targets,
        out (IMethodSymbol Method, IOperation? Instance) target)
    {
        var instance = invocation.Instance;
        if (instance != null &&
            TryGetMethodReference(instance, out var reference))
        {
            target = (reference.Method, reference.Instance);
            return true;
        }
        if (targets != null &&
            TryGetLocalReference(instance, out var local) &&
            targets.TryGetValue(local, out var known) &&
            IsStableAtInvocation(invocation, known))
        {
            target = (known.Method, known.Instance);
            return true;
        }

        target = default;
        return false;
    }

    private static bool IsStableAtInvocation(
        IInvocationOperation invocation,
        DirectDelegateTarget target)
    {
        var invocationTree = invocation.Syntax.SyntaxTree;
        var invocationInsideLoop = IsInsideLoop(invocation);
        var invocationInsideNestedCallable = IsInsideNestedCallable(invocation);
        foreach (var invalidation in target.Invalidations)
        {
            if (invalidation.Syntax.SyntaxTree != invocationTree ||
                invalidation.Syntax.SpanStart <=
                    invocation.Syntax.SpanStart ||
                invocationInsideLoop ||
                IsInsideLoop(invalidation) ||
                invocationInsideNestedCallable ||
                IsInsideNestedCallable(invalidation) ||
                target.HasGoto)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsInsideLoop(IOperation operation)
    {
        return Ancestors(operation).Any(static ancestor =>
            ancestor is ILoopOperation);
    }

    private static bool IsInsideNestedCallable(IOperation operation)
    {
        return Ancestors(operation).Any(static ancestor =>
            ancestor is IAnonymousFunctionOperation or
                ILocalFunctionOperation);
    }

    private static IEnumerable<IOperation> Ancestors(
        IOperation operation)
    {
        for (var current = operation.Parent;
             current != null;
             current = current.Parent)
        {
            yield return current;
        }
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
             MethodMayReturnTrue(
                 info.MoveNextMethod,
                 semanticModel.Compilation,
                 cancellationToken) &&
             (operationFacts == null ||
              operationFacts.MethodCanCompleteNormally(
                  info.MoveNextMethod))))
        {
            Add(current, instance: null);
        }
        Add(
            ResolveDisposeMethod(
                info.GetEnumeratorMethod.ReturnType,
                loop.IsAsynchronous,
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
                ImmutableDictionary<int, long>.Empty,
                true));
        }
    }

    private static bool MethodMayReturnTrue(
        IMethodSymbol? method,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (method?.ReturnType.SpecialType !=
                SpecialType.System_Boolean ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return true;
        }

        var declaration = method.DeclaringSyntaxReferences[0]
            .GetSyntax(cancellationToken);
        if (declaration is not MethodDeclarationSyntax methodDeclaration ||
            methodDeclaration.ExpressionBody?.Expression is not { } expression)
        {
            return true;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression, cancellationToken);
        return !constant.HasValue || constant.Value is not false;
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
                    ImmutableDictionary<int, long>.Empty,
                    CanReplay: true));
            }
        }
        return calls.ToImmutable();
    }

    private static IMethodSymbol? ResolveDisposeMethod(
        ITypeSymbol resourceType,
        bool isAsynchronous,
        Compilation compilation,
        HashSet<ITypeSymbol>? visited = null)
    {
        visited ??= new HashSet<ITypeSymbol>(
            SymbolEqualityComparer.Default);
        if (!visited.Add(resourceType))
        {
            return null;
        }
        resourceType = CompilerIdentityBridge.GetNullableUnderlyingType(
            resourceType) ?? resourceType;
        if (resourceType is ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                var constrained = ResolveDisposeMethod(
                    constraint,
                    isAsynchronous,
                    compilation,
                    visited);
                if (constrained != null)
                {
                    return constrained;
                }
            }
            return null;
        }

        var interfaceName = isAsynchronous
            ? "System.IAsyncDisposable"
            : FrameworkTypeMetadataNames.IDisposable;
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
                !method.IsStatic &&
                method.Arity == 0 &&
                method.Parameters.IsEmpty);
    }

    private static ImmutableArray<RequiresCallTarget>
        GetRecursivePatternCalls(
            IRecursivePatternOperation pattern,
            IMethodSymbol deconstruct,
            ManagedFlowResult? flowResult)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        var governingValue = instance ??
            GetRootPatternGoverningValue(pattern);
        if (governingValue != null &&
            (DefiniteOperationFacts.IsDefinitelyNull(governingValue) ||
             flowResult?.ProvesNull(pattern, governingValue) == true))
        {
            return [];
        }

        return [new RequiresCallTarget(
            deconstruct,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            true)];
    }

    private static IOperation? GetRootPatternGoverningValue(
        IPatternOperation pattern)
    {
        IOperation current = pattern;
        while (current.Parent is IPatternOperation or
               IPropertySubpatternOperation)
        {
            current = current.Parent;
        }
        return current is IPatternOperation root
            ? SwitchExpressionFacts.GetGoverningValue(root)
            : null;
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

        var requiredLength = 0;
        var hasSlice = false;
        var sliceIndex = -1;
        for (var index = 0; index < pattern.Patterns.Length; index++)
        {
            if (pattern.Patterns[index] is ISlicePatternOperation)
            {
                hasSlice = true;
                sliceIndex = sliceIndex < 0 ? index : sliceIndex;
            }
            else
            {
                requiredLength++;
            }
        }
        long knownLength = 0;
        var hasKnownLength = compilation != null &&
            TryGetKnownListLength(
                pattern,
                instance,
                compilation!,
                cancellationToken,
                out knownLength);
        if (hasKnownLength &&
            (hasSlice
                ? knownLength < requiredLength
                : knownLength != requiredLength))
        {
            return calls.ToImmutable();
        }

        for (var itemIndex = 0;
             itemIndex < pattern.Patterns.Length;
             itemIndex++)
        {
            var item = pattern.Patterns[itemIndex];
            var member = SwitchExpressionFacts.GetCallableListPatternMember(
                pattern,
                item);
            if (member == null)
            {
                continue;
            }
            ImmutableDictionary<int, long> implicitArguments;
            if (item is ISlicePatternOperation)
            {
                implicitArguments = CreateImplicitListPatternArguments(
                    member,
                    itemIndex,
                    hasKnownLength
                        ? knownLength - requiredLength
                        : null);
            }
            else
            {
                long? implicitIndex = sliceIndex < 0 ||
                    itemIndex < sliceIndex
                        ? itemIndex
                        : hasKnownLength
                            ? knownLength -
                                (pattern.Patterns.Length - itemIndex)
                            : null;
                implicitArguments = CreateImplicitListPatternArguments(
                    member,
                    implicitIndex);
            }
            calls.Add(CreateImplicitListPatternCall(
                member,
                instance,
                implicitArguments));
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
        ImmutableDictionary<int, long> implicitArguments)
    {
        return new RequiresCallTarget(
            method,
            instance,
            [],
            ImmutableDictionary<int, IOperation>.Empty,
            implicitArguments,
            true);
    }

    private static ImmutableDictionary<int, long>
        CreateImplicitListPatternArguments(
            IMethodSymbol method,
            long? firstValue,
            long? secondValue = null)
    {
        var arguments = ImmutableDictionary.CreateBuilder<int, long>();
        if (method.Parameters.Length > 0 &&
            firstValue.HasValue &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
        {
            arguments.Add(0, firstValue.Value);
        }
        if (method.Parameters.Length > 1 &&
            secondValue.HasValue &&
            method.Parameters[1].Type.SpecialType == SpecialType.System_Int32)
        {
            arguments.Add(1, secondValue.Value);
        }
        return arguments.ToImmutable();
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
        if (SharpProof.Effects.ArrayLengthFacts.TryGetConstantLength(
                instance,
                out length))
        {
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
                call.ImplicitIntegerArguments,
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

        IEnumerable<IOperation> Descend(IOperation child)
        {
            return ExecutableUnflowedDescendantsAndSelfCore(
                child,
                operationFacts);
        }

        IEnumerable<IOperation> DescendOptional(IOperation? child)
        {
            if (child is null)
            {
                yield break;
            }

            foreach (var descendant in Descend(child))
            {
                yield return descendant;
            }
        }

        IEnumerable<IOperation> DescendInputs(
            IOperation? instance,
            IEnumerable<IArgumentOperation> arguments)
        {
            if (instance is { } value)
            {
                foreach (var descendant in Descend(value))
                {
                    yield return descendant;
                }
                if (!operationFacts!.MayCompleteNormally(value))
                {
                    yield break;
                }
            }

            foreach (var argument in arguments)
            {
                foreach (var descendant in Descend(argument.Value))
                {
                    yield return descendant;
                }
                if (!operationFacts!.MayCompleteNormally(argument.Value))
                {
                    yield break;
                }
            }
        }

        if (operationFacts != null && operation is IInvocationOperation invocation)
        {
            foreach (var descendant in DescendInputs(
                         invocation.Instance,
                         invocation.Arguments))
            {
                yield return descendant;
            }
            yield return invocation;
            yield break;
        }

        if (operationFacts != null &&
            operation is IObjectCreationOperation creation)
        {
            foreach (var descendant in DescendInputs(
                         instance: null,
                         arguments: creation.Arguments))
            {
                yield return descendant;
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
                         Descend(creation.Initializer))
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
                         Descend(item))
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
            foreach (var descendant in DescendInputs(
                         property.Instance,
                         property.Arguments))
            {
                yield return descendant;
            }
            foreach (var descendant in
                     Descend(assignment.Value))
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
            foreach (var descendant in DescendInputs(
                         propertyReference.Instance,
                         propertyReference.Arguments))
            {
                yield return descendant;
            }
            yield return propertyReference;
            yield break;
        }

        if (operationFacts != null &&
            operation is IConditionalOperation factConditional)
        {
            yield return factConditional;
            foreach (var descendant in
                     Descend(factConditional.Condition))
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
                foreach (var descendant in DescendOptional(branch))
                {
                    yield return descendant;
                }
                yield break;
            }
            foreach (var branch in new[]
                     {
                         factConditional.WhenTrue,
                         factConditional.WhenFalse
                     })
            {
                foreach (var descendant in DescendOptional(branch))
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
                     Descend(factBinary.LeftOperand))
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
                         Descend(factBinary.RightOperand))
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
                     Descend(factCoalesce.Value))
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
                         Descend(factCoalesce.WhenNull))
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
                     Descend(factAccess.Operation))
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
                     Descend(factAccess.WhenNotNull))
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
                     Descend(conditional.Condition))
            {
                yield return descendant;
            }
            var branch = condition
                ? conditional.WhenTrue
                : conditional.WhenFalse;
            foreach (var descendant in DescendOptional(branch))
            {
                yield return descendant;
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
                     Descend(binary.LeftOperand))
            {
                yield return descendant;
            }
            if (left != (binary.OperatorKind ==
                    BinaryOperatorKind.ConditionalOr))
            {
                foreach (var descendant in
                         Descend(binary.RightOperand))
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
                     Descend(coalesce.Value))
            {
                yield return descendant;
            }
            if (coalesce.Value.ConstantValue.Value == null)
            {
                foreach (var descendant in
                         Descend(coalesce.WhenNull))
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
                     Descend(access.Operation))
            {
                yield return descendant;
            }
            yield break;
        }

        if (operation is ISwitchExpressionOperation switchExpression &&
            switchExpression.Value.ConstantValue.HasValue)
        {
            foreach (var descendant in
                     Descend(switchExpression.Value))
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
                    ? SwitchExpressionSelection.Maybe
                    : SwitchExpressionFacts.GetPatternSelection(
                        switchCompilation,
                        arm.Pattern,
                        input,
                        switchExpression.Value.Type);
                if (match == SwitchExpressionSelection.Never)
                {
                    continue;
                }
                if (arm.Guard != null)
                {
                    foreach (var descendant in
                             Descend(arm.Guard))
                    {
                        yield return descendant;
                    }
                    if (operationFacts != null &&
                        !operationFacts.MayCompleteNormally(arm.Guard))
                    {
                        if (match == SwitchExpressionSelection.Always)
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
                         Descend(arm.Value))
                {
                    yield return descendant;
                }
                var guardIsTrue = arm.Guard == null ||
                    arm.Guard.ConstantValue is { HasValue: true, Value: true };
                if (match == SwitchExpressionSelection.Always && guardIsTrue)
                {
                    break;
                }
            }
            yield break;
        }

        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in
                     Descend(child))
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
        if (getter == null ||
            Ancestors(property).Any(static ancestor =>
                ancestor is INameOfOperation))
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
            ImmutableDictionary<int, long>.Empty,
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
            ImmutableDictionary<int, long>.Empty,
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
