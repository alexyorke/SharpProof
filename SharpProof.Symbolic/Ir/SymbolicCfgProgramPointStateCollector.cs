using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicCfgProgramPointStateCollector
{
    internal static SymbolicLoweringResult<SymbolicState> CollectStraightLineState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(
            site,
            ExecutionRootPolicy.Callable);
        if (executionRoot == null)
            return Unsupported(site, "execution-root");

        ControlFlowGraph? graph;
        try
        {
            graph = ControlFlowGraph.Create(executionRoot, semanticModel, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unsupported(site, "cfg");
        }

        if (graph == null || graph.Blocks.IsDefaultOrEmpty)
            return Unsupported(site, "cfg-empty");

        var state = initialState ?? new SymbolicState();
        SymbolicStatementStateTransfer.AddMethodEntryNullableFlowStateFacts(
            ref state,
            site,
            semanticModel,
            cancellationToken);

        var visited = new HashSet<int>();
        var block = graph.Blocks[0];
        while (visited.Add(block.Ordinal))
        {
            foreach (var operation in block.Operations)
            {
                if (ContainsSite(operation.Syntax, site))
                    return Exact(state, site);
                if (operation.Syntax.SpanStart >= site.SpanStart)
                    return Unsupported(site, "operation-order");
                if (!TryApplyOperation(ref state, operation, semanticModel, cancellationToken))
                    return Unsupported(operation.Syntax, "operation-" + operation.Kind);
            }

            if (block.BranchValue != null)
            {
                if (ContainsSite(block.BranchValue.Syntax, site))
                    return Exact(state, site);
                if (block.BranchValue.Syntax.SpanStart < site.SpanStart)
                    return Unsupported(block.BranchValue.Syntax, "branch");
            }

            var successor = GetSingleSuccessor(block);
            if (successor == null)
                return Unsupported(site, "target-block");
            block = successor;
        }

        return Unsupported(site, "cycle");
    }

    private static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (operation is IVariableDeclarationGroupOperation declarations)
        {
            foreach (var declarator in declarations.Declarations
                         .SelectMany(static declaration => declaration.Declarators))
            {
                if (declarator.Initializer?.Value is not { } value ||
                    !TryApplyAssignment(
                        ref state,
                        declarator.Symbol,
                        value,
                        semanticModel,
                        cancellationToken,
                        "operation-lowering.declaration"))
                    return false;
            }

            return true;
        }

        var assignment = operation switch
        {
            IExpressionStatementOperation { Operation: ISimpleAssignmentOperation nested } => nested,
            ISimpleAssignmentOperation direct => direct,
            _ => null
        };
        if (assignment != null)
            return TryGetDirectTarget(assignment.Target, out var target) &&
                   TryApplyAssignment(
                       ref state,
                       target,
                       assignment.Value,
                       semanticModel,
                       cancellationToken,
                       "operation-lowering.assignment");

        return false;
    }

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        ISymbol target,
        IOperation value,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance)
    {
        if (value.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression ||
            SymbolMutationFacts.ExpressionReferencesSymbol(
                expression,
                target,
                semanticModel,
                cancellationToken))
            return false;

        var transition = SymbolicOperationTransferAdapter.ApplyAssignment(
            state,
            target,
            expression,
            semanticModel,
            cancellationToken,
            provenance: provenance,
            bindingProvenance: provenance + ".assigned-value",
            asExpressionProvenanceRoot: provenance + ".as",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);
        if (!transition.IsExact)
            return false;

        state = transition.State;
        return true;
    }

    private static bool TryGetDirectTarget(IOperation operation, out ISymbol target)
    {
        target = operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null!
        };
        return target != null;
    }

    private static BasicBlock? GetSingleSuccessor(BasicBlock block)
    {
        var fallThrough = block.FallThroughSuccessor?.Destination;
        var conditional = block.ConditionalSuccessor?.Destination;
        if (fallThrough == null)
            return conditional;
        return conditional == null || ReferenceEquals(fallThrough, conditional)
            ? fallThrough
            : null;
    }

    private static bool ContainsSite(SyntaxNode container, SyntaxNode site) =>
        container.Span.Contains(site.SpanStart) || site.Span.Contains(container.SpanStart);

    private static SymbolicLoweringResult<SymbolicState> Exact(
        SymbolicState state,
        SyntaxNode site) =>
        SymbolicLoweringResult<SymbolicState>.Exact(
            state.Normalize(),
            Provenance(site, "exact"));

    private static SymbolicLoweringResult<SymbolicState> Unsupported(
        SyntaxNode site,
        string detail) =>
        SymbolicLoweringResult<SymbolicState>.Unsupported(Provenance(site, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode site, string detail) =>
        new("cfg-program-point", site.Span, detail);
}
