namespace SharpProof.CompilerArtifact;

// Collect potential source calls lazily from claim roots. Each method owns one
// factory and is lowered once. This shadow graph does not publish proof results.
internal static class CompilerReachableSourceCollector
{
    internal static CompilerReachableSourceArtifact Collect(CSharpCompilation compilation,
        IEnumerable<ManifestCallableTarget> targets, CompilerSyntaxTreeSnapshot[] trees,
        CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken)
    {
        var ordinals = compilation.SyntaxTrees.Select((tree, ordinal) => (tree, ordinal))
            .ToDictionary(item => item.tree, item => item.ordinal);
        var pending = new Queue<IMethodSymbol>();
        var bodies = new Dictionary<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal);
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var roots = new List<CompilerSourceRootArtifact>();
        var complete = true;
        var apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        foreach (var target in targets.OrderBy(target => target.Entry.CallableId, StringComparer.Ordinal))
        {
            var body = Enqueue(target.Method);
            if (body != null)
            {
                body.MethodIdentity = target.Entry.CallableId;
                roots.Add(new() { CallableId = target.Entry.CallableId, BodyId = body.BodyId });
            }
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
                    if (current is INameOfOperation or IAnonymousFunctionOperation ||
                        current is ILocalFunctionOperation && !ReferenceEquals(current, operation))
                    { continue; }
                    if (HasUncollectedCall(current, compilation, model, cancellationToken, ref remaining))
                    { body.CallsComplete = false; complete = false; }
                    if (current is IInvocationOperation invocation)
                    {
                        if (emission.IsElided(current))
                        { continue; }
                        var callee = invocation.TargetMethod.OriginalDefinition;
                        if (callee.IsVirtual || callee.IsAbstract || callee.IsOverride ||
                            callee.MethodKind == MethodKind.DelegateInvoke)
                        { body.CallsComplete = false; complete = false; }
                        if (SymbolEqualityComparer.Default.Equals(callee.ContainingAssembly, compilation.Assembly))
                        {
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
            // Calls stay as shadow boundaries rather than expanding bodies.
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
                        var specificationPacks = new CompilerSpecificationPackProvider(context.Factory, specificationPackAuthority);
                        var lowered = new RoslynProgramLowerer(context.Factory).LowerShadowSourceBody(graph, context,
                            callee => Enqueue(callee) != null, cancellationToken,
                            method => CompilerTotalCallableLowerer.ResolveScalarModel(method, context.Factory, apiSpecs, specificationPacks));
                        if (lowered.Classification.IsExact && body.CallsComplete)
                        {
                            var encoded = PortableIrGraphCodec.Encode(context.Factory, lowered.Program, [], cancellationToken: cancellationToken);
                            body.Graph = encoded.Graph;
                            body.IsCallSkeleton = lowered.PreservedSourceCalls.Count != 0;
                            body.SourceCalls = [.. lowered.PreservedSourceCalls.Select(call => new CompilerSourceCallArtifact
                            {
                                InstructionIndex = encoded.InstructionIndices[call.Key.Id],
                                CalleeBodyId = Enqueue(call.Value)!.BodyId
                            }).OrderBy(call => call.InstructionIndex)];
                            body.Callees = [.. body.SourceCalls.Select(call => call.CalleeBodyId).Distinct(StringComparer.Ordinal)
                                .OrderBy(id => id, StringComparer.Ordinal)];
                        }
                    }
                }
            }
        }
        // The compiler CFG can omit statically unreachable calls. Retain only
        // bodies connected by the final IR edges (or an incomplete boundary).
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var reachable = new Stack<string>(roots.Select(root => root.BodyId));
        while (reachable.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = reachable.Pop();
            if (!retained.Add(id))
            { continue; }
            foreach (var callee in bodies[id].Callees)
            { reachable.Push(callee); }
        }
        foreach (var id in bodies.Keys.Where(id => !retained.Contains(id)).ToArray())
        { bodies.Remove(id); }
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
            body.CallIdentity = CompilerIdentityBridge.CreateSymbolDisplay(method);
            body.IsStatic = method.IsStatic;
            if (method.Parameters.Length <= CompilerPreparedBody.MaximumInstructions &&
                method.Parameters.All(parameter => CSharpOperationSemantics.IsValueDomain(parameter.Type)) &&
                (method.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(method.ReturnType)))
            {
                var signatureFactory = new IrFactory(IrExecutionSemantics.Total);
                var mapper = new RoslynTypeMapper(signatureFactory);
                body.ParameterTypes = [.. method.Parameters.Select(parameter =>
                    CompilerReachableSourceValidator.TypeKey(signatureFactory, mapper.GetTypeId(parameter.Type)))];
                body.ReturnType = method.ReturnsVoid ? null :
                    CompilerReachableSourceValidator.TypeKey(signatureFactory, mapper.GetTypeId(method.ReturnType));
            }
            bodies.Add(id, body);
            pending.Enqueue(method);
            return body;
        }
    }

    private static bool HasUncollectedCall(IOperation operation, Compilation compilation, SemanticModel model,
        CancellationToken cancellationToken, ref int remaining)
    {
        if (operation is IDeconstructionAssignmentOperation deconstruction)
        {
            if (deconstruction.Syntax is not AssignmentExpressionSyntax syntax)
            { return true; }
            var pending = new Stack<DeconstructionInfo>();
            pending.Push(model.GetDeconstructionInfo(syntax));
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return true; }
                var info = pending.Pop();
                if (IsUncollectedMethod(info.Method, compilation) ||
                    IsUncollectedMethod(info.Conversion?.MethodSymbol, compilation))
                { return true; }
                if (info.Nested.IsDefault)
                { continue; }
                if (info.Nested.Length > remaining - pending.Count)
                { return true; }
                foreach (var nested in info.Nested)
                { pending.Push(nested); }
            }
        }
        if (operation is IRecursivePatternOperation { DeconstructSymbol: not null and not IMethodSymbol })
        { return true; }
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
            IRecursivePatternOperation { DeconstructSymbol: IMethodSymbol deconstruct } => deconstruct,
            IWithOperation withOperation => withOperation.CloneMethod,
            _ => null
        };
        return IsUncollectedMethod(method, compilation) ||
            operation is IIncrementOrDecrementOperation implicitIncrement && CSharpOperationSemantics.IsUnsupportedImplicitIncrement(implicitIncrement) ||
            operation is IDynamicInvocationOperation or IDynamicObjectCreationOperation or
                IDynamicMemberReferenceOperation or IDynamicIndexerAccessOperation or IFunctionPointerInvocationOperation or
                IAwaitOperation or IForEachLoopOperation or IUsingOperation or IUsingDeclarationOperation;
    }

    private static bool IsUncollectedMethod(IMethodSymbol? method, Compilation compilation)
    {
        var callable = method?.ReducedFrom ?? method;
        return callable != null &&
            (SymbolEqualityComparer.Default.Equals(callable.ContainingAssembly, compilation.Assembly) ||
                callable.IsVirtual || callable.IsAbstract || callable.IsOverride);
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
