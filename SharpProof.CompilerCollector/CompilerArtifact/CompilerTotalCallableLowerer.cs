namespace SharpProof.CompilerArtifact;

// Independent admission for the same discovered target. A legacy failure is
// preserved and does not suppress exact typed candidate evidence.
internal static class CompilerTotalCallableLowerer
{
    internal static CompilerTotalCallablePreparation? Prepare(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Declaration is not MethodDeclarationSyntax declaration || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerPreparedBody.MaximumInstructions)
        { return null; }
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: capturedTrees[ordinal].Path))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => documents[tree]);
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        cancellationToken.ThrowIfCancellationRequested();
        // Source Assume is erased today; keep it closed until original replay
        // carries its point filter and conditional proof metadata.
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerPreparedBody.MaximumInstructions ||
            binding.Clauses.Any(clause => clause.Kind == BoundContractKind.Assume) ||
            target.Entry.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume))
        { return null; }
        var ensures = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Ensures).ToArray();
        var requires = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires).ToArray();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        if (ensures.Length != target.Claims.Length || requires.Length != preconditions.Length)
        { return null; }
        for (var ordinal = 0; ordinal < ensures.Length; ordinal++)
        {
            var claim = target.Claims[ordinal];
            var span = context.Factory.GetOperationInfo(ensures[ordinal].SourceOperation).SourceSpan;
            if (claim.Entry.Kind != WorkerClaimKind.Postcondition || claim.Entry.Evidence != WorkerClaimEvidence.DirectClause ||
                claim.Entry.Ordinal != ordinal || claim.SourceOperation == null || span == null ||
                claim.SourceOperation.Syntax.SpanStart != span.Start || claim.SourceOperation.Syntax.Span.Length != span.Length ||
                documents[claim.SourceOperation.Syntax.SyntaxTree] != span.Document)
            { return null; }
        }
        ControlFlowGraph? graph;
        try
        { graph = ControlFlowGraph.Create(declaration, target.SemanticModel, cancellationToken); }
        catch (ArgumentException)
        { return null; }
        if (graph == null)
        { return null; }
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerPreparedBody.MaximumInstructions)
        { return null; }
        var instructionCount = 0;
        foreach (var block in lowering.Program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Instructions.Length > CompilerPreparedBody.MaximumInstructions - instructionCount)
            { return null; }
            instructionCount += block.Instructions.Length;
        }
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        return new(target.Entry.CallableId, lowering.Program,
            [.. context.Parameters.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result,
            [.. binding.Clauses.Select(clause => new CompilerTotalClause(CompilerLoweringWireMappings.ToCompiler(clause.Kind),
                clause.Value, clause.SafeCondition, clause.SourceOperation,
                clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
                clause.Kind == BoundContractKind.Requires ? preconditions[assumptionOrdinal++].Id : null))]);
    }
}
