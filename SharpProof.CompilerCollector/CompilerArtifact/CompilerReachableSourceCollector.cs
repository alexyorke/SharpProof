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
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        ArgumentNullGuard.NotNull(targets, nameof(targets));
        ArgumentNullGuard.NotNull(trees, nameof(trees));
        if (trees.Length < compilation.SyntaxTrees.Length)
        { throw new ArgumentException("Snapshot array must cover every compilation syntax tree.", nameof(trees)); }
        var ordinals = compilation.SyntaxTrees.Select((tree, ordinal) => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((tree, ordinal)))
            .ToImmutableDictionary(item => item.tree, item => item.ordinal);
        var pending = ImmutableQueue<IMethodSymbol>.Empty;
        var bodies = ImmutableDictionary.Create<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal);
        var methods = ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default);
        var roots = ImmutableList<CompilerSourceRootArtifact>.Empty;
        var complete = true;
        var apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        foreach (var target in targets.OrderBy(target => target is null ? throw new System.ArgumentNullException(nameof(target)) : (target.Entry.CallableId), StringComparer.Ordinal))
        {
            var body = Enqueue(target.Method);
            if (body != null)
            {
                body = UpdateRegisteredBody(body with { MethodIdentity = target.Entry.CallableId });
                roots = roots.Add(new() { CallableId = target.Entry.CallableId, BodyId = body.BodyId });
            }
            else
            { complete = false; }
        }
        var emission = new InvocationEmissionPolicy(compilation);
        while (!pending.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = pending.Peek();
            pending = pending.Dequeue();
            var syntax = method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var body = bodies[Id(syntax)];
            body = UpdateRegisteredBody(body with
            {
                EffectsCompleteAtEntry = CompilerTotalCallableLowerer.HasNoEffectEntryInitialization(
                compilation, method.ContainingType, cancellationToken)
            });
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            var calls = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
            body = UpdateRegisteredBody(body with { CallsComplete = operation != null });
            if (operation == null)
            { complete = false; }
            if (operation != null)
            {
                var stack = ImmutableStack<IOperation>.Empty;
                stack = stack.Push(operation);
                var remaining = CompilerArtifactLimits.MaximumInstructions;
                while (!stack.IsEmpty)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (--remaining < 0)
                    { body = UpdateRegisteredBody(body with { CallsComplete = false }); complete = false; break; }
                    var current = stack.Peek();
                    stack = stack.Pop();
                    // Deferred bodies belong to their own invocation, not to
                    // the enclosing method's execution.
                    if (current is INameOfOperation or IAnonymousFunctionOperation ||
                        current is ILocalFunctionOperation && !ReferenceEquals(current, operation))
                    { continue; }
                    if (HasUncollectedCall(current, compilation, model, cancellationToken, ref remaining))
                    { body = UpdateRegisteredBody(body with { CallsComplete = false }); complete = false; }
                    if (current is IInvocationOperation invocation)
                    {
                        if (emission.IsElided(current))
                        { continue; }
                        var callee = invocation.TargetMethod.OriginalDefinition;
                        if (callee.IsVirtual || callee.IsAbstract || callee.IsOverride ||
                            callee.MethodKind == MethodKind.DelegateInvoke)
                        { body = UpdateRegisteredBody(body with { CallsComplete = false }); complete = false; }
                        if (SymbolEqualityComparer.Default.Equals(callee.ContainingAssembly, compilation.Assembly))
                        {
                            var destination = Enqueue(callee);
                            if (destination == null)
                            { body = UpdateRegisteredBody(body with { CallsComplete = false }); complete = false; }
                            else
                            { calls = calls.Add(destination.BodyId); }
                        }
                    }
                    foreach (var child in current.ChildOperations)
                    { stack = stack.Push(child); }
                }
            }
            body = UpdateRegisteredBody(body with { Callees = [.. calls.OrderBy(id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id), StringComparer.Ordinal)] });
            // Calls stay as shadow boundaries rather than expanding bodies.
            if (syntax is MethodDeclarationSyntax declaration && method.IsStatic && !method.IsGenericMethod &&
                !method.ContainingType.IsGenericType &&
                !method.ContainingType.StaticConstructors.Any(constructor => constructor is null ? throw new System.ArgumentNullException(nameof(constructor)) : (!constructor.IsImplicitlyDeclared)) &&
                method.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind == RefKind.None &&
                    CSharpOperationSemantics.IsValueDomain(parameter.Type))) &&
                (method.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(method.ReturnType)))
            {
                var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method,
                    tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (trees[ordinals[tree]].Path));
                var binding = new ContractBinder(compilation, context.Factory).BindTotal(context,
                    cancellationToken: cancellationToken);
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
                            callee => callee is null ? throw new System.ArgumentNullException(nameof(callee)) : (Enqueue(callee) != null), cancellationToken,
                            method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (TotalBodyLowering.ResolveScalarModel(method, context.Factory, apiSpecs) ?? specificationPacks.ResolveTotal(method)));
                        if (lowered.Classification.IsExact && body.CallsComplete)
                        {
                            var encoded = PortableIrGraphCodec.Encode(context.Factory, lowered.Program, [], variables: null, cancellationToken: cancellationToken);
                            body = UpdateRegisteredBody(body with { Graph = encoded.Graph });
                            body = UpdateRegisteredBody(body with { IsCallSkeleton = lowered.PreservedSourceCalls.Count != 0 });
                            body = UpdateRegisteredBody(body with
                            {
                                SourceCalls = [.. lowered.PreservedSourceCalls.Select(call => new CompilerSourceCallArtifact
                            {
                                InstructionIndex = encoded.InstructionIndices[call.Key.Id],
                                CalleeBodyId = (Enqueue(call.Value) ?? throw new InvalidOperationException("Preserved source call has no collectable callee body.")).BodyId
                            }).OrderBy(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (call.InstructionIndex))]
                            });
                            body = UpdateRegisteredBody(body with
                            {
                                Callees = [.. body.SourceCalls.Select(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (call.CalleeBodyId)).Distinct(StringComparer.Ordinal)
                                .OrderBy(id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id), StringComparer.Ordinal)]
                            });
                        }
                    }
                }
            }
        }
        // The compiler CFG can omit statically unreachable calls. Retain only
        // bodies connected by the final IR edges (or an incomplete boundary).
        var retained = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
        var reachable = ImmutableStack<string>.Empty;
        foreach (var root in roots)
        { reachable = reachable.Push(root.BodyId); }
        while (!reachable.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = reachable.Peek();
            reachable = reachable.Pop();
            if (retained.Contains(id))
            { continue; }
            retained = retained.Add(id);
            foreach (var callee in bodies[id].Callees)
            { reachable = reachable.Push(callee); }
        }
        foreach (var id in bodies.Keys.Where(id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (!retained.Contains(id))).ToImmutableArray())
        { bodies = bodies.Remove(id); }
        return new()
        {
            CollectionComplete = complete,
            Roots = [.. roots.OrderBy(root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root.CallableId), StringComparer.Ordinal)],
            Bodies = [.. bodies.Values.OrderBy(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.BodyId), StringComparer.Ordinal)],
            Documents = [.. bodies.Values.Select(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.SourceTreeOrdinal)).Distinct().OrderBy(ordinal => ordinal)
                .Select(ordinal => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (new CompilerSourceDocumentArtifact
                {
                    SourceTreeOrdinal = ordinal, Path = trees[ordinal].Path,
                    MaximumBodyEnd = bodies.Values.Where(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.SourceTreeOrdinal == ordinal))
                        .Max(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Start + body.Length))
                })))]
        };

        CompilerSourceBodyArtifact UpdateRegisteredBody(CompilerSourceBodyArtifact body)
        {
            body = RepositoryParameterGuard.NotNull(body, nameof(body));
            if (!bodies.ContainsKey(body.BodyId))
            {
                throw new InvalidOperationException("A collected body must be registered before publication.");
            }
            bodies = bodies.SetItem(body.BodyId, body);
            return body;
        }

        string Id(SyntaxNode syntax)
        { syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax)); return CompilerReachableSourceValidator.BodyId(ordinals[syntax.SyntaxTree], syntax.SpanStart, syntax.Span.Length); }

        CompilerSourceBodyArtifact? Enqueue(IMethodSymbol requested)
        {
            requested = RepositoryParameterGuard.NotNull(requested, nameof(requested));
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
            if (bodies.Count >= CompilerArtifactLimits.MaximumInstructions || methods.Contains(method))
            { return null; }
            methods = methods.Add(method);
            var body = new CompilerSourceBodyArtifact
            {
                BodyId = id,
                MethodIdentity = method.GetDocumentationCommentId() ?? method.MetadataName + "@" + id,
                SourceTreeOrdinal = ordinal,
                Start = syntax.SpanStart,
                Length = syntax.Span.Length
            };
            body = body with { CallIdentity = CompilerIdentityBridge.CreateSymbolDisplay(method) };
            body = body with { IsStatic = method.IsStatic };
            if (method.Parameters.Length <= CompilerArtifactLimits.MaximumInstructions &&
                method.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (CSharpOperationSemantics.IsValueDomain(parameter.Type))) &&
                (method.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(method.ReturnType)))
            {
                var signatureFactory = new IrFactory(IrExecutionSemantics.Total);
                var mapper = new RoslynTypeMapper(signatureFactory);
                body = body with
                {
                    ParameterTypes = [.. method.Parameters.Select(parameter =>
                    parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (CompilerReachableSourceValidator.TypeKey(signatureFactory, mapper.GetTypeId(parameter.Type))))]
                };
                body = body with
                {
                    ReturnType = method.ReturnsVoid ? null :
                    CompilerReachableSourceValidator.TypeKey(signatureFactory, mapper.GetTypeId(method.ReturnType))
                };
            }
            bodies = bodies.AddUnique(id, body);
            pending = pending.Enqueue(method);
            return body;
        }
    }

    private static bool HasUncollectedCall(IOperation operation, Compilation compilation, SemanticModel model,
        CancellationToken cancellationToken, ref int remaining)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        if (operation is IDeconstructionAssignmentOperation deconstruction)
        {
            var syntax = (AssignmentExpressionSyntax)deconstruction.Syntax;
            var pending = ImmutableList<DeconstructionInfo>.Empty;
            pending = pending.Add(model.GetDeconstructionInfo(syntax));
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return true; }
                var info = pending[pending.Count - 1];
                pending = pending.RemoveAt(pending.Count - 1);
                if (IsUncollectedMethod(info.Method, compilation) ||
                    IsUncollectedMethod(info.Conversion?.MethodSymbol, compilation))
                { return true; }
                if (info.Nested.IsDefault)
                { continue; }
                if (info.Nested.Length > remaining - pending.Count)
                { return true; }
                foreach (var nested in info.Nested)
                { pending = pending.Add(nested); }
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
            ICollectionExpressionOperation collection => collection.ConstructMethod,
            _ => null
        };
        return IsUncollectedMethod(method, compilation) ||
            operation is IImplicitIndexerReferenceOperation indexer &&
                (IsUncollectedMember(indexer.LengthSymbol, compilation) ||
                 IsUncollectedMember(indexer.IndexerSymbol, compilation)) ||
            operation is ICompoundAssignmentOperation assignment &&
                (IsUncollectedMethod(assignment.InConversion.MethodSymbol, compilation) ||
                 IsUncollectedMethod(assignment.OutConversion.MethodSymbol, compilation)) ||
            operation is IIncrementOrDecrementOperation implicitIncrement && CSharpOperationSemantics.IsUnsupportedImplicitIncrement(implicitIncrement) ||
            operation is IDynamicInvocationOperation or IDynamicObjectCreationOperation or
                IDynamicMemberReferenceOperation or IDynamicIndexerAccessOperation or IFunctionPointerInvocationOperation or
                IAwaitOperation or IForEachLoopOperation or IUsingOperation or IUsingDeclarationOperation;
    }

    private static bool IsUncollectedMethod(IMethodSymbol? method, Compilation compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var callable = method?.ReducedFrom ?? method;
        return callable != null &&
            (SymbolEqualityComparer.Default.Equals(callable.ContainingAssembly, compilation.Assembly) ||
                callable.IsVirtual || callable.IsAbstract || callable.IsOverride);
    }

    private static bool IsUncollectedMember(ISymbol member, Compilation compilation)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        if (member is IMethodSymbol method)
        {
            return IsUncollectedMethod(method, compilation);
        }
        var property = (IPropertySymbol)member;
        return IsUncollectedMethod(property.GetMethod, compilation) ||
            IsUncollectedMethod(property.SetMethod, compilation);
    }

    private static bool RestoreEmittedSpecifications(IOperation operation, TotalLoweringContext context,
        InvocationEmissionPolicy emission, CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        emission = RepositoryParameterGuard.NotNull(emission, nameof(emission));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var pending = ImmutableStack<IOperation>.Empty;
        pending = pending.Push(operation);
        var remaining = CompilerArtifactLimits.MaximumInstructions;
        while (!pending.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return false; }
            var current = pending.Peek();
            pending = pending.Pop();
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            { continue; }
            if (current is IInvocationOperation invocation && context.IsSpecificationOperation(current))
            {
                if (!emission.IsElided(current))
                { context.RestoreSpecificationCall(invocation); }
                continue;
            }
            foreach (var child in current.ChildOperations)
            { pending = pending.Push(child); }
        }
        return true;
    }
}
