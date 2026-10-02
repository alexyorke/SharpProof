namespace SharpProof.CompilerArtifact;

// Collect potential source calls lazily from claim roots. Each method owns one
// factory and is lowered once. This shadow graph does not publish proof results.
internal static class CompilerReachableSourceCollector
{
    internal static CompilerReachableSourceArtifact Collect(CSharpCompilation compilation,
        IEnumerable<ManifestCallableTarget> targets, CompilerSyntaxTreeSnapshot[] trees,
        CancellationToken cancellationToken)
    {
        var ordinals = compilation.SyntaxTrees.Select((tree, ordinal) => (tree, ordinal))
            .ToDictionary(item => item.tree, item => item.ordinal);
        var pending = new Queue<IMethodSymbol>();
        var bodies = new Dictionary<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal);
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var roots = new List<CompilerSourceRootArtifact>();
        var complete = true;
        foreach (var target in targets)
        {
            var body = Enqueue(target.Method);
            if (body != null)
            { roots.Add(new() { CallableId = target.Entry.CallableId, BodyId = body.BodyId }); }
            else
            { complete = false; }
        }
        var emission = new InvocationEmissionPolicy(compilation);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = pending.Dequeue();
            var syntax = method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var body = bodies[Id(syntax)];
            body.EffectsCompleteAtEntry = CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(
                compilation, method.ContainingType, cancellationToken);
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            var calls = new HashSet<string>(StringComparer.Ordinal);
            body.CallsComplete = operation != null;
            if (operation == null)
            { complete = false; }
            if (operation != null)
            {
                var stack = new Stack<IOperation>();
                stack.Push(operation);
                var remaining = CompilerPreparedBody.MaximumInstructions;
                while (stack.Count != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (--remaining < 0)
                    { body.CallsComplete = false; complete = false; break; }
                    var current = stack.Pop();
                    // Deferred bodies belong to their own invocation, not to
                    // the enclosing method's execution.
                    if (current is IAnonymousFunctionOperation || current is ILocalFunctionOperation && !ReferenceEquals(current, operation))
                    { continue; }
                    if (HasUncollectedCall(current, compilation))
                    { body.CallsComplete = false; complete = false; }
                    if (current is IInvocationOperation invocation)
                    {
                        if (emission.IsElided(current))
                        { continue; }
                        var callee = invocation.TargetMethod.OriginalDefinition;
                        if (SymbolEqualityComparer.Default.Equals(callee.ContainingAssembly, compilation.Assembly))
                        {
                            if (callee.IsVirtual || callee.IsAbstract || callee.IsOverride)
                            { body.CallsComplete = false; complete = false; }
                            var destination = Enqueue(callee);
                            if (destination == null)
                            { body.CallsComplete = false; complete = false; }
                            else
                            { calls.Add(destination.BodyId); }
                        }
                    }
                    foreach (var child in current.ChildOperations)
                    { stack.Push(child); }
                }
            }
            body.Callees = [.. calls.OrderBy(id => id, StringComparer.Ordinal)];
            // Source calls deliberately remain incomplete in this first
            // consumer. Leaf bodies already use the canonical Total lowering.
            if (syntax is MethodDeclarationSyntax declaration && method.IsStatic && !method.IsGenericMethod &&
                !method.ContainingType.IsGenericType &&
                !method.ContainingType.StaticConstructors.Any(constructor => !constructor.IsImplicitlyDeclared) &&
                method.Parameters.All(parameter => parameter.RefKind == RefKind.None &&
                    CSharpOperationSemantics.IsValueDomain(parameter.Type)) &&
                (method.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(method.ReturnType)))
            {
                var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method,
                    tree => trees[ordinals[tree]].Path);
                var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
                if (binding.IsSuccess && operation != null)
                {
                    if (!RestoreEmittedSpecifications(operation, context, emission, cancellationToken))
                    { continue; }
                    context.DiscardSpecificationAssumptions();
                    var graph = ControlFlowGraph.Create(declaration, model, cancellationToken);
                    if (graph != null)
                    {
                        var lowered = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context, cancellationToken);
                        if (lowered.IsExact)
                        { body.Graph = PortableIrGraphCodec.Encode(context.Factory, lowered.Program, [], cancellationToken: cancellationToken).Graph; }
                    }
                }
            }
        }
        return new()
        {
            CollectionComplete = complete,
            Roots = [.. roots.OrderBy(root => root.CallableId, StringComparer.Ordinal)],
            Bodies = [.. bodies.Values.OrderBy(body => body.BodyId, StringComparer.Ordinal)],
            Documents = [.. bodies.Values.Select(body => body.SourceTreeOrdinal).Distinct().OrderBy(ordinal => ordinal)
                .Select(ordinal => new CompilerSourceDocumentArtifact
                {
                    SourceTreeOrdinal = ordinal, Path = trees[ordinal].Path,
                    MaximumBodyEnd = bodies.Values.Where(body => body.SourceTreeOrdinal == ordinal)
                        .Max(body => body.Start + body.Length)
                })]
        };

        string Id(SyntaxNode syntax)
        { return CompilerReachableSourceValidator.BodyId(ordinals[syntax.SyntaxTree], syntax.SpanStart, syntax.Span.Length); }

        CompilerSourceBodyArtifact? Enqueue(IMethodSymbol requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = requested.OriginalDefinition;
            if (method.DeclaringSyntaxReferences.Length != 1)
            { return null; }
            var syntax = method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            if (!ordinals.TryGetValue(syntax.SyntaxTree, out var ordinal))
            { return null; }
            var id = Id(syntax);
            if (bodies.TryGetValue(id, out var existing))
            { return existing; }
            if (bodies.Count >= CompilerPreparedBody.MaximumInstructions || !methods.Add(method))
            { return null; }
            var body = new CompilerSourceBodyArtifact
            {
                BodyId = id,
                MethodIdentity = method.GetDocumentationCommentId() ?? method.MetadataName + "@" + id,
                SourceTreeOrdinal = ordinal,
                Start = syntax.SpanStart,
                Length = syntax.Span.Length
            };
            bodies.Add(id, body);
            pending.Enqueue(method);
            return body;
        }
    }

    private static bool HasUncollectedCall(IOperation operation, Compilation compilation)
    {
        var method = operation switch
        {
            IObjectCreationOperation creation => creation.Constructor,
            IPropertyReferenceOperation property => property.Property.GetMethod ?? property.Property.SetMethod,
            IEventReferenceOperation eventReference => eventReference.Event.AddMethod ?? eventReference.Event.RemoveMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            _ => null
        };
        return method != null && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) ||
            operation is IDynamicInvocationOperation or IDynamicObjectCreationOperation or IAwaitOperation or IForEachLoopOperation or IUsingOperation;
    }

    private static bool RestoreEmittedSpecifications(IOperation operation, TotalLoweringContext context,
        InvocationEmissionPolicy emission, CancellationToken cancellationToken)
    {
        var pending = new Stack<IOperation>();
        pending.Push(operation);
        var remaining = CompilerPreparedBody.MaximumInstructions;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return false; }
            var current = pending.Pop();
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            { continue; }
            if (current is IInvocationOperation invocation && context.IsSpecificationOperation(current))
            {
                if (!emission.IsElided(current))
                { context.RestoreSpecificationCall(invocation); }
                continue;
            }
            foreach (var child in current.ChildOperations)
            { pending.Push(child); }
        }
        return true;
    }
}
