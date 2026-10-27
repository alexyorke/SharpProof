namespace SharpProof.CompilerCollector;

internal static class CompilerForkJoinResourceLowering
{
    internal const string ModelId = "fork-join-source-child-projection-v1";

    internal static CompilerForkJoinProjectionResult Lower(IMethodSymbol method, CSharpCompilation compilation,
        string callableId, CancellationToken cancellationToken)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        callableId = RepositoryParameterGuard.NotNull(callableId, nameof(callableId));
        if (string.IsNullOrWhiteSpace(callableId))
        { throw new ArgumentException("A callable identity is required.", nameof(callableId)); }
        cancellationToken.ThrowIfCancellationRequested();
        var plan = ResourceForkJoinPlan.TryCreate(method, compilation, cancellationToken);
        if (plan == null)
        { return new(null, "ForkJoinShapeUnsupported"); }
        if (!CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(compilation, method.ContainingType, cancellationToken))
        { return new(null, "ForkJoinEntryInitializationUnknown"); }
        var children = ImmutableArray.CreateBuilder<CompilerForkJoinChild>(plan.Children.Length);
        var remaining = TotalBodyLowering.MaximumWork;
        foreach (var invocation in plan.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = invocation.TargetMethod;
            if (!CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(compilation, child.ContainingType, cancellationToken))
            { return new(null, "ForkJoinEntryInitializationUnknown"); }
            var declaration = (MethodDeclarationSyntax)child.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
            var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), child)
            { CaptureResourceLoopEntries = true };
            var contracts = new ContractBinder(compilation, context.Factory).BindTotal(context, cancellationToken: cancellationToken);
            if (!contracts.IsSuccess || !contracts.Clauses.IsEmpty)
            { return new(null, "ForkJoinChildContractsUnsupported"); }
            var failure = ReadChildBody(declaration, child, model, childBody =>
            {
                childBody = RepositoryParameterGuard.NotNull(childBody, nameof(childBody));
                var lowered = TotalBodyLowering.Lower(compilation, childBody.Graph, context, cancellationToken);
                if (!lowered.IsExact || lowered.ConstructionLimitExceeded || !lowered.SourceInitializationEffectsComplete)
                { return new(null, "ForkJoinChildLoweringIncomplete"); }
                if (!lowered.CallPreconditions.IsEmpty)
                { return new(null, "ForkJoinChildContractsUnsupported"); }
                foreach (var block in lowered.Program.Blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    remaining -= block.Instructions.Length;
                    if (remaining < 0)
                    { return new(null, "ForkJoinProjectionBudgetExceeded"); }
                    foreach (var instruction in block.Instructions)
                    {
                        if (instruction is IrCallInstruction or IrStoreInstruction ||
                            instruction is IrWriteInstruction { Region: not (IrWriteRegion.Local or IrWriteRegion.Parameter) } ||
                            instruction is IrHavocInstruction { HavocKind: not IrHavocKind.Variables })
                        { return new(null, "ForkJoinSharedEffectsUnknown"); }
                    }
                }
                var body = declaration.Body is { } blockBody ? model.GetOperation(blockBody, cancellationToken) :
                    model.GetOperation(declaration.ExpressionBody!.Expression, cancellationToken);
                var cost = ResourceBoundsAnalysis.Analyze(child, body, cancellationToken);
                if (cost.Bounds == null)
                { return new(null, "ForkJoinChildCostUnknown:" + cost.UnknownReason); }
                var arguments = ImmutableArray.CreateBuilder<CompilerForkJoinArgument>(invocation.Arguments.Length);
                foreach (var argument in invocation.Arguments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parameter = argument.Parameter!;
                    arguments.Add(argument.Value is IParameterReferenceOperation root
                        ? new(parameter.Ordinal, root.Parameter.Ordinal, null)
                        : new(parameter.Ordinal, null, (int)argument.Value.ConstantValue.Value!));
                }
                var owner = new CompilerTotalCallablePreparation(
                    callableId + ":fork-child:" + children.Count.ToString(CultureInfo.InvariantCulture),
                    lowered.Program,
                    [.. context.Parameters.Select(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                        new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))], null, [])
                { EffectsCompleteAtEntry = true };
                children.Add(new(owner, cost.Bounds.Program, arguments.MoveToImmutable()));
                return null;
            }, cancellationToken);
            if (failure != null)
            { return failure; }
        }
        return new(new(callableId, plan.Join.TargetMethod.ContainingAssembly.Identity.ToString(),
            [.. method.Parameters.Select(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.Name)], children.MoveToImmutable())
        { ConfiguredAwait = plan.ConfiguredAwait }, null);
    }

    internal sealed record ChildBody(MethodDeclarationSyntax Declaration, SemanticModel Model, ControlFlowGraph Graph)
    {
        public MethodDeclarationSyntax Declaration { get; init; } = RepositoryParameterGuard.NotNull(Declaration, nameof(Declaration));
        public SemanticModel Model { get; init; } = RepositoryParameterGuard.NotNull(Model, nameof(Model));
        public ControlFlowGraph Graph { get; init; } = RepositoryParameterGuard.NotNull(Graph, nameof(Graph));
    }

    // Recognition admits source children before this operation boundary. Keep
    // declaration, model and CFG together so no consumer lowers a missing body.
    internal static CompilerForkJoinProjectionResult? ReadChildBody(CSharpCompilation compilation,
        IMethodSymbol child, Func<ChildBody, CompilerForkJoinProjectionResult?> continuation, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        child = RepositoryParameterGuard.NotNull(child, nameof(child));
        continuation = RepositoryParameterGuard.NotNull(continuation, nameof(continuation));
        cancellationToken.ThrowIfCancellationRequested();
        var declaration = (MethodDeclarationSyntax)child.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        return ReadChildBody(declaration, child, model, continuation, cancellationToken);
    }

    private static CompilerForkJoinProjectionResult? ReadChildBody(MethodDeclarationSyntax declaration,
        IMethodSymbol child, SemanticModel model, Func<ChildBody, CompilerForkJoinProjectionResult?> continuation,
        CancellationToken cancellationToken)
    {
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        child = RepositoryParameterGuard.NotNull(child, nameof(child));
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        continuation = RepositoryParameterGuard.NotNull(continuation, nameof(continuation));
        cancellationToken.ThrowIfCancellationRequested();
        var graph = TotalSourceCallSession.CreateGraph(declaration, child, model, cancellationToken);
        if (graph == null)
        { return new(null, "ForkJoinChildBodyUnavailable"); }
        return continuation(new(declaration, model, graph));
    }
}
