using SharpProof.Roslyn;

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
        private readonly bool _rootIsGenerated =
            AnalyzerGeneratedCodePolicy.IsGenerated(
                root,
                rootDeclaration.SyntaxTree,
                semanticModel.Compilation,
                cancellationToken);
        private readonly INamedTypeSymbol? _delegateType =
            semanticModel.Compilation.GetTypeByMetadataName("System.Delegate");
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
            if (!isRoot &&
                !_rootIsGenerated &&
                IsGenerated(caller, declaration))
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
                if (!_rootIsGenerated &&
                    IsGenerated(nested.Method, nested.Declaration))
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
            var pending = new Queue<IMethodSymbol>();
            var scheduledLocals = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default);
            foreach (var method in reachable)
            {
                pending.Enqueue(method);
                scheduledLocals.Add(method);
            }
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = pending.Dequeue();
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
                        if (scheduledLocals.Add(discovered))
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
            var anonymousFunctions = new List<
                IFlowAnonymousFunctionOperation>();
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
                if (operation is IFlowAnonymousFunctionOperation anonymous)
                {
                    anonymousFunctions.Add(anonymous);
                }
            }
            foreach (var anonymous in anonymousFunctions)
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
            if (IsInsideNameOf(value))
            {
                return false;
            }
            if (IsDirectDelegateRemovalOperand(value.Syntax) ||
                IsDiscardedDelegateConversion(value.Syntax))
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
                GetTuplePath(value.Syntax, definition));
        }

        private bool IsDiscardedDelegateConversion(SyntaxNode value)
        {
            var assignment = value.Ancestors()
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.Right.Span.Contains(value.Span));
            if (assignment?.Left is not IdentifierNameSyntax discard ||
                discard.Identifier.Text != "_")
            {
                return false;
            }

            return semanticModel.GetOperation(
                discard,
                cancellationToken) is IDiscardOperation;
        }

        private static bool IsInsideNameOf(IOperation value)
        {
            for (var operation = value.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is INameOfOperation)
                {
                    return true;
                }
            }
            return false;
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
            string[]? tuplePath = null)
        {
            var searches = new Queue<(
                ILocalSymbol Local,
                int DefinitionBlock,
                int DefinitionEnd,
                string[]? TuplePath)>();
            var assignmentThrowCache = new Dictionary<(
                SyntaxTree Tree,
                int Start,
                int End,
                int After), bool>();
            var searched = new Dictionary<
                ILocalSymbol,
                HashSet<(
                    int DefinitionBlock,
                    int DefinitionEnd,
                    string TuplePath)>>(SymbolEqualityComparer.Default);
            searches.Enqueue((
                local,
                definitionBlock,
                definitionEnd,
                tuplePath));
            while (searches.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (local, definitionBlock, definitionEnd, tuplePath) =
                    searches.Dequeue();
                if (!searched.TryGetValue(local, out var localSearches))
                {
                    localSearches = [];
                    searched.Add(local, localSearches);
                }
                var tuplePathKey = tuplePath == null
                    ? "\u0000"
                    : "\u0001" + string.Join("\u0000", tuplePath);
                if (!localSearches.Add((
                        definitionBlock,
                        definitionEnd,
                        tuplePathKey)))
                {
                    continue;
                }

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
                    var pendingWriteOnlyOutCommit = -1;
                    foreach (var entry in BlockOperations(block)
                                 .SelectMany(static operation =>
                                     operation.DescendantsAndSelf())
                                 .OfType<ILocalReferenceOperation>()
                                 .Where(reference =>
                                     SymbolEqualityComparer.Default.Equals(
                                         reference.Local,
                                         local))
                                 .Select(static reference =>
                                     (Reference: reference,
                                         Order: GetReferenceOrder(reference)))
                                 .OrderBy(static item => item.Order))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var reference = entry.Reference;
                        var order = entry.Order;
                        if (order <= after || reference.IsDeclaration)
                        {
                            continue;
                        }
                        if (pendingWriteOnlyOutCommit >= 0 &&
                            order >= pendingWriteOnlyOutCommit)
                        {
                            exceptionalStateSurvivesKill = true;
                            killed = true;
                            break;
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
                            if (IsAssignedStorage(reference))
                            {
                                if (AssignmentKillsTrackedValue(
                                        tuplePath,
                                        accessedTuplePath))
                                {
                                    exceptionalStateSurvivesKill =
                                        BlockMayThrowBeforeAssignmentCommit(
                                            graph,
                                            after,
                                            reference,
                                            assignmentThrowCache);
                                    killed = true;
                                    break;
                                }
                                continue;
                            }
                        }
                        if (TryGetWriteOnlyOutCommit(
                                reference,
                                out var writeOnlyOutCommit))
                        {
                            if (AssignmentKillsTrackedValue(
                                    tuplePath,
                                    accessedTuplePath))
                            {
                                pendingWriteOnlyOutCommit =
                                    pendingWriteOnlyOutCommit < 0
                                        ? writeOnlyOutCommit
                                        : Math.Min(
                                            pendingWriteOnlyOutCommit,
                                            writeOnlyOutCommit);
                            }
                            continue;
                        }
                        string[]? propagatedTuplePath = tuplePath;
                        if (tuplePath != null &&
                            accessedTuplePath.Count != 0)
                        {
                            var shared = 0;
                            while (shared < tuplePath.Length &&
                                   shared < accessedTuplePath.Count &&
                                   string.Equals(
                                       tuplePath[shared],
                                       accessedTuplePath[shared],
                                       StringComparison.Ordinal))
                            {
                                shared++;
                            }
                            if (shared < accessedTuplePath.Count &&
                                shared < tuplePath.Length)
                            {
                                continue;
                            }
                            propagatedTuplePath = shared < tuplePath.Length
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
                            searches.Enqueue((
                                alias,
                                ordinal,
                                aliasDefinition.Span.End,
                                propagatedTuplePath));
                            continue;
                        }
                        if (TryGetDeconstructionDestination(
                                reference.Syntax,
                                propagatedTuplePath,
                                out var deconstructionAlias,
                                out var deconstructionDefinition,
                                out var remainingTuplePath))
                        {
                            if (deconstructionAlias != null)
                            {
                                searches.Enqueue((
                                    deconstructionAlias,
                                    ordinal,
                                    deconstructionDefinition.Span.End,
                                    remainingTuplePath));
                            }
                            continue;
                        }
                        var patternDestinations = GetPatternDestinations(
                            reference,
                            propagatedTuplePath);
                        if (patternDestinations.Count != 0)
                        {
                            foreach (var patternAlias in patternDestinations)
                            {
                                searches.Enqueue((
                                    patternAlias.Local,
                                    ordinal,
                                    patternAlias.Definition.Span.End,
                                    patternAlias.TuplePath));
                            }
                            continue;
                        }
                        if (IsNonExecutingObservation(reference))
                        {
                            continue;
                        }
                        return true;
                    }
                    if (!killed && pendingWriteOnlyOutCommit >= 0)
                    {
                        exceptionalStateSurvivesKill = true;
                        killed = true;
                    }
                    if (killed)
                    {
                        if (exceptionalStateSurvivesKill)
                        {
                            foreach (var successor in RoslynCfgThrowFacts.ExceptionalSuccessors(
                                         graph,
                                         block))
                            {
                                pending.Enqueue((successor.Ordinal, -1));
                            }
                        }
                        continue;
                    }
                    foreach (var successor in RegularSuccessors(graph, block))
                    {
                        pending.Enqueue((successor.Ordinal, -1));
                    }
                    if (BlockMayThrow(block, after))
                    {
                        foreach (var successor in RoslynCfgThrowFacts.ExceptionalSuccessors(
                                     graph,
                                     block))
                        {
                            pending.Enqueue((successor.Ordinal, -1));
                        }
                    }
                }
            }
            return false;

            static bool TryGetWriteOnlyOutCommit(
                ILocalReferenceOperation reference,
                out int commitEnd)
            {
                for (var operation = reference.Parent;
                     operation != null;
                     operation = operation.Parent)
                {
                    if (operation is IArgumentOperation argument)
                    {
                        if (argument.Parameter?.RefKind == RefKind.Out)
                        {
                            commitEnd = argument.Parent?.Syntax.Span.End ??
                                argument.Syntax.Span.End;
                            return true;
                        }
                        break;
                    }
                }
                commitEnd = -1;
                return false;
            }
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
                    components.Add(GetTupleElementName(field.Field));
                    continue;
                }
                break;
            }
            return components;
        }

        private static string GetTupleElementName(IFieldSymbol field)
        {
            if (field.ContainingType is not { IsTupleType: true } tuple)
            {
                return field.Name;
            }
            foreach (var element in tuple.TupleElements)
            {
                if (SymbolEqualityComparer.Default.Equals(element, field) ||
                    SymbolEqualityComparer.Default.Equals(
                        element.CorrespondingTupleField,
                        field))
                {
                    return element.Name;
                }
            }
            return field.Name;
        }

        private static int FindTupleElementIndex(
            INamedTypeSymbol tuple,
            string component)
        {
            for (var index = 0; index < tuple.TupleElements.Length; index++)
            {
                var element = tuple.TupleElements[index];
                if (string.Equals(
                        element.Name,
                        component,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        element.CorrespondingTupleField?.Name,
                        component,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private bool TryGetDeconstructionDestination(
            SyntaxNode reference,
            string[]? tuplePath,
            out ILocalSymbol? local,
            out SyntaxNode definition,
            out string[]? remainingTuplePath)
        {
            local = null;
            definition = null!;
            remainingTuplePath = null;
            if (tuplePath == null || tuplePath.Length == 0)
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
                   consumed < tuplePath.Length)
            {
                var component = tuplePath[consumed];
                var index = FindTupleElementIndex(sourceType, component);
                var elements = GetDeconstructionElements(target);
                if (index < 0 || index >= elements.Length)
                {
                    return false;
                }
                target = elements[index];
                sourceType = sourceType.TupleElements[index].Type as
                    INamedTypeSymbol;
                consumed++;
                if (consumed < tuplePath.Length &&
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
            remainingTuplePath = consumed < tuplePath.Length
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
            ControlFlowGraph graph,
            BasicBlock block)
        {
            var seen = new HashSet<int>();
            foreach (var branch in new[]
                     {
                         block.FallThroughSuccessor,
                         block.ConditionalSuccessor
                     })
            {
                if (branch == null)
                {
                    continue;
                }
                if (!branch.FinallyRegions.IsDefaultOrEmpty)
                {
                    foreach (var region in branch.FinallyRegions)
                    {
                        if (seen.Add(region.FirstBlockOrdinal))
                        {
                            yield return graph.Blocks[region.FirstBlockOrdinal];
                        }
                    }
                }
                if (branch.Semantics is (
                        ControlFlowBranchSemantics.Regular or
                        ControlFlowBranchSemantics.StructuredExceptionHandling) &&
                    branch.Destination is { } destination &&
                    seen.Add(destination.Ordinal))
                {
                    yield return destination;
                }
            }
        }

        private static bool BlockMayThrow(BasicBlock block, int after)
        {
            return BlockOperations(block)
                .Where(operation => operation.Syntax.Span.End > after)
                .SelectMany(static operation =>
                    operation.DescendantsAndSelf())
                .Any(static operation =>
                    RoslynCfgThrowFacts.OperationMayThrow(operation));
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

        private static bool IsAssignedStorage(
            ILocalReferenceOperation reference)
        {
            IOperation storage = reference;
            while (true)
            {
                switch (storage.Parent)
                {
                    case IParenthesizedOperation parenthesized:
                        storage = parenthesized;
                        continue;
                    case IConversionOperation
                    {
                        IsImplicit: true,
                        OperatorMethod: null
                    } conversion:
                        storage = conversion;
                        continue;
                    case IFieldReferenceOperation
                    {
                        Field.ContainingType.IsTupleType: true,
                        Instance: { } instance
                    } field when ReferenceEquals(instance, storage):
                        storage = field;
                        continue;
                    case ISimpleAssignmentOperation assignment:
                        return ReferenceEquals(assignment.Target, storage);
                    default:
                        return false;
                }
            }
        }

        private static bool BlockMayThrowBeforeAssignmentCommit(
            ControlFlowGraph graph,
            int after,
            ILocalReferenceOperation reference,
            Dictionary<(
                SyntaxTree Tree,
                int Start,
                int End,
                int After), bool> cache)
        {
            for (var operation = reference.Parent;
                 operation != null;
                 operation = operation.Parent)
            {
                if (operation is ISimpleAssignmentOperation assignment)
                {
                    var commitEnd = assignment.Syntax.Span.End;
                    var key = (
                        assignment.Syntax.SyntaxTree,
                        assignment.Syntax.SpanStart,
                        commitEnd,
                        after);
                    if (cache.TryGetValue(key, out var cached))
                    {
                        return cached;
                    }

                    // The assignment's RHS may be lowered across several
                    // basic blocks (e.g. a ternary or coalesce), so the
                    // throwing sub-expression is not necessarily in the
                    // same block as the commit. Scan every block, bounded
                    // by the assignment's own syntax span.
                    var result = graph.Blocks
                        .SelectMany(BlockOperations)
                        .Where(candidate =>
                            candidate.Syntax.Span.End > after &&
                            candidate.Syntax.SpanStart < commitEnd)
                        .SelectMany(static candidate =>
                            candidate.DescendantsAndSelf())
                        .Any(static candidate =>
                            RoslynCfgThrowFacts.OperationMayThrow(candidate));
                    cache.Add(key, result);
                    return result;
                }
            }
            return false;
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

        private bool IsNonExecutingObservation(
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
                if (operation is IBinaryOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add or
                            BinaryOperatorKind.Subtract,
                        Type.TypeKind: TypeKind.Delegate
                    })
                {
                    continue;
                }
                if (operation is IPropertyReferenceOperation property &&
                    _delegateType != null &&
                    SymbolEqualityComparer.Default.Equals(
                        property.Property.OriginalDefinition.ContainingType,
                        _delegateType) &&
                    property.Property.OriginalDefinition is
                    {
                        IsStatic: false,
                        IsIndexer: false,
                        GetMethod: not null,
                        SetMethod: null,
                        Parameters.IsEmpty: true,
                        MetadataName: "Method" or "Target"
                    })
                {
                    return true;
                }
                return operation is IBinaryOperation
                {
                    OperatorMethod: null,
                    OperatorKind: BinaryOperatorKind.Equals or
                            BinaryOperatorKind.NotEquals
                } or IIsPatternOperation or
                ISimpleAssignmentOperation
                {
                    Target: IDiscardOperation
                };
            }
            return false;
        }

        private List<(
            ILocalSymbol Local,
            SyntaxNode Definition,
            string[]? TuplePath)> GetPatternDestinations(
                ILocalReferenceOperation reference,
                string[]? tuplePath)
        {
            IIsPatternOperation? match = null;
            for (var current = reference.Parent;
                 current != null;
                 current = current.Parent)
            {
                if (current is IIsPatternOperation isPattern)
                {
                    match = isPattern;
                    break;
                }
            }
            if (match == null)
            {
                return [];
            }

            var result = new List<(
                ILocalSymbol Local,
                SyntaxNode Definition,
                string[]? TuplePath)>();
            var seenLocals = new HashSet<ILocalSymbol>(
                SymbolEqualityComparer.Default);
            var pending = new Stack<(IPatternOperation Pattern, string[]? Path)>();
            pending.Push((match.Pattern, tuplePath));
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (pattern, path) = pending.Pop();
                var declared = pattern switch
                {
                    IDeclarationPatternOperation declaration =>
                        declaration.DeclaredSymbol,
                    IRecursivePatternOperation recursive =>
                        recursive.DeclaredSymbol,
                    IListPatternOperation list => list.DeclaredSymbol,
                    _ => null
                };
                if (declared is ILocalSymbol local &&
                    seenLocals.Add(local))
                {
                    result.Add((local, match.Syntax, path));
                }

                switch (pattern)
                {
                    case IBinaryPatternOperation binary:
                        pending.Push((binary.RightPattern, path));
                        pending.Push((binary.LeftPattern, path));
                        break;
                    case INegatedPatternOperation negated:
                        pending.Push((negated.Pattern, path));
                        break;
                    case ISlicePatternOperation { Pattern: { } slice }:
                        pending.Push((slice, path));
                        break;
                    case IListPatternOperation list:
                        for (var index = list.Patterns.Length - 1;
                             index >= 0;
                             index--)
                        {
                            pending.Push((list.Patterns[index], path));
                        }
                        break;
                    case IRecursivePatternOperation recursive
                        when path is { Length: > 0 }:
                        {
                            var remainingPath = path.Length == 1
                                ? null
                                : path.Skip(1).ToArray();
                            foreach (var subpattern in TupleComponentPatterns(
                                         recursive,
                                         path[0]))
                            {
                                pending.Push((subpattern, remainingPath));
                            }
                            break;
                        }
                    case IRecursivePatternOperation recursive:
                        for (var index =
                                 recursive.PropertySubpatterns.Length - 1;
                             index >= 0;
                             index--)
                        {
                            pending.Push((
                                recursive.PropertySubpatterns[index].Pattern,
                                path));
                        }
                        for (var index =
                                 recursive.DeconstructionSubpatterns.Length - 1;
                             index >= 0;
                             index--)
                        {
                            pending.Push((
                                recursive.DeconstructionSubpatterns[index],
                                path));
                        }
                        break;
                }
            }
            return result;

            static IEnumerable<IPatternOperation> TupleComponentPatterns(
                IRecursivePatternOperation recursive,
                string component)
            {
                if (recursive.InputType is not INamedTypeSymbol
                    {
                        IsTupleType: true
                    } tuple)
                {
                    yield break;
                }

                var componentIndex = FindTupleElementIndex(tuple, component);
                if (componentIndex >= 0 &&
                    componentIndex <
                        recursive.DeconstructionSubpatterns.Length)
                {
                    yield return recursive.DeconstructionSubpatterns[
                        componentIndex];
                }

                foreach (var property in recursive.PropertySubpatterns)
                {
                    if (property.Member is IMemberReferenceOperation member &&
                        (string.Equals(
                             member.Member.Name,
                             component,
                             StringComparison.Ordinal) ||
                         member.Member is IFieldSymbol field &&
                         string.Equals(
                             field.CorrespondingTupleField?.Name,
                             component,
                             StringComparison.Ordinal)))
                    {
                        yield return property.Pattern;
                    }
                }
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
            string[]? trackedPath,
            List<string> assignedPath)
        {
            if (trackedPath == null || assignedPath.Count == 0)
            {
                return true;
            }
            if (assignedPath.Count > trackedPath.Length)
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
            return ReachableOperations(graph)
                .OfType<IFlowAnonymousFunctionOperation>();
        }
    }

}
