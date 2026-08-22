using Microsoft.CodeAnalysis.CSharp.Syntax;

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
                scanner.ScanUsingDisposalEffects(root));
        }

        localSummary = EffectSummaryOperations.Join(
            localSummary,
            _session.ResolveEntryPreconditions(method),
            CanTriggerOwnTypeInitialization(method) &&
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

        return type.StaticConstructors.Any() ||
        type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: true) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                EffectProjections.GetInitializerExpression(reference.GetSyntax()) != null));
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
        foreach (var block in graph.Blocks.Where(static block => block.IsReachable))
        {
            var step = scanner.ScanSequence(
                block.Operations.Where(scanner.IsReachable));
            if (step.CompletesNormally &&
                block.BranchValue != null &&
                scanner.IsReachable(block.BranchValue))
            {
                step = step.Then(scanner.ScanSequence([block.BranchValue]));
            }

            summary = EffectSummaryOperations.Join(summary, step.Summary);
        }

        return ManagedAbstractFlow.IsAcyclic(graph)
            ? summary
            : EffectSummaryOperations.Join(
                summary,
                EffectSummaryOperations.MayDiverge());
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
