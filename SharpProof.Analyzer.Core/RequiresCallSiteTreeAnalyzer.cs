namespace SharpProof.Analyzer;

internal static partial class RequiresCallSiteTreeAnalyzer
{
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        caller = ContractClauseInventoryBuilder
            .NormalizeCallable(caller);
        var discovery = new RequiresCallSiteDiscovery(
            caller,
            declaration,
            semanticModel,
            cancellationToken);
        var potentialOwners =
            discovery.GetPotentialCallOwners(
                session.HasPotentialCallPreconditions);
        if (potentialOwners == null)
        {
            return session.TryBeginRequiresCallSiteAnalysis(
                    caller)
                ? RequiresCallSiteAnalyzer.AnalyzeCallable(
                    caller,
                    declaration,
                    semanticModel,
                    session,
                    reportDiagnostic,
                    graph: null,
                    operationRoot: null,
                    screenForPotentialCalls: false,
                    cancellationToken:
                        cancellationToken)
                : AnalyzerSemanticOutcome.NotApplicable;
        }

        if (potentialOwners.IsEmpty)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        if (!discovery.TryCreateGraph(
                out var operationRoot,
                out var graph))
        {
            return RecordUnavailableOwners(
                caller,
                potentialOwners,
                session);
        }

        return new TreeAnalysis(
                caller,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                potentialOwners,
                cancellationToken)
            .Run(graph, operationRoot);
    }

    private static AnalyzerSemanticOutcome
        RecordUnavailableOwners(
            IMethodSymbol root,
            ImmutableHashSet<IMethodSymbol> owners,
            AnalyzerSession session)
    {
        var rootOutcome =
            AnalyzerSemanticOutcome.NotApplicable;
        foreach (var owner in owners)
        {
            if (!session.TryBeginRequiresCallSiteAnalysis(
                    owner))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    owner,
                    root))
            {
                rootOutcome =
                    AnalyzerSemanticOutcome.Unknown;
            }
            else
            {
                session.RecordSemanticOutcome(
                    owner,
                    AnalyzerSemanticOutcome.Unknown);
            }
        }

        return rootOutcome;
    }

    private sealed class TreeAnalysis(
        IMethodSymbol root,
        SyntaxNode rootDeclaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ImmutableHashSet<IMethodSymbol> potentialOwners,
        CancellationToken cancellationToken)
    {
        private readonly HashSet<IMethodSymbol>
            _visitedPotentialOwners =
                new(SymbolEqualityComparer.Default);
        private AnalyzerSemanticOutcome _rootOutcome =
            AnalyzerSemanticOutcome.NotApplicable;

        internal AnalyzerSemanticOutcome Run(
            ControlFlowGraph graph,
            IOperation? operationRoot)
        {
            AnalyzeGraph(
                root,
                rootDeclaration,
                graph,
                operationRoot,
                isRoot: true);
            foreach (var owner in potentialOwners)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                if (_visitedPotentialOwners.Contains(owner) ||
                    !session
                        .TryBeginRequiresCallSiteAnalysis(
                            owner))
                {
                    continue;
                }

                session.RecordSemanticOutcome(
                    owner,
                    AnalyzerSemanticOutcome.Unknown);
            }

            return _rootOutcome;
        }

        private void AnalyzeGraph(
            IMethodSymbol caller,
            SyntaxNode declaration,
            ControlFlowGraph graph,
            IOperation? operationRoot,
            bool isRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            caller = ContractClauseInventoryBuilder
                .NormalizeCallable(caller);
            if (!isRoot && IsGenerated(caller, declaration))
            {
                RecordGeneratedSubtree(declaration);
                return;
            }
            if (potentialOwners.Contains(caller))
            {
                _visitedPotentialOwners.Add(caller);
                var outcome = AnalyzerSemanticOutcome.NotApplicable;
                if (session.TryBeginRequiresCallSiteAnalysis(caller))
                {
                    outcome = SharpProofControlAttributePolicy
                        .ValidateAndShouldSuppress(
                            caller,
                            session,
                            reportDiagnostic,
                            cancellationToken)
                        ? AnalyzerSemanticOutcome.Suppressed
                        : RequiresCallSiteAnalyzer.AnalyzeCallable(
                            caller,
                            declaration,
                            semanticModel,
                            session,
                            reportDiagnostic,
                            graph,
                            operationRoot,
                            screenForPotentialCalls: false,
                            cancellationToken:
                                cancellationToken);
                }
                if (isRoot)
                {
                    _rootOutcome = outcome;
                }
                else
                {
                    session.RecordSemanticOutcome(
                        caller,
                        outcome);
                }
            }

            foreach (var nested in
                     GetNestedCallables(graph))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();
                if (IsGenerated(nested.Method, nested.Declaration))
                {
                    RecordGeneratedSubtree(nested.Declaration);
                    continue;
                }
                if (nested.IsExpressionTree)
                {
                    RecordUnsupportedNested(
                        nested.Method);
                    continue;
                }

                ControlFlowGraph childGraph;
                try
                {
                    childGraph =
                        nested.AnonymousFunction == null
                            ? graph
                                .GetLocalFunctionControlFlowGraph(
                                    nested.Method,
                                    cancellationToken)
                            : graph
                                .GetAnonymousFunctionControlFlowGraph(
                                    nested.AnonymousFunction,
                                    cancellationToken);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                        InvalidOperationException)
                {
                    RecordUnsupportedNested(
                        nested.Method);
                    continue;
                }

                AnalyzeGraph(
                    nested.Method,
                    nested.Declaration,
                    childGraph,
                    childGraph.OriginalOperation,
                    isRoot: false);
            }
        }

        private bool IsGenerated(
            IMethodSymbol method,
            SyntaxNode declaration)
        {
            return AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                semanticModel.Compilation,
                cancellationToken);
        }

        private void RecordGeneratedSubtree(SyntaxNode declaration)
        {
            foreach (var owner in potentialOwners)
            {
                var belongsToGeneratedSubtree = owner
                    .DeclaringSyntaxReferences.Any(reference =>
                        ReferenceEquals(
                            reference.SyntaxTree,
                            declaration.SyntaxTree) &&
                        declaration.FullSpan.Contains(reference.Span));
                if (belongsToGeneratedSubtree &&
                    _visitedPotentialOwners.Add(owner) &&
                    session.TryBeginRequiresCallSiteAnalysis(owner))
                {
                    session.RecordSemanticOutcome(
                        owner,
                        AnalyzerSemanticOutcome.NotApplicable);
                }
            }
        }

        private void RecordUnsupportedNested(
            IMethodSymbol method)
        {
            method = ContractClauseInventoryBuilder
                .NormalizeCallable(method);
            if (!potentialOwners.Contains(method) ||
                !_visitedPotentialOwners.Add(method) ||
                !session.TryBeginRequiresCallSiteAnalysis(
                    method))
            {
                return;
            }

            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Unknown);
        }

        private ImmutableArray<NestedCallable>
            GetNestedCallables(
                ControlFlowGraph graph)
        {
            var result =
                ImmutableArray.CreateBuilder<
                    NestedCallable>();
            var seen = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            var localMethods = ImmutableHashSet.CreateRange<IMethodSymbol>(
                SymbolEqualityComparer.Default,
                graph.LocalFunctions.Select(
                    static method => ContractClauseInventoryBuilder
                        .NormalizeCallable(method).OriginalDefinition));
            var reachableLocals = GetReachableLocalFunctions(
                graph, localMethods);
            foreach (var method in localMethods)
            {
                if (!reachableLocals.Contains(method))
                {
                    _visitedPotentialOwners.Add(method);
                    continue;
                }
                var declaration =
                    GetDeclaration(method);
                if (declaration != null &&
                    seen.Add(method))
                {
                    result.Add(new(
                        method,
                        declaration,
                        AnonymousFunction: null,
                        IsExpressionTree: false));
                }
            }

            foreach (var anonymous in
                     GetAnonymousFunctions(graph))
            {
                var method =
                    ContractClauseInventoryBuilder
                        .NormalizeCallable(
                            anonymous.Symbol);
                var expressionTree = IsExpressionTree(
                    anonymous.Syntax);
                if (!expressionTree &&
                    !IsAnonymousExecutableOrEscaped(
                        graph,
                        anonymous))
                {
                    _visitedPotentialOwners.Add(method);
                    continue;
                }
                if (seen.Add(method))
                {
                    result.Add(new(
                        method,
                        anonymous.Syntax,
                        anonymous,
                        expressionTree));
                }
            }

            return [
                .. result.OrderBy(static nested =>
                    nested.Declaration.SyntaxTree
                        .FilePath,
                    StringComparer.Ordinal)
                    .ThenBy(static nested =>
                        nested.Declaration.SpanStart)
            ];
        }

        private ImmutableHashSet<IMethodSymbol>
            GetReachableLocalFunctions(
                ControlFlowGraph graph,
                ImmutableHashSet<IMethodSymbol> candidates)
        {
            if (candidates.IsEmpty)
            {
                return candidates;
            }
            var reachable = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            var scannedAnonymous = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            if (!TryCollectLocalReferences(
                    graph, candidates, reachable, scannedAnonymous))
            {
                return candidates;
            }
            var pending = new Queue<IMethodSymbol>(reachable);
            var scannedLocals = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = pending.Dequeue();
                if (!scannedLocals.Add(method))
                {
                    continue;
                }
                ControlFlowGraph child;
                try
                {
                    child = graph.GetLocalFunctionControlFlowGraph(
                        method, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                        InvalidOperationException)
                {
                    return candidates;
                }
                var count = reachable.Count;
                if (!TryCollectLocalReferences(
                        child, candidates, reachable, scannedAnonymous))
                {
                    return candidates;
                }
                if (reachable.Count != count)
                {
                    foreach (var discovered in reachable)
                    {
                        if (!scannedLocals.Contains(discovered))
                        {
                            pending.Enqueue(discovered);
                        }
                    }
                }
            }
            return reachable.ToImmutableHashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
        }

        private bool TryCollectLocalReferences(
            ControlFlowGraph graph,
            ImmutableHashSet<IMethodSymbol> candidates,
            HashSet<IMethodSymbol> reachable,
            HashSet<IMethodSymbol> scannedAnonymous)
        {
            foreach (var operation in ReachableOperations(graph))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var referenced = operation switch
                {
                    IInvocationOperation invocation =>
                        invocation.TargetMethod,
                    IMethodReferenceOperation methodReference =>
                        IsAnonymousExecutableOrEscaped(
                            graph,
                            methodReference)
                            ? methodReference.Method
                            : null,
                    _ => null
                };
                if (referenced != null)
                {
                    referenced = ContractClauseInventoryBuilder
                        .NormalizeCallable(referenced).OriginalDefinition;
                    if (candidates.Contains(referenced))
                    {
                        reachable.Add(referenced);
                    }
                }
            }
            foreach (var anonymous in GetAnonymousFunctions(graph))
            {
                if (IsExpressionTree(anonymous.Syntax) ||
                    !IsAnonymousExecutableOrEscaped(
                        graph,
                        anonymous))
                {
                    continue;
                }
                var method = ContractClauseInventoryBuilder
                    .NormalizeCallable(anonymous.Symbol);
                if (!scannedAnonymous.Add(method))
                {
                    continue;
                }
                try
                {
                    var child = graph
                        .GetAnonymousFunctionControlFlowGraph(
                            anonymous, cancellationToken);
                    if (!TryCollectLocalReferences(
                            child, candidates, reachable,
                            scannedAnonymous))
                    {
                        return false;
                    }
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                        InvalidOperationException)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsAnonymousExecutableOrEscaped(
            ControlFlowGraph graph,
            IOperation value)
        {
            if (IsDirectDelegateRemovalOperand(value.Syntax))
            {
                return false;
            }
            if (!TryGetLocalDestination(
                    value.Syntax,
                    out var initialLocal,
                    out var definition))
            {
                return true;
            }
            var block = FindContainingBlock(graph, value.Syntax);
            if (block == null)
            {
                return true;
            }
            return CanReachConsumption(
                graph,
                initialLocal,
                block.Ordinal,
                definition.Span.End,
                new HashSet<SyntaxNode> { definition },
                GetTuplePath(value.Syntax, definition));
        }

        private bool TryGetLocalDestination(
            SyntaxNode value,
            out ILocalSymbol local,
            out SyntaxNode definition)
        {
            foreach (var ancestor in value.Ancestors())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ancestor is EqualsValueClauseSyntax equalsValue &&
                    equalsValue.Value.Span.Contains(value.Span) &&
                    equalsValue.Parent is VariableDeclaratorSyntax variable &&
                    semanticModel.GetDeclaredSymbol(
                        variable,
                        cancellationToken) is ILocalSymbol declared)
                {
                    local = declared;
                    definition = variable;
                    return true;
                }

                if (ancestor is AssignmentExpressionSyntax assignment &&
                    assignment.Right.Span.Contains(value.Span) &&
                    semanticModel.GetSymbolInfo(
                        assignment.Left,
                        cancellationToken).Symbol is ILocalSymbol assigned)
                {
                    local = assigned;
                    definition = assignment;
                    return true;
                }

                if (ancestor is StatementSyntax or ArrowExpressionClauseSyntax)
                {
                    break;
                }
            }

            local = null!;
            definition = null!;
            return false;
        }

        private bool CanReachConsumption(
            ControlFlowGraph graph,
            ILocalSymbol local,
            int definitionBlock,
            int definitionEnd,
            HashSet<SyntaxNode> activeDefinitions,
            IReadOnlyList<string>? tuplePath = null)
        {
            var pending = new Queue<(int Ordinal, int After)>();
            var visited = new HashSet<(int Ordinal, bool FromStart)>();
            pending.Enqueue((definitionBlock, definitionEnd));
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (ordinal, after) = pending.Dequeue();
                if (!visited.Add((ordinal, after < 0)))
                {
                    continue;
                }
                var block = graph.Blocks[ordinal];
                var killed = false;
                var exceptionalStateSurvivesKill = false;
                foreach (var reference in BlockOperations(block)
                             .SelectMany(static operation =>
                                 operation.DescendantsAndSelf())
                             .OfType<ILocalReferenceOperation>()
                             .Where(reference =>
                                 SymbolEqualityComparer.Default.Equals(
                                     reference.Local,
                                     local))
                             .OrderBy(GetReferenceOrder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var order = GetReferenceOrder(reference);
                    if (order <= after || reference.IsDeclaration)
                    {
                        continue;
                    }
                    var accessedTuplePath = GetAccessedTuplePath(reference);
                    if (IsAssignmentTarget(reference.Syntax))
                    {
                        // A target-shaped reference that has no enclosing
                        // ISimpleAssignmentOperation isn't the real commit:
                        // a multi-block RHS (e.g. a ternary) can lower into
                        // a flow-capture that happens to share the target's
                        // syntax span in an earlier block. Only the
                        // reference embedded in the actual assignment
                        // operation represents the commit.
                        if (!HasEnclosingSimpleAssignment(reference))
                        {
                            continue;
                        }
                        if (AssignmentKillsTrackedValue(
                                tuplePath,
                                accessedTuplePath))
                        {
                            exceptionalStateSurvivesKill =
                                BlockMayThrowBeforeAssignmentCommit(
                                    graph,
                                    after,
                                    reference);
                            killed = true;
                            break;
                        }
                        continue;
                    }
                    IReadOnlyList<string>? propagatedTuplePath = tuplePath;
                    if (tuplePath != null && accessedTuplePath.Count != 0)
                    {
                        var shared = 0;
                        while (shared < tuplePath.Count &&
                               shared < accessedTuplePath.Count &&
                               string.Equals(
                                   tuplePath[shared],
                                   accessedTuplePath[shared],
                                   StringComparison.Ordinal))
                        {
                            shared++;
                        }
                        if (shared < accessedTuplePath.Count &&
                            shared < tuplePath.Count)
                        {
                            continue;
                        }
                        propagatedTuplePath = shared < tuplePath.Count
                            ? tuplePath.Skip(shared).ToArray()
                            : null;
                    }
                    if (TryGetLocalDestination(
                            reference.Syntax,
                            out var alias,
                            out var aliasDefinition) &&
                        (accessedTuplePath.Count != 0 ||
                         IsDirectDelegatePropagation(
                             reference.Syntax,
                             aliasDefinition)))
                    {
                        if (activeDefinitions.Add(aliasDefinition))
                        {
                            try
                            {
                                if (CanReachConsumption(
                                        graph,
                                        alias,
                                        ordinal,
                                        aliasDefinition.Span.End,
                                        activeDefinitions,
                                        propagatedTuplePath))
                                {
                                    return true;
                                }
                            }
                            finally
                            {
                                activeDefinitions.Remove(aliasDefinition);
                            }
                        }
                        continue;
                    }
                    if (TryGetDeconstructionDestination(
                            reference.Syntax,
                            propagatedTuplePath,
                            out var deconstructionAlias,
                            out var deconstructionDefinition,
                            out var remainingTuplePath))
                    {
                        if (deconstructionAlias != null &&
                            activeDefinitions.Add(deconstructionDefinition))
                        {
                            try
                            {
                                if (CanReachConsumption(
                                        graph,
                                        deconstructionAlias,
                                        ordinal,
                                        deconstructionDefinition.Span.End,
                                        activeDefinitions,
                                        remainingTuplePath))
                                {
                                    return true;
                                }
                            }
                            finally
                            {
                                activeDefinitions.Remove(
                                    deconstructionDefinition);
                            }
                        }
                        continue;
                    }
                    var patternDestinations = GetPatternDestinations(
                        reference.Syntax);
                    if (patternDestinations.Count != 0)
                    {
                        foreach (var patternAlias in patternDestinations)
                        {
                            if (!activeDefinitions.Add(
                                    patternAlias.Definition))
                            {
                                continue;
                            }
                            try
                            {
                                if (CanReachConsumption(
                                        graph,
                                        patternAlias.Local,
                                        ordinal,
                                        patternAlias.Definition.Span.End,
                                        activeDefinitions,
                                        propagatedTuplePath))
                                {
                                    return true;
                                }
                            }
                            finally
                            {
                                activeDefinitions.Remove(
                                    patternAlias.Definition);
                            }
                        }
                        continue;
                    }
                    if (IsNonExecutingObservation(reference))
                    {
                        continue;
                    }
                    return true;
                }
                if (killed)
                {
                    if (exceptionalStateSurvivesKill)
                    {
                        foreach (var successor in ExceptionalSuccessors(
                                     graph,
                                     block))
                        {
                            pending.Enqueue((successor.Ordinal, -1));
                        }
                    }
                    continue;
                }
                foreach (var successor in RegularSuccessors(block))
                {
                    pending.Enqueue((successor.Ordinal, -1));
                }
                if (BlockMayThrow(block, after))
                {
                    foreach (var successor in ExceptionalSuccessors(
                                 graph,
                                 block))
                    {
                        pending.Enqueue((successor.Ordinal, -1));
                    }
                }
            }
            return false;
        }

        private string[]? GetTuplePath(
            SyntaxNode value,
            SyntaxNode definition)
        {
            var components = value.Ancestors()
                .OfType<ArgumentSyntax>()
                .Where(candidate =>
                    candidate.Parent is TupleExpressionSyntax tuple &&
                    definition.Span.Contains(tuple.Span))
                .Select(argument =>
                {
                    var owner = (TupleExpressionSyntax)argument.Parent!;
                    var index = owner.Arguments.IndexOf(argument);
                    return GetConvertedTupleElementName(owner, index) ??
                        argument.NameColon?.Name.Identifier.ValueText ??
                        $"Item{index + 1}";
                })
                .Reverse()
                .ToArray();
            return components.Length == 0 ? null : components;
        }

        private string? GetConvertedTupleElementName(
            TupleExpressionSyntax owner,
            int index)
        {
            var convertedType = semanticModel.GetTypeInfo(
                    owner,
                    cancellationToken)
                .ConvertedType as INamedTypeSymbol;
            return convertedType is { IsTupleType: true } &&
                index < convertedType.TupleElements.Length
                ? convertedType.TupleElements[index].Name
                : null;
        }

        private static List<string> GetAccessedTuplePath(
            ILocalReferenceOperation reference)
        {
            var components = new List<string>();
            for (var operation = reference.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is IConversionOperation or
                    IParenthesizedOperation)
                {
                    continue;
                }
                if (operation is IFieldReferenceOperation field &&
                    field.Field.ContainingType?.IsTupleType == true)
                {
                    components.Add(field.Field.Name);
                    continue;
                }
                break;
            }
            return components;
        }

        private bool TryGetDeconstructionDestination(
            SyntaxNode reference,
            IReadOnlyList<string>? tuplePath,
            out ILocalSymbol? local,
            out SyntaxNode definition,
            out IReadOnlyList<string>? remainingTuplePath)
        {
            local = null;
            definition = null!;
            remainingTuplePath = null;
            if (tuplePath == null || tuplePath.Count == 0)
            {
                return false;
            }

            var assignment = reference.AncestorsAndSelf()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    candidate.Right.Span.Contains(reference.Span));
            if (assignment == null)
            {
                return false;
            }

            SyntaxNode target = assignment.Left;
            var sourceType = semanticModel.GetTypeInfo(
                assignment.Right,
                cancellationToken).Type as INamedTypeSymbol;
            var consumed = 0;
            while (sourceType?.IsTupleType == true &&
                   consumed < tuplePath.Count)
            {
                var component = tuplePath[consumed];
                var index = -1;
                for (var candidate = 0;
                     candidate < sourceType.TupleElements.Length;
                     candidate++)
                {
                    var element = sourceType.TupleElements[candidate];
                    if (string.Equals(
                            element.Name,
                            component,
                            StringComparison.Ordinal) ||
                        string.Equals(
                            element.CorrespondingTupleField?.Name,
                            component,
                            StringComparison.Ordinal))
                    {
                        index = candidate;
                        break;
                    }
                }
                var elements = GetDeconstructionElements(target);
                if (index < 0 || index >= elements.Length)
                {
                    return false;
                }
                target = elements[index];
                sourceType = sourceType.TupleElements[index].Type as
                    INamedTypeSymbol;
                consumed++;
                if (consumed < tuplePath.Count &&
                    GetDeconstructionElements(target).Length == 0)
                {
                    break;
                }
            }

            if (consumed == 0)
            {
                return false;
            }
            definition = assignment;
            remainingTuplePath = consumed < tuplePath.Count
                ? tuplePath.Skip(consumed).ToArray()
                : null;
            local = target switch
            {
                SingleVariableDesignationSyntax designation =>
                    semanticModel.GetDeclaredSymbol(
                        designation,
                        cancellationToken) as ILocalSymbol,
                DeclarationExpressionSyntax
                { Designation: SingleVariableDesignationSyntax designation } =>
                    semanticModel.GetDeclaredSymbol(
                        designation,
                        cancellationToken) as ILocalSymbol,
                IdentifierNameSyntax identifier => semanticModel.GetSymbolInfo(
                    identifier,
                    cancellationToken).Symbol as ILocalSymbol,
                _ => null
            };
            return local != null || IsDiscardDeconstructionTarget(target);
        }

        private static SyntaxNode[] GetDeconstructionElements(
            SyntaxNode target)
        {
            return target switch
            {
                TupleExpressionSyntax tuple => tuple.Arguments
                    .Select(static argument => (SyntaxNode)argument.Expression)
                    .ToArray(),
                DeclarationExpressionSyntax
                { Designation: ParenthesizedVariableDesignationSyntax tuple } =>
                    tuple.Variables.Cast<SyntaxNode>().ToArray(),
                ParenthesizedVariableDesignationSyntax tuple =>
                    tuple.Variables.Cast<SyntaxNode>().ToArray(),
                _ => []
            };
        }

        private static bool IsDiscardDeconstructionTarget(SyntaxNode target)
        {
            return target is DiscardDesignationSyntax or DiscardPatternSyntax ||
                target is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "_";
        }

        private static BasicBlock? FindContainingBlock(
            ControlFlowGraph graph,
            SyntaxNode syntax)
        {
            return graph.Blocks.FirstOrDefault(block =>
                BlockOperations(block).Any(operation =>
                    operation.DescendantsAndSelf().Any(descendant =>
                        descendant.Syntax.SyntaxTree == syntax.SyntaxTree &&
                        descendant.Syntax.Span.Contains(syntax.Span))));
        }

        private static IEnumerable<IOperation> BlockOperations(
            BasicBlock block)
        {
            return block.Operations.Concat(
                block.BranchValue == null
                    ? []
                    : [block.BranchValue]);
        }

        private static IEnumerable<BasicBlock> RegularSuccessors(
            BasicBlock block)
        {
            if (block.FallThroughSuccessor is
                {
                    Semantics: ControlFlowBranchSemantics.Regular or
                    ControlFlowBranchSemantics.StructuredExceptionHandling,
                    Destination: not null
                } fallThrough)
            {
                yield return fallThrough.Destination!;
            }
            if (block.ConditionalSuccessor is
                {
                    Semantics: ControlFlowBranchSemantics.Regular or
                    ControlFlowBranchSemantics.StructuredExceptionHandling,
                    Destination: not null
                } conditional &&
                conditional.Destination.Ordinal !=
                    block.FallThroughSuccessor?.Destination?.Ordinal)
            {
                yield return conditional.Destination!;
            }
        }

        private static bool BlockMayThrow(BasicBlock block, int after)
        {
            return BlockOperations(block)
                .Where(operation => operation.Syntax.Span.End > after)
                .SelectMany(static operation =>
                    operation.DescendantsAndSelf())
                .Any(static operation => OperationMayThrow(operation));
        }

        private static bool HasEnclosingSimpleAssignment(
            ILocalReferenceOperation reference)
        {
            for (var operation = reference.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is ISimpleAssignmentOperation)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool BlockMayThrowBeforeAssignmentCommit(
            ControlFlowGraph graph,
            int after,
            ILocalReferenceOperation reference)
        {
            for (var operation = reference.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is ISimpleAssignmentOperation assignment)
                {
                    var commitEnd = assignment.Syntax.Span.End;

                    // The assignment's RHS may be lowered across several
                    // basic blocks (e.g. a ternary or coalesce), so the
                    // throwing sub-expression is not necessarily in the
                    // same block as the commit. Scan every block, bounded
                    // by the assignment's own syntax span.
                    return graph.Blocks
                        .SelectMany(BlockOperations)
                        .Where(candidate =>
                            candidate.Syntax.Span.End > after &&
                            candidate.Syntax.SpanStart < commitEnd)
                        .SelectMany(static candidate =>
                            candidate.DescendantsAndSelf())
                        .Any(static candidate =>
                            OperationMayThrow(candidate));
                }
            }
            return false;
        }

        private static bool OperationMayThrow(IOperation operation)
        {
            if (operation is IConversionOperation conversion)
            {
                return conversion.IsChecked ||
                    (!conversion.IsTryCast && !conversion.IsImplicit &&
                     (conversion.Conversion.IsReference ||
                      conversion.Operand.Type?.IsReferenceType == true &&
                      conversion.Type?.IsValueType == true));
            }
            return operation is
                IThrowOperation or
                IInvocationOperation or
                IDynamicInvocationOperation or
                IDynamicObjectCreationOperation or
                IDynamicIndexerAccessOperation or
                IFunctionPointerInvocationOperation or
                IObjectCreationOperation or
                IArrayCreationOperation or
                IArrayElementReferenceOperation or
                IDynamicMemberReferenceOperation or
                IFieldReferenceOperation { Instance: not null } or
                IPropertyReferenceOperation or
                IEventAssignmentOperation or
                ILockOperation or
                IAwaitOperation or
                ICompoundAssignmentOperation
                { IsChecked: true } or
                ICompoundAssignmentOperation
                {
                    OperatorKind: BinaryOperatorKind.Divide or
                        BinaryOperatorKind.Remainder
                } or
                IBinaryOperation { IsChecked: true } or
                IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Divide or
                        BinaryOperatorKind.Remainder
                } or
                IUnaryOperation { IsChecked: true } or
                IIncrementOrDecrementOperation { IsChecked: true };
        }

        private static IEnumerable<BasicBlock> ExceptionalSuccessors(
            ControlFlowGraph graph,
            BasicBlock block)
        {
            var yielded = new HashSet<int>();
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (region.Kind != ControlFlowRegionKind.Try ||
                    region.EnclosingRegion is not { } owner)
                {
                    continue;
                }
                foreach (var handler in owner.NestedRegions.Where(candidate =>
                             candidate.Kind is ControlFlowRegionKind.Filter or
                                 ControlFlowRegionKind.Catch or
                                 ControlFlowRegionKind.FilterAndHandler or
                                 ControlFlowRegionKind.Finally))
                {
                    if (yielded.Add(handler.FirstBlockOrdinal))
                    {
                        yield return graph.Blocks[handler.FirstBlockOrdinal];
                    }
                }
            }
        }

        private static int GetReferenceOrder(
            ILocalReferenceOperation reference)
        {
            var assignment = reference.Syntax.AncestorsAndSelf()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.Left.Span.Contains(reference.Syntax.Span));
            return assignment?.Span.End ?? reference.Syntax.SpanStart;
        }

        private static bool IsDirectDelegatePropagation(
            SyntaxNode reference,
            SyntaxNode definition)
        {
            for (var current = reference.Parent;
                 current != null && current != definition;
                 current = current.Parent)
            {
                if (current is InvocationExpressionSyntax or
                    ObjectCreationExpressionSyntax or
                    ElementAccessExpressionSyntax or
                    MemberAccessExpressionSyntax)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsDirectDelegateRemovalOperand(
            SyntaxNode value)
        {
            var assignment = value.Ancestors()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.IsKind(
                        SyntaxKind.SubtractAssignmentExpression) &&
                    candidate.Right.Span.Contains(value.Span));
            return assignment != null &&
                IsDirectDelegatePropagation(value, assignment);
        }

        private static bool IsNonExecutingObservation(
            ILocalReferenceOperation reference)
        {
            if (reference.Syntax.Ancestors()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment =>
                    assignment.IsKind(
                        SyntaxKind.CoalesceAssignmentExpression) &&
                    assignment.Left.Span.Contains(reference.Syntax.Span)))
            {
                return true;
            }
            for (var operation = reference.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is IConversionOperation or
                    IParenthesizedOperation ||
                    operation is IFieldReferenceOperation tupleField &&
                    tupleField.Field.ContainingType?.IsTupleType == true)
                {
                    continue;
                }
                return operation is IBinaryOperation
                {
                    OperatorMethod: null,
                    OperatorKind: BinaryOperatorKind.Equals or
                            BinaryOperatorKind.NotEquals
                } or IIsPatternOperation;
            }
            return false;
        }

        private List<(ILocalSymbol Local, SyntaxNode Definition)>
            GetPatternDestinations(SyntaxNode reference)
        {
            var pattern = reference.Ancestors()
                .OfType<IsPatternExpressionSyntax>()
                .FirstOrDefault();
            if (pattern == null)
            {
                return [];
            }
            var result = new List<(
                ILocalSymbol Local,
                SyntaxNode Definition)>();
            foreach (var designation in WholeInputDesignations(
                         pattern.Pattern))
            {
                if (designation is SingleVariableDesignationSyntax single &&
                    semanticModel.GetDeclaredSymbol(
                        single,
                        cancellationToken) is ILocalSymbol declared &&
                    !result.Any(candidate =>
                        SymbolEqualityComparer.Default.Equals(
                            candidate.Local,
                            declared)))
                {
                    result.Add((declared, pattern));
                }
            }
            return result;
        }

        private static IEnumerable<VariableDesignationSyntax>
            WholeInputDesignations(PatternSyntax pattern)
        {
            switch (pattern)
            {
                case DeclarationPatternSyntax declaration:
                    yield return declaration.Designation;
                    yield break;
                case VarPatternSyntax varPattern:
                    yield return varPattern.Designation;
                    yield break;
                case RecursivePatternSyntax
                { Designation: { } designation }:
                    yield return designation;
                    yield break;
                case ParenthesizedPatternSyntax parenthesized:
                    foreach (var nested in WholeInputDesignations(
                                 parenthesized.Pattern))
                    {
                        yield return nested;
                    }
                    yield break;
                case BinaryPatternSyntax binary:
                    foreach (var nested in WholeInputDesignations(
                                 binary.Left))
                    {
                        yield return nested;
                    }
                    foreach (var nested in WholeInputDesignations(
                                 binary.Right))
                    {
                        yield return nested;
                    }
                    yield break;
            }
        }

        private static bool IsAssignmentTarget(
            SyntaxNode reference)
        {
            return reference.Ancestors()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment =>
                    assignment.IsKind(
                        SyntaxKind.SimpleAssignmentExpression) &&
                    assignment.Left.Span.Contains(reference.Span));
        }

        private static bool AssignmentKillsTrackedValue(
            IReadOnlyList<string>? trackedPath,
            List<string> assignedPath)
        {
            if (trackedPath == null || assignedPath.Count == 0)
            {
                return true;
            }
            if (assignedPath.Count > trackedPath.Count)
            {
                return false;
            }
            return assignedPath
                .Select((component, index) => (component, index))
                .All(pair => string.Equals(
                    pair.component,
                    trackedPath[pair.index],
                    StringComparison.Ordinal));
        }

        private static IEnumerable<IOperation> ReachableOperations(
            ControlFlowGraph graph)
        {
            return graph.Blocks
                .Where(static block => block.IsReachable)
                .SelectMany(static block =>
                    block.Operations.Concat(
                        block.BranchValue == null
                            ? []
                            : [block.BranchValue]))
                .SelectMany(static operation =>
                    operation.DescendantsAndSelf());
        }

        private SyntaxNode? GetDeclaration(
            IMethodSymbol method)
        {
            return method.DeclaringSyntaxReferences
                .Select(reference =>
                    reference.GetSyntax(
                        cancellationToken))
                .OrderBy(static syntax =>
                    syntax.SyntaxTree.FilePath,
                    StringComparer.Ordinal)
                .ThenBy(static syntax =>
                    syntax.SpanStart)
                .FirstOrDefault();
        }

        private bool IsExpressionTree(
            SyntaxNode declaration)
        {
            var expression = semanticModel.Compilation
                .GetTypeByMetadataName(
                    FrameworkTypeMetadataNames
                        .ExpressionOfT);
            return expression != null &&
                semanticModel.GetTypeInfo(
                        declaration,
                        cancellationToken)
                    .ConvertedType is
                    INamedTypeSymbol converted &&
                SymbolEqualityComparer.Default.Equals(
                    converted.OriginalDefinition,
                    expression.OriginalDefinition);
        }

        private static IEnumerable<
            IFlowAnonymousFunctionOperation>
            GetAnonymousFunctions(
                ControlFlowGraph graph)
        {
            return graph.Blocks
                .Where(static block => block.IsReachable)
                .SelectMany(static block =>
                    block.Operations.Concat(
                        block.BranchValue == null
                            ? []
                            : [block.BranchValue]))
                .SelectMany(static operation =>
                    operation.DescendantsAndSelf())
                .OfType<
                    IFlowAnonymousFunctionOperation>();
        }
    }

}
