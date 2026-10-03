using SharpProof.Dataflow;
using SharpProof.Frontend.Host;
using SharpProof.Roslyn;

namespace SharpProof.Analyzer;

internal sealed record AdvisoryRequiresSite(string CalleeIdentity, int ClauseOrdinal, OperationId ClauseSite,
    IrAssignInstruction Marker, IntervalValue Condition, bool PrefixHasGap)
{
    internal bool MayViolate => Condition.Contains(0);
}
internal sealed record AdvisoryCallAnalysis(ImmutableArray<AdvisoryRequiresSite> Calls, ImmutableArray<string> Gaps,
    bool Enabled, IrProgram? Program = null)
{
    internal static AdvisoryCallAnalysis Disabled { get; } = new([], [], false);
}

// Optional source observation. These rows are not proof outcomes.
// This optional observer does not publish diagnostics.
internal static class AdvisoryCallConsumer
{
    internal static AdvisoryCallAnalysis Analyze(CSharpCompilation compilation, MethodDeclarationSyntax declaration,
        bool enabled, CancellationToken token = default)
    {
        if (!enabled)
        {
            return AdvisoryCallAnalysis.Disabled;
        }
        token.ThrowIfCancellationRequested();
        if (compilation.References.Any(reference => reference is CompilationReference))
        {
            return Gap("UnsupportedSourceReference");
        }
        var remainingSyntax = 32768;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var pending = new Stack<(SyntaxNode Node, int Depth)>();
            if (--remainingSyntax < 0)
            {
                return Gap("SyntaxBudget");
            }
            pending.Push((tree.GetRoot(token), 0));
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var (node, depth) = pending.Pop();
                if (depth > 128)
                {
                    return Gap("SyntaxBudget");
                }
                if (node is AttributeListSyntax or FieldDeclarationSyntax or ConstructorDeclarationSyntax)
                {
                    return Gap("UnsupportedInitializationOrAttributedSource");
                }
                foreach (var child in node.ChildNodes())
                {
                    token.ThrowIfCancellationRequested();
                    if (--remainingSyntax < 0)
                    {
                        return Gap("SyntaxBudget");
                    }
                    pending.Push((child, depth + 1));
                }
            }
        }
        if (!compilation.SyntaxTrees.Contains(declaration.SyntaxTree))
        {
            return Gap("SourceOwnership");
        }
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        if (model.GetDeclaredSymbol(declaration, token) is not IMethodSymbol owner || !Admit(owner))
        {
            return Gap("UnsupportedOwner");
        }
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), owner, tree => tree.FilePath);
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        if (!binding.IsSuccess || !binding.Clauses.IsEmpty)
        {
            return Gap("UnsupportedOwnContracts");
        }
        var graph = ControlFlowGraph.Create(declaration, model, token);
        if (graph == null)
        {
            return Gap("UnavailableFlow");
        }
        var emission = new InvocationEmissionPolicy(compilation);
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context, frame =>
        {
            token.ThrowIfCancellationRequested();
            if (!Admit(frame.Target) || frame.Target.DeclaringSyntaxReferences.Length != 1 ||
                !compilation.SyntaxTrees.Contains(frame.Target.DeclaringSyntaxReferences[0].SyntaxTree))
            {
                return false;
            }
            var contracts = new ContractBinder(compilation, context.Factory).BindTotalRequires(frame);
            if (!contracts.IsSuccess)
            {
                return false;
            }
            frame.SourceCallPreconditions = [.. contracts.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires)
                .Select(clause => new TotalSourcePrecondition(clause.Value, clause.SafeCondition, clause.SourceOperation))];
            var syntax = frame.Target.DeclaringSyntaxReferences[0].GetSyntax(token);
            var operation = CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree).GetOperation(syntax, token);
            if (operation == null)
            {
                return false;
            }
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            var remaining = 4096;
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                if (--remaining < 0)
                {
                    return false;
                }
                var current = pending.Pop();
                if (current is IInvocationOperation invocation && frame.IsSpecificationOperation(current))
                {
                    if (!emission.IsElided(current))
                    {
                        frame.RestoreSpecificationCall(invocation);
                    }
                    continue;
                }
                foreach (var child in current.ChildOperations)
                {
                    pending.Push(child);
                }
            }
            frame.DiscardSpecificationAssumptions();
            return true;
        }, token);
        token.ThrowIfCancellationRequested();
        if (!lowering.IsExact)
        {
            return Gap("UnsupportedLowering");
        }
        if (lowering.Program.Blocks.Length > 4096)
        {
            return Gap("GraphBudget");
        }
        var instructionBudget = 4096;
        var variables = new HashSet<IrVarId>();
        var terms = new Stack<IrTerm>();
        foreach (var block in lowering.Program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                token.ThrowIfCancellationRequested();
                if (--instructionBudget < 0)
                {
                    return Gap("GraphBudget");
                }
                variables.UnionWith(IrInstructionFacts.WrittenVariables(instruction));
                foreach (var term in IrInstructionFacts.ReadTerms(instruction))
                {
                    terms.Push(term);
                }
            }
        }
        var visited = new HashSet<IrId>();
        var work = 65536;
        while (terms.Count != 0)
        {
            token.ThrowIfCancellationRequested();
            if (--work < 0)
            {
                return Gap("TermInventoryBudget");
            }
            var term = terms.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }
            if (term is IrVariableTerm variable)
            {
                variables.Add(variable.Variable);
            }
            IrTraversal.PushChildren(term, terms);
        }
        var facts = new CoreIrAdvisoryInterpreter(lowering.Program, [.. variables.OrderBy(variable => variable.Value)])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, token: token, markers: [.. lowering.CallPreconditions.Keys]);
        token.ThrowIfCancellationRequested();
        if (!facts.Accepted)
        {
            return new([], facts.Gaps, true);
        }
        var calls = facts.Markers.Select(snapshot =>
        {
            var clause = lowering.CallPreconditions[snapshot.Marker];
            return new AdvisoryRequiresSite(clause.CalleeIdentity, clause.ClauseOrdinal, clause.ClauseSite,
                snapshot.Marker, snapshot.Value, snapshot.PrefixHasGap);
        }).ToImmutableArray();
        return new(calls, facts.Gaps, true, lowering.Program);
    }

    private static AdvisoryCallAnalysis Gap(string reason)
    {
        return new([], [reason], true);
    }
    private static bool Admit(IMethodSymbol method)
    {
        return method.IsStatic && method.MethodKind == MethodKind.Ordinary && !method.IsAsync && method.Arity == 0 &&
            !method.ContainingType.IsGenericType && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
            method.Parameters.All(parameter => parameter.RefKind == RefKind.None && Scalar(parameter.Type)) &&
            (method.ReturnsVoid || Scalar(method.ReturnType));
    }
    private static bool Scalar(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64;
    }
}
