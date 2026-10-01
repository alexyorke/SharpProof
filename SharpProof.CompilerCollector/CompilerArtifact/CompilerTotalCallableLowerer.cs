namespace SharpProof.CompilerArtifact;

// Independent admission for the same discovered target. A legacy failure is
// preserved and does not suppress exact typed candidate evidence.
internal static class CompilerTotalCallableLowerer
{
    internal static CompilerTotalEntryPreparation? PrepareEntry(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Declaration is not MethodDeclarationSyntax || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerPreparedBody.MaximumInstructions)
        { return null; }
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: capturedTrees[ordinal].Path))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => documents[tree]);
        var binding = new ContractBinder(compilation, context.Factory).BindTotalRequires(context);
        cancellationToken.ThrowIfCancellationRequested();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerPreparedBody.MaximumInstructions ||
            binding.Clauses.Length != preconditions.Length)
        { return null; }
        return new(target.Entry.CallableId, context.Factory,
            [.. context.Parameters.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            [.. binding.Clauses.Select((clause, ordinal) => new CompilerTotalClause(CompilerContractKind.Requires,
                clause.Value, clause.SafeCondition, clause.SourceOperation, null, preconditions[ordinal].Id))]);
    }

    internal static CompilerTotalCallablePreparation? Prepare(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CancellationToken cancellationToken)
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
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerPreparedBody.MaximumInstructions)
        { return null; }
        var ensures = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Ensures).ToArray();
        var requires = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires).ToArray();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        var assumptions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        if (ensures.Length != target.Claims.Length || requires.Length != preconditions.Length ||
            binding.Clauses.Count(clause => clause.Kind == BoundContractKind.Assume) != assumptions.Length)
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
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context, frame =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contracts = new ContractBinder(compilation, context.Factory).BindTotal(frame);
            return contracts.IsSuccess && contracts.Clauses.All(clause => clause.Kind != BoundContractKind.Assume);
        }, new CompilerTotalIlBodyProvider(compilation, capturedReferences).Resolve, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var program = lowering.Program;
        var isBodyAbstraction = false;
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerPreparedBody.MaximumInstructions)
        {
            if (lowering.ConstructionLimitExceeded || graph.Blocks.Length > CompilerPreparedBody.MaximumInstructions ||
                graph.Blocks.Sum(block => block.Operations.Length) > CompilerPreparedBody.MaximumInstructions ||
                binding.Clauses.Any(clause => clause.Kind == BoundContractKind.Assume) ||
                context.Parameters.Any(parameter => !Primitive(parameter.Entry)) ||
                context.Result is { } resultVariable && !Primitive(resultVariable) ||
                context.Parameters.Length * 2 + 2 > CompilerPreparedBody.MaximumInstructions)
            { return null; }
            var builder = new IrProgramBuilder(context.Factory);
            var block = builder.CreateBlock();
            var site = context.Site(target.SemanticModel.GetOperation(declaration, cancellationToken)!);
            foreach (var parameter in context.Parameters)
            {
                builder.Assign(block, site, parameter.Current, context.Factory.Variable(parameter.Entry));
                builder.Assign(block, site, parameter.PreState, context.Factory.Variable(parameter.Entry));
            }
            var mutable = context.Parameters.Select(parameter => parameter.Current)
                .Concat(context.Result is { } resultId ? [resultId] : Array.Empty<IrVarId>()).ToArray();
            if (mutable.Length == 0)
            { mutable = [context.Factory.CreateVariable("abstract-body", context.Factory.BooleanType)]; }
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, mutable);
            builder.Return(block, site, context.Result is { } returned ? context.Factory.Variable(returned) : null);
            program = builder.Build();
            isBodyAbstraction = true;

            bool Primitive(IrVarId variable)
            { return context.Factory.GetTypeInfo(context.Factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.Boolean or IrTypeKind.Integer; }
        }
        var instructionCount = 0;
        foreach (var block in program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Instructions.Length > CompilerPreparedBody.MaximumInstructions - instructionCount)
            { return null; }
            instructionCount += block.Instructions.Length;
        }
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        return new(target.Entry.CallableId, program,
            [.. context.Parameters.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result,
            [.. binding.Clauses.Select(clause => new CompilerTotalClause(CompilerLoweringWireMappings.ToCompiler(clause.Kind),
                clause.Value, clause.SafeCondition, clause.SourceOperation,
                clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
                clause.Kind == BoundContractKind.Requires ? preconditions[assumptionOrdinal++].Id :
                    clause.Kind == BoundContractKind.Assume ? assumptions[userAssumptionOrdinal++].Id : null))], isBodyAbstraction);
    }
}
