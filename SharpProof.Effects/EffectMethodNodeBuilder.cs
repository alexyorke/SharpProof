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
            allowDirectWitnesses: graph != null);
        var localSummary = graph == null
            ? EffectSummaryOperations.Join(
                scanner.Scan(root),
                EffectSummaryOperations.Unsupported())
            : AnalyzeControlFlowGraph(graph, scanner);

        // Cyclic scalar flow does not invalidate the conservative all-block effect scan.
        if (abstractAnalysis is
            {
                IsComplete: false,
                IncompleteReason: not EffectAnalysisIncompleteReason.CyclicControlFlow
            })
        {
            localSummary = EffectSummaryOperations.Join(
                localSummary,
                EffectSummaryOperations.IncompleteAnalysis(
                    abstractAnalysis.IncompleteReason));
        }

        localSummary = EffectSummaryOperations.Join(
            localSummary,
            _session.ResolveEntryPreconditions(method),
            scanner.ScanLexicalControlEffects(root),
            ScanConstructorMemberInitializers(method, scanner, cancellationToken),
            CanTriggerOwnTypeInitialization(method) &&
            HasPotentialStaticInitialization(method.ContainingType)
                ? EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall)
                : EffectSummary.Empty);
        return new EffectMethodNode(localSummary, [.. calls], scanner.DirectWitnesses);
    }

    private EffectSummary ScanConstructorMemberInitializers(
        IMethodSymbol method,
        OperationEffectScanner scanner,
        CancellationToken cancellationToken)
    {
        var staticInitializers = method.MethodKind == MethodKind.StaticConstructor;
        if (!staticInitializers && method.MethodKind != MethodKind.Constructor)
        {
            return EffectSummary.Empty;
        }

        var summary = EffectSummary.Empty;
        var write = EffectSummaryOperations.Write(EffectRegionSet.Create(
            staticInitializers ? EffectRegionId.Static() : EffectRegionId.Receiver));
        foreach (var member in method.ContainingType.GetMembers()
                     .Where(member => !member.IsImplicitlyDeclared &&
                         IsInitializableMember(member, staticInitializers))
                     .OrderBy(static member => member.MetadataName, StringComparer.Ordinal))
        {
            foreach (var syntaxReference in member.DeclaringSyntaxReferences
                         .OrderBy(
                             static reference => reference.SyntaxTree.FilePath,
                             StringComparer.Ordinal)
                         .ThenBy(static reference => reference.Span.Start))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var expression = GetInitializerExpression(declaration);
                if (expression == null)
                {
                    continue;
                }

                var model = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, expression.SyntaxTree);
                var operation = model.GetOperation(expression, cancellationToken);
                summary = EffectSummaryDomain.Instance.Join(
                    summary,
                    operation == null
                        ? EffectSummaryOperations.Unsupported()
                        : EffectSummaryOperations.Join(scanner.Scan(operation), write));
            }
        }

        return summary;
    }

    internal static bool HasPotentialStaticInitialization(INamedTypeSymbol type)
    {
        return type.StaticConstructors.Any(
            static constructor => !constructor.IsImplicitlyDeclared) ||
        type.GetMembers().Any(member =>
            !member.IsImplicitlyDeclared &&
            IsInitializableMember(member, staticInitializers: true) &&
            member.DeclaringSyntaxReferences.Any(reference =>
                GetInitializerExpression(reference.GetSyntax()) != null));
    }

    private static bool CanTriggerOwnTypeInitialization(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor ||
        method.IsStatic && method.MethodKind != MethodKind.StaticConstructor;
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

    private static ExpressionSyntax? GetInitializerExpression(SyntaxNode declaration)
    {
        return declaration switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer?.Value,
            PropertyDeclarationSyntax property => property.Initializer?.Value,
            _ => null
        };
    }

    private static EffectSummary AnalyzeControlFlowGraph(
        ControlFlowGraph graph,
        OperationEffectScanner scanner)
    {
        var summary = EffectSummary.Empty;
        foreach (var block in graph.Blocks.Where(static block => block.IsReachable))
        {
            summary = EffectSummaryOperations.JoinFrom(
                summary,
                block.Operations.Where(scanner.IsReachable).Select(scanner.Scan));
            if (block.BranchValue != null && scanner.IsReachable(block.BranchValue))
            {
                summary = EffectSummaryOperations.Join(
                    summary,
                    scanner.Scan(block.BranchValue));
            }
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
