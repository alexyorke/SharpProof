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
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken)
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
            var syntax = claim.SourceOperation?.Syntax ?? claim.SourceAttribute?.ApplicationSyntaxReference?.GetSyntax(cancellationToken);
            if (claim.Entry.Kind != WorkerClaimKind.Postcondition ||
                claim.Entry.Evidence != CompilerLoweringWireMappings.ToWorkerEvidence(ensures[ordinal].Evidence) ||
                claim.Entry.Ordinal != ordinal || syntax == null || span == null ||
                syntax.SpanStart != span.Start || syntax.Span.Length != span.Length ||
                documents[syntax.SyntaxTree] != span.Document)
            { return null; }
        }
        ControlFlowGraph? graph;
        try
        { graph = ControlFlowGraph.Create(declaration, target.SemanticModel, cancellationToken); }
        catch (ArgumentException)
        { return null; }
        if (graph == null)
        { return null; }
        var apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        var specificationPacks = new CompilerSpecificationPackProvider(context.Factory, specificationPackAuthority);
        var invocationEmission = new InvocationEmissionPolicy(compilation);
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context, frame =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contracts = new ContractBinder(compilation, context.Factory).BindTotalRequires(frame);
            if (!contracts.IsSuccess || frame.Target.DeclaringSyntaxReferences.Length != 1)
            { return false; }
            frame.SourceCallPreconditions = [.. contracts.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires)
                .Select(clause => new TotalSourcePrecondition(clause.Value, clause.SafeCondition, clause.SourceOperation))];
            var syntax = frame.Target.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var operation = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree)
                .GetOperation(syntax, cancellationToken);
            if (operation == null)
            { return false; }
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            var remaining = CompilerPreparedBody.MaximumInstructions;
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return false; }
                var current = pending.Pop();
                if (current is IInvocationOperation invocation && frame.IsSpecificationOperation(current))
                {
                    if (!invocationEmission.IsElided(current))
                    { frame.RestoreSpecificationCall(invocation); }
                    // Emitted calls and their arguments use ordinary body
                    // lowering. Elided arguments do not execute. Neither case
                    // imports callee proof assumptions into the caller.
                    continue;
                }
                foreach (var child in current.ChildOperations)
                { pending.Push(child); }
            }
            frame.DiscardSpecificationAssumptions();
            return true;
        }, new CompilerTotalIlBodyProvider(compilation, capturedReferences).Resolve, cancellationToken,
            method => ResolveScalarModel(method, context.Factory, apiSpecs, specificationPacks));
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
                    clause.Kind == BoundContractKind.Assume ? assumptions[userAssumptionOrdinal++].Id : null))], isBodyAbstraction)
        {
            EffectsCompleteAtEntry = HasNoEffectEntryInitialization(compilation, target.Method.ContainingType, cancellationToken),
            ValidEffectClaimIds = [.. target.EffectClaims.Where(claim => claim.HasValidConstraint)
                .Select(claim => claim.Evidence.ClaimId).OrderBy(id => id, StringComparer.Ordinal)],
            ExceptionConstraints = ExceptionConstraints(compilation, target, cancellationToken),
            CallPreconditions = isBodyAbstraction ? [] : [.. lowering.CallPreconditions.OrderBy(pair => pair.Key.Id.Value)
                .Select(pair => new CompilerTotalCallPrecondition(pair.Key.Id, pair.Value.CalleeIdentity,
                    pair.Value.ClauseOrdinal, pair.Value.ClauseSite, pair.Value.Value, pair.Value.Safe))]
        };
    }

    internal static TotalScalarCallModel? ResolveScalarModel(IMethodSymbol method, IrFactory factory,
        ResolvedApiSpecTable apiSpecs, CompilerSpecificationPackProvider specificationPacks)
    {
        if (apiSpecs.TryGet(method, out var spec))
        {
            switch (spec.Template.Target.DocumentationCommentId)
            {
                case "M:System.Math.Abs(System.Int32)":
                    return new TotalScalarCallModel(1, arguments => CSharpOperationSemantics.Int32MathAbs(factory, arguments[0]));
                case "M:System.Array.Empty``1" when CSharpOperationSemantics.IsReferenceDomain(method.ReturnType):
                    return new TotalScalarCallModel(0, _ => CSharpOperationSemantics.ArrayEmpty(factory,
                        new RoslynTypeMapper(factory).GetTypeId(method.ReturnType)));
                case "M:System.String.Concat(System.String,System.String)":
                    return new TotalScalarCallModel(2, arguments => CSharpOperationSemantics.StringConcat(factory,
                        arguments[0], arguments[1]), stringConcatenation: true);
            }
        }
        return specificationPacks.ResolveTotal(method);
    }

    internal static bool HasNoEffectEntryInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.StaticConstructors.Length != 0)
            { return false; }
        }
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        var remaining = CompilerPreparedBody.MaximumInstructions;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in pending.Pop().GetMembers())
            {
                if (--remaining < 0)
                { return false; }
                if (member is INamespaceOrTypeSymbol nested)
                { pending.Push(nested); }
                if (member is IMethodSymbol method && method.GetAttributes().Any(attribute =>
                        attribute.AttributeClass is { Name: "ModuleInitializerAttribute", ContainingNamespace: { } ns } &&
                        CompilerMetadataResolution.HasNamespace(ns, "System", "Runtime", "CompilerServices")))
                { return false; }
            }
        }
        return true;
    }

    private static ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints(CSharpCompilation compilation,
        ManifestCallableTarget target, CancellationToken cancellationToken)
    {
        var constraints = ImmutableArray.CreateBuilder<CompilerTotalExceptionConstraint>();
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        foreach (var claim in target.EffectClaims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = claim.Evidence;
            if (evidence.ContractKind is not (WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions) ||
                !claim.HasValidConstraint)
            { continue; }
            var allowed = ImmutableArray.CreateBuilder<IrExceptionKind>();
            foreach (var kind in (IrExceptionKind[])Enum.GetValues(typeof(IrExceptionKind)))
            {
                var runtime = core.GetTypeByMetadataName(CSharpOperationSemantics.ExceptionMetadataName(kind));
                if (runtime == null)
                { return []; }
                if (evidence.ContractKind == WorkerEffectContractKind.AllowedExceptions &&
                    CompilerExceptionTypeIdentity.EncodeHierarchy(runtime).Any(identity =>
                        evidence.Constraint.AllowedExceptionTypes.Contains(identity, StringComparer.Ordinal)))
                { allowed.Add(kind); }
            }
            constraints.Add(new(evidence.ClaimId, allowed.ToImmutable()));
        }
        return constraints.ToImmutable();
    }
}
