namespace SharpProof.Effects;

/// <summary>
/// Lowers one source method into the local node consumed by the effect call graph.
/// </summary>
internal sealed class EffectMethodNodeBuilder
{
    private readonly EffectAnalysisSession _session;
    private readonly Compilation _compilation;
    private readonly ManagedAbstractFlow _managedFlow;

    internal EffectMethodNodeBuilder(
        EffectAnalysisSession session,
        Compilation compilation,
        ManagedAbstractFlow managedFlow)
    {
        _session = session;
        _compilation = compilation;
        _managedFlow = managedFlow;
    }

    internal EffectMethodNode Build(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var calls = new List<EffectCallSite>();
        var root = GetOperationRoot(method, cancellationToken);
        if (root == null)
        {
            return new EffectMethodNode(EffectSummaryOperations.UnknownBoundary(
                EffectUncertainty.UnsupportedOperation), [], []);
        }

        var graph = TryCreateControlFlowGraph(root, cancellationToken);
        var abstractAnalysis = graph == null
            ? null
            : _managedFlow.Analyze(
                method,
                graph,
                entryState: null,
                cancellationToken);
        var scanner = new OperationEffectScanner(
            _session, method, calls, root, abstractAnalysis?.Result,
            allowDirectWitnesses:
                graph != null &&
                HasDefiniteBodyEntry(method, _session.ApiSpecs));
        var initializers = ScanConstructorMemberInitializers(
            method,
            scanner,
            cancellationToken);
        var localSummary = initializers.Summary;
        if (initializers.CompletesNormally)
        {
            var bodySummary = graph == null
                ? EffectSummaryOperations.Join(
                    scanner.Scan(root),
                    EffectSummaryOperations.Unsupported())
                : AnalyzeControlFlowGraph(graph, scanner);

            // Cyclic scalar flow does not invalidate the conservative
            // all-block effect scan.
            if (abstractAnalysis is
                {
                    IsComplete: false,
                    IncompleteReason: not
                        EffectAnalysisIncompleteReason.CyclicControlFlow
                })
            {
                bodySummary = EffectSummaryOperations.Join(
                    bodySummary,
                    EffectSummaryOperations.IncompleteAnalysis(
                        abstractAnalysis.IncompleteReason));
            }

            localSummary = EffectSummaryOperations.Join(
                localSummary,
                bodySummary,
                scanner.ScanLexicalControlEffects(root),
                scanner.ScanAwaitProtocolEffects(root),
                scanner.ScanUsingDisposalEffects(root));
        }

        localSummary = EffectSummaryOperations.Join(
            localSummary,
            _session.ResolveEntryPreconditions(method),
            CanTriggerOwnTypeInitialization(method) &&
            (!SymbolEqualityComparer.Default.Equals(
                method.ContainingAssembly,
                _compilation.Assembly)
                ? true
                : method.MethodKind == MethodKind.Constructor
                    ? method.ContainingType.StaticConstructors.All(
                          static constructor => constructor.IsImplicitlyDeclared)
                    : true) &&
            HasPotentialStaticInitialization(
                method.ContainingType,
                _session.ApiSpecs)
                ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
                : EffectSummary.Empty);
        return new EffectMethodNode(localSummary, [.. calls], scanner.DirectWitnesses);
    }

    private EffectStep ScanConstructorMemberInitializers(
        IMethodSymbol method,
        OperationEffectScanner scanner,
        CancellationToken cancellationToken)
    {
        var staticInitializers = method.MethodKind == MethodKind.StaticConstructor;
        if (!staticInitializers && method.MethodKind != MethodKind.Constructor)
        {
            return EffectStep.Empty;
        }

        var result = EffectStep.Empty;
        var write = EffectSummaryOperations.Write(EffectRegionSet.Create(
            staticInitializers ? EffectRegionId.Static() : EffectRegionId.Receiver));
        var syntaxTreeOrder = _compilation.SyntaxTrees
            .Select(static (tree, ordinal) => (tree, ordinal))
            .ToDictionary(
                static item => item.tree,
                static item => item.ordinal);
        var references = method.ContainingType.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared &&
                IsInitializableMember(member, staticInitializers))
            .SelectMany(static member => member.DeclaringSyntaxReferences)
            .OrderBy(reference => syntaxTreeOrder.TryGetValue(
                    reference.SyntaxTree,
                    out var ordinal)
                ? ordinal
                : int.MaxValue)
            .ThenBy(static reference => reference.Span.Start);
        foreach (var syntaxReference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = syntaxReference.GetSyntax(cancellationToken);
            var expression = EffectProjections.GetInitializerExpression(declaration);
            if (expression == null)
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, expression.SyntaxTree);
            var operation = model.GetOperation(expression, cancellationToken);
            if (operation == null)
            {
                result = result.Then(new EffectStep(
                    EffectSummaryOperations.Unsupported(),
                    true));
                continue;
            }

            result = result.Then(scanner.ScanSequence([operation]));
            if (!result.CompletesNormally)
            {
                break;
            }

            result = result.Then(new EffectStep(write, true));
        }

        return result;
    }

    internal static bool HasPotentialStaticInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs)
    {
        if (type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        if (type.SpecialType == SpecialType.System_Object &&
            HasApprovedSystemObjectConstructor(type, apiSpecs))
        {
            return false;
        }

        if (type.DeclaringSyntaxReferences.Length == 0)
        {
            return true;
        }

        var result = type.StaticConstructors.Any(static constructor =>
            constructor.DeclaringSyntaxReferences.Length != 0) ||
            type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: true) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                EffectProjections.GetInitializerExpression(reference.GetSyntax()) != null));
        return result;
    }

    internal static bool HasPotentialConstructionInitialization(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs)
    {
        const int maximumBaseTypeDepth = 256;
        var seen = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        INamedTypeSymbol? current = type;
        for (var depth = 0; current != null; depth++)
        {
            if (depth >= maximumBaseTypeDepth ||
                current.TypeKind == TypeKind.Error ||
                !seen.Add(current.OriginalDefinition) ||
                HasPotentialStaticInitialization(
                    current,
                    apiSpecs))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    internal static bool IsProvablyEmptyImplicitConstructorLayer(
        IMethodSymbol method,
        ResolvedApiSpecTable apiSpecs)
    {
        var type = method.ContainingType;
        return method.MethodKind == MethodKind.Constructor &&
        method.IsImplicitlyDeclared &&
        method.Parameters.IsDefaultOrEmpty &&
        type.DeclaringSyntaxReferences.Length != 0 &&
        !HasPotentialStaticInitialization(type, apiSpecs) &&
        !HasInstanceMemberInitializer(type);
    }

    internal static IMethodSymbol? GetUniqueParameterlessBaseConstructor(
        IMethodSymbol constructor)
    {
        var candidates = constructor.ContainingType.BaseType?
            .InstanceConstructors
            .Where(static candidate => candidate.Parameters.IsDefaultOrEmpty)
            .ToImmutableArray() ?? [];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool HasInstanceMemberInitializer(INamedTypeSymbol type)
    {
        return type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: false) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                EffectProjections.GetInitializerExpression(
                    reference.GetSyntax()) != null));
    }

    private static bool HasApprovedSystemObjectConstructor(
        INamedTypeSymbol type,
        ResolvedApiSpecTable apiSpecs)
    {
        return type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.IsDefaultOrEmpty &&
            apiSpecs.TryGet(constructor, out var spec) &&
            spec.Template.Target.WitnessIdentifier ==
            "bcl.object.ctor");
    }

    private static bool HasDefiniteBodyEntry(
        IMethodSymbol method,
        ResolvedApiSpecTable apiSpecs)
    {
        return method.MethodKind is not (
            MethodKind.Constructor or MethodKind.StaticConstructor) &&
        (!CanTriggerOwnTypeInitialization(method) ||
         !HasPotentialStaticInitialization(
             method.ContainingType,
             apiSpecs));
    }

    private static bool CanTriggerOwnTypeInitialization(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor ||
        method.MethodKind != MethodKind.StaticConstructor &&
        (method.IsStatic || method.ContainingType.IsValueType);
    }

    private static bool IsInitializableMember(
        ISymbol member,
        bool staticInitializers)
    {
        return member switch
        {
            IFieldSymbol field => !field.IsConst &&
                field.IsStatic == staticInitializers,
            IPropertySymbol property => property.IsStatic == staticInitializers,
            IEventSymbol @event => @event.IsStatic == staticInitializers,
            _ => false
        };
    }

    private static EffectSummary AnalyzeControlFlowGraph(
        ControlFlowGraph graph,
        OperationEffectScanner scanner)
    {
        var summary = EffectSummary.Empty;
        var pending = new SortedSet<int> { graph.Blocks[0].Ordinal };
        var exceptionalRegionOperations =
            CreateExceptionalRegionOperations(graph);
        var finallyEntries = CreateFinallyEntries(graph);
        foreach (var block in graph.Blocks.Where(static block =>
                     block.Predecessors.All(static predecessor =>
                         predecessor.Semantics !=
                             ControlFlowBranchSemantics.Regular)))
        {
            if (IsExceptionalEntryReachable(block))
            {
                pending.Add(block.Ordinal);
            }
        }

        var visited = new HashSet<int>();
        while (pending.Count != 0)
        {
            var ordinal = pending.Min;
            pending.Remove(ordinal);
            if (!visited.Add(ordinal))
            {
                continue;
            }

            var block = graph.Blocks[ordinal];
            var step = scanner.ScanSequence(
                block.Operations.Where(scanner.IsReachable));
            if (step.CompletesNormally &&
                block.BranchValue != null &&
                scanner.IsReachable(block.BranchValue))
            {
                step = step.Then(scanner.ScanSequence([block.BranchValue]));
            }

            summary = EffectSummaryOperations.Join(summary, step.Summary);
            if (!step.Summary.Throws.IsEmpty)
            {
                AddReachableFinallyEntriesForBlock(block);
            }
            AddControlTransferFinally(block, block.FallThroughSuccessor, step);
            AddControlTransferFinally(block, block.ConditionalSuccessor, step);
            if (!step.CompletesNormally)
            {
                continue;
            }

            AddRegularSuccessor(block.FallThroughSuccessor);
            AddRegularSuccessor(block.ConditionalSuccessor);
        }

        return ManagedAbstractFlow.IsAcyclic(graph)
            ? summary
            : EffectSummaryOperations.Join(
                summary,
                EffectSummaryOperations.MayDiverge());

        void AddRegularSuccessor(ControlFlowBranch? branch)
        {
            if (branch is
                {
                    Semantics: ControlFlowBranchSemantics.Regular,
                    Destination: { IsReachable: true } destination
                } && LeavingFinallysMayComplete(branch))
            {
                pending.Add(destination.Ordinal);
            }
        }

        bool LeavingFinallysMayComplete(ControlFlowBranch branch)
        {
            foreach (var region in branch.LeavingRegions)
            {
                if (finallyEntries.TryGetValue(region, out var entry) &&
                    entry.Operation is { } operation &&
                    !scanner.CanCompleteNormally(operation))
                {
                    return false;
                }
            }
            return true;
        }

        void AddControlTransferFinally(
            BasicBlock source,
            ControlFlowBranch? branch,
            EffectStep step)
        {
            if (branch == null ||
                !step.CompletesNormally &&
                branch.Semantics is not (
                    ControlFlowBranchSemantics.Throw or
                    ControlFlowBranchSemantics.Rethrow))
            {
                return;
            }
            if (branch.Semantics is
                ControlFlowBranchSemantics.Throw or
                ControlFlowBranchSemantics.Rethrow)
            {
                AddReachableFinallyEntriesForBlock(source);
                return;
            }
            AddReachableFinallyEntries(branch);
        }

        void AddReachableFinallyEntries(ControlFlowBranch branch)
        {
            foreach (var region in branch.LeavingRegions)
            {
                if (finallyEntries.TryGetValue(region, out var entry))
                {
                    pending.Add(entry.EntryOrdinal);
                    return;
                }
            }
        }

        bool IsExceptionalEntryReachable(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (region.Kind == ControlFlowRegionKind.Finally)
                {
                    return false;
                }
                if (exceptionalRegionOperations.TryGetValue(
                        region,
                        out var operation))
                {
                    return scanner.IsReachable(operation);
                }
            }
            return true;
        }

        void AddReachableFinallyEntriesForBlock(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (finallyEntries.TryGetValue(region, out var entry))
                {
                    pending.Add(entry.EntryOrdinal);
                    return;
                }
            }
        }
    }

    private readonly record struct FinallyEntry(
        int EntryOrdinal,
        IOperation? Operation);

    private static Dictionary<ControlFlowRegion, FinallyEntry> CreateFinallyEntries(
        ControlFlowGraph graph)
    {
        var regions = graph.Blocks
            .SelectMany(static block => EnclosingRegions(block.EnclosingRegion))
            .Distinct()
            .ToArray();
        var finallyRegions = regions
            .Where(static region =>
                region.Kind == ControlFlowRegionKind.Finally)
            .OrderBy(static region => region.FirstBlockOrdinal)
            .ToArray();
        var finallyOperations = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<ITryOperation>()
            .Where(@try =>
                @try.Finally != null &&
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    @try,
                    graph.OriginalOperation))
            .OrderBy(static @try => @try.Finally!.Syntax.SpanStart)
            .Select(static @try => (IOperation)@try.Finally!)
            .ToArray();
        var operationByRegion = new Dictionary<ControlFlowRegion, IOperation>();
        if (finallyRegions.Length == finallyOperations.Length)
        {
            for (var index = 0; index < finallyRegions.Length; index++)
            {
                operationByRegion.Add(
                    finallyRegions[index],
                    finallyOperations[index]);
            }
        }
        var result = new Dictionary<ControlFlowRegion, FinallyEntry>();
        foreach (var tryRegion in regions.Where(static region =>
                     region.Kind == ControlFlowRegionKind.Try))
        {
            var finallyRegion = regions.FirstOrDefault(region =>
                region.Kind == ControlFlowRegionKind.Finally &&
                ReferenceEquals(
                    region.EnclosingRegion,
                    tryRegion.EnclosingRegion));
            if (finallyRegion != null)
            {
                result.Add(
                    tryRegion,
                    new FinallyEntry(
                        finallyRegion.FirstBlockOrdinal,
                        operationByRegion.TryGetValue(
                            finallyRegion,
                            out var operation)
                                ? operation
                                : null));
            }
        }
        return result;

        static IEnumerable<ControlFlowRegion> EnclosingRegions(
            ControlFlowRegion? region)
        {
            for (; region != null; region = region.EnclosingRegion)
            {
                yield return region;
            }
        }
    }

    private static Dictionary<ControlFlowRegion, IOperation>
        CreateExceptionalRegionOperations(ControlFlowGraph graph)
    {
        var regions = graph.Blocks
            .SelectMany(static block => EnclosingRegions(block.EnclosingRegion))
            .Distinct()
            .ToArray();
        var catches = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<ICatchClauseOperation>()
            .Where(@catch =>
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    @catch,
                    graph.OriginalOperation))
            .OrderBy(static @catch => @catch.Syntax.SpanStart)
            .ToArray();
        var result = new Dictionary<ControlFlowRegion, IOperation>();
        AddMappings(
            regions.Where(static region =>
                    region.Kind == ControlFlowRegionKind.Catch)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray(),
            catches.Select(static @catch => @catch.Handler).ToArray());
        AddMappings(
            regions.Where(static region =>
                    region.Kind == ControlFlowRegionKind.Filter)
                .OrderBy(static region => region.FirstBlockOrdinal)
                .ToArray(),
            catches.Where(static @catch => @catch.Filter != null)
                .Select(static @catch => @catch.Filter!)
                .ToArray());
        return result;

        void AddMappings(
            ControlFlowRegion[] candidates,
            IOperation[] operations)
        {
            if (candidates.Length != operations.Length)
            {
                return;
            }
            for (var index = 0; index < candidates.Length; index++)
            {
                result.Add(candidates[index], operations[index]);
            }
        }

        static IEnumerable<ControlFlowRegion> EnclosingRegions(
            ControlFlowRegion? region)
        {
            for (; region != null; region = region.EnclosingRegion)
            {
                yield return region;
            }
        }
    }

    private IOperation? GetOperationRoot(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences
                     .OrderBy(
                         static reference => reference.SyntaxTree.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start))
        {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation is
                IMethodBodyOperation or
                IConstructorBodyOperation or
                IBlockOperation)
            {
                return operation;
            }

            foreach (var node in syntax.DescendantNodes())
            {
                operation = model.GetOperation(node, cancellationToken);
                if (operation is IMethodBodyOperation or IConstructorBodyOperation)
                {
                    return operation;
                }
            }
        }

        return null;
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        IOperation root,
        CancellationToken cancellationToken)
    {
        try
        {
            return root switch
            {
                IMethodBodyOperation method =>
                    ControlFlowGraph.Create(method, cancellationToken),
                IConstructorBodyOperation constructor =>
                    ControlFlowGraph.Create(constructor, cancellationToken),
                IBlockOperation { Parent: null } block =>
                    ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
