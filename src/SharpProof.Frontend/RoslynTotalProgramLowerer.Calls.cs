using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private sealed class SourceCallFrame(Action<IrBlockId, OperationId, IrTerm?> returned,
        Func<IrExceptionKind, OperationId, Func<IrBlockId, IrBlockId>, IrBlockId> search)
    {
        private Func<IrExceptionKind, OperationId, Func<IrBlockId, IrBlockId>, IrBlockId> _guarded_search = search ?? throw new System.ArgumentNullException(nameof(search));
        private Action<IrBlockId, OperationId, IrTerm?> _guarded_returned = returned ?? throw new System.ArgumentNullException(nameof(returned));
        internal IrBlockId Entry { get; set; }
        internal void Return(IrBlockId block, OperationId site, IrTerm? value)
        { RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block)); RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site)); _guarded_returned(block, site, value); }
        internal IrBlockId Search(IrExceptionKind kind, OperationId site, Func<IrBlockId, IrBlockId> unwind)
        { unwind = RepositoryParameterGuard.NotNull(unwind, nameof(unwind)); RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site)); return _guarded_search(kind, site, unwind); }
    }

    // A shadow call records an effect boundary, not executable Total semantics.
    // In particular, it supplies no exception edge or completion guarantee.
    private TotalBodyValue? PreserveSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var method = invocation.TargetMethod;
        if (invocation.Instance != null || method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic || method.IsAsync || method.IsExtern || method.IsVirtual || method.IsAbstract || method.IsOverride ||
            method.Arity != 0 || method.ContainingType.IsGenericType || method.ReducedFrom != null ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.PartialDefinitionPart != null || method.PartialImplementationPart != null ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _context.Target.ContainingAssembly) ||
            method.DeclaringSyntaxReferences.Length != 1 || invocation.Arguments.Length != method.Parameters.Length ||
            method.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind != RefKind.None ||
                !CSharpOperationSemantics.IsValueDomain(parameter.Type))) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType))
        { return null; }
        var ordinals = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            if (argument.Parameter is not { } parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method) ||
                parameter.Ordinal < 0 || parameter.Ordinal >= method.Parameters.Length || !ordinals.Add(parameter.Ordinal) ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue or ArgumentKind.ParamArray) ||
                argument.ArgumentKind == ArgumentKind.ParamArray && !parameter.IsParams ||
                argument.ArgumentKind == ArgumentKind.DefaultValue &&
                    (!parameter.HasExplicitDefaultValue || !argument.Value.ConstantValue.HasValue))
            { return null; }
        }
        if (!_preserveSourceCall!(method.OriginalDefinition))
        { return null; }
        var resultType = _context.Type(method.ReturnType);
        var target = method.ReturnsVoid ? (IrVarId?)null : _context.Temporary(resultType);
        IrTerm value = target is { } returned ? _context.Factory.Variable(returned) : _context.Factory.Boolean(false);
        if (target is { } storage)
        { _builder.Assign(block, _context.Site(invocation), storage, CSharpOperationSemantics.DefaultValue(_context.Factory, resultType)); }
        var arguments = new IrTerm[method.Parameters.Length];
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            if (TotalSourceCallSession.IsEmptyParamsArray(argument))
            {
                if (_calls?.PrepareEmptyParamsArray(argument) is not { } prepared ||
                    prepared.Model.Apply([]) is not { Classification.IsExact: true, Throws.IsEmpty: true } empty)
                { return new(value, block, FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape)); }
                _expressions.RecordScalarEffects(argument, prepared.Method, prepared.Model, [], block);
                arguments[argument.Parameter!.Ordinal] = empty.Value;
                continue;
            }
            var lowered = _expressions.LowerBodyValue(argument.Value, block, depth + 1);
            block = lowered.Continuation;
            if (!lowered.Classification.IsExact)
            { return new(value, block, lowered.Classification); }
            arguments[argument.Parameter!.Ordinal] = lowered.Value;
        }
        var member = _context.Factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_context.Factory, method), _context.Type(method.ContainingType),
            "shadow-call:" + CompilerIdentityBridge.CreateSymbolDisplay(method), resultType, true,
            [.. method.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (_context.Type(parameter.Type)))]);
        var call = _builder.Call(block, _context.Site(invocation), target, member, null, arguments);
        _preservedSourceCalls.Add(call, method.OriginalDefinition);
        return new(value, block, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? InlineSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (_calls?.PrepareScalarCall(invocation) is { } model)
        { return _expressions.LowerScalarCall(invocation, model, block, depth); }
        if (_preserveSourceCall != null)
        { return PreserveSourceCall(invocation, block, depth); }
        if (invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty && _calls?.PrepareIl(invocation.TargetMethod) is { } body)
        { return InlineMetadataCall(invocation, body, block, depth); }
        return InlineSourceMember(invocation, invocation.TargetMethod, invocation.Instance, invocation.Arguments, block, depth);
    }

    // `new C(arguments)` evaluates the arguments, allocates the object and
    // runs instance initializers, the constructor body, then object initializers.
    // A body CFG does not contain the separate field/property initializers.
    // Any other declared class constructor runs as an unknown call after its
    // preconditions.
    private TotalBodyValue? InlineSourceConstruction(IObjectCreationOperation creation, IrBlockId block, int depth)
    {
        if (_preserveSourceCall != null || creation.Constructor is not { } constructor ||
            !CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken) &&
                !(_expressions.AllowOpaqueCalls && !constructor.IsImplicitlyDeclared && constructor.ContainingType.TypeKind == TypeKind.Class))
        { return null; }
        foreach (var initializer in creation.Initializer?.Initializers ?? [])
        {
            SpendRegion();
            if (initializer is not ISimpleAssignmentOperation
                {
                    IsRef: false, Target: IFieldReferenceOperation
                    { Field.IsStatic: false, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver } }
                })
            { return null; }
        }
        if (!TryConstructionInitializers(constructor, out var initializers))
        { return null; }
        IrTerm? created = null;
        IrBlockId Allocate(IrBlockId current)
        {
            RepositoryParameterGuard.ThrowIfInvalid(current.IsDefault, nameof(current));
            var allocatedType = _context.Type(creation.Type);
            var initializeDefaults = false;
            if (CSharpOperationSemantics.CompleteObjectStorage(creation.Type, _cancellationToken) is { } storage &&
                _context.Factory.TryGetClosedClassReferenceBase(allocatedType, out var parent) && !parent.HasValue)
            {
                _context.Factory.RegisterObjectStorageType(allocatedType, storage);
                initializeDefaults = true;
            }
            var allocated = _expressions.AllocateValue(creation, current);
            created = allocated.Value;
            current = allocated.Continuation;
            // CLR zero initialization precedes source initializers and constructor
            // execution. Materialize each certified slot with its scoped field ID.
            if (initializeDefaults && creation.Type is INamedTypeSymbol owner)
            {
                foreach (var member in owner.GetMembers())
                {
                    if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
                    { continue; }
                    SpendRegion();
                    var value = CSharpOperationSemantics.DefaultValue(_context.Factory, _context.Type(field.Type));
                    if (!_expressions.StoreInitializer(field, created, creation, value, current))
                    { throw new RegionIncompleteException(); }
                }
            }
            foreach (var (field, initializer) in initializers)
            {
                SpendRegion();
                var lowered = _expressions.LowerBodyValue(initializer, current, depth + 1);
                if (!lowered.Classification.IsExact ||
                    !_expressions.StoreInitializer(field, created, initializer, lowered.Value, lowered.Continuation))
                { throw new RegionIncompleteException(); }
                current = lowered.Continuation;
            }
            return current;
        }
        TotalBodyValue Complete(IrBlockId current)
        {
            RepositoryParameterGuard.ThrowIfInvalid(current.IsDefault, nameof(current));
            foreach (var initializer in creation.Initializer?.Initializers ?? [])
            {
                SpendRegion();
                var assignment = (ISimpleAssignmentOperation)initializer;
                var field = (IFieldReferenceOperation)assignment.Target;
                var lowered = _expressions.LowerBodyValue(assignment.Value, current, depth + 1);
                if (!lowered.Classification.IsExact ||
                    !_expressions.StoreInitializer(field.Field, created!, assignment.Value, lowered.Value, lowered.Continuation))
                { throw new RegionIncompleteException(); }
                current = lowered.Continuation;
            }
            return new(created!, current, FrontendSubsetClassification.Exact);
        }
        if (constructor.IsImplicitlyDeclared)
        {
            if (!creation.Arguments.IsEmpty || _calls == null || !_calls.HasNoTypeInitialization(constructor.ContainingType))
            { return null; }
            block = Allocate(block);
            return Complete(block);
        }
        if (InlineSourceMember(creation, constructor, null, creation.Arguments, block, depth, prelude: Allocate, constructedReceiver: () => created) is not { } body)
        { return null; }
        if (created == null)
        {
            // The constructor abstained before allocating; its value is unknown.
            var unknown = _context.Temporary(_context.Type(creation.Type));
            _builder.Havoc(body.Continuation, _context.Site(creation), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
            created = _context.Factory.Variable(unknown);
        }
        return body.Classification.IsExact ? Complete(body.Continuation) : new(created, body.Continuation, body.Classification);
    }

    private bool TryConstructionInitializers(IMethodSymbol constructor, out ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        initializers = [];
        if (CSharpOperationSemantics.IsPlainConstructor(constructor, _cancellationToken) ||
            !CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken))
        { return true; }
        var type = constructor.ContainingType;
        if (_calls == null || _context.Compilation is not { } compilation || type.IsGenericType ||
            type.DeclaringSyntaxReferences.Length != 1 ||
            type.DeclaringSyntaxReferences[0].GetSyntax(_cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration)
        { return false; }
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        var values = ImmutableArray.CreateBuilder<(IFieldSymbol Field, IOperation Value)>();
        foreach (var member in declaration.Members)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_calls.Spend())
            { return false; }
            if (member is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field &&
                !field.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) || modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword)))
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (!_calls.Spend())
                    { return false; }
                    if (variable.Initializer == null)
                    { continue; }
                    if (model.GetOperation(variable.Initializer, _cancellationToken) is not IFieldInitializerOperation { InitializedFields.Length: 1 } initializer)
                    { return false; }
                    values.Add((initializer.InitializedFields[0], initializer.Value));
                }
            }
            else if (member is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax { Initializer: { } value } property &&
                !property.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)))
            {
                if (model.GetDeclaredSymbol(property, _cancellationToken) is not IPropertySymbol symbol ||
                    model.GetOperation(value, _cancellationToken) is not IPropertyInitializerOperation initializer ||
                    type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
                        SymbolEqualityComparer.Default.Equals(candidate.AssociatedSymbol, symbol)) is not { } backing)
                { return false; }
                values.Add((backing, initializer.Value));
            }
            else if (member is Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax)
            { return false; }
        }
        initializers = values.ToImmutable();
        return true;
    }
    private TotalBodyValue? InlineSourceGetter(IPropertyReferenceOperation property, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        return _preserveSourceCall != null || property.Property.GetMethod is not { } getter ? null
            : InlineSourceMember(property, getter, property.Instance, property.Arguments, block, depth);
    }

    // `target[arguments] = value` evaluates the receiver, the arguments and the
    // value, then runs the setter; the expression's value is the assigned one.
    private TotalBodyValue? InlineSourceSetter(ISimpleAssignmentOperation assignment, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        assignment = RepositoryParameterGuard.NotNull(assignment, nameof(assignment));
        return _preserveSourceCall != null || assignment.Target is not IPropertyReferenceOperation { Property.SetMethod: { } setter } property
            ? null : InlineSourceMember(assignment, setter, property.Instance, property.Arguments, block, depth, assignment.Value);
    }

    private TotalBodyValue? InlineSourceMember(IOperation invocation, IMethodSymbol method, IOperation? instance,
        ImmutableArray<IArgumentOperation> callArguments, IrBlockId block, int depth, IOperation? assigned = null,
        Func<IrBlockId, IrBlockId>? prelude = null, Func<IrTerm?>? constructedReceiver = null)
    {
        if (_calls == null || !_calls.TryPrepare(_context, method, instance, callArguments, out var frame, out var graph, assigned != null, constructorInitializers: constructedReceiver != null) &&
            !(_expressions.AllowOpaqueCalls &&
                _calls.TryPrepare(_context, method, instance, callArguments, out frame, out graph, assigned != null, contractOnly: true, constructorInitializers: constructedReceiver != null)))
        { return null; }
        var callee = frame!;
        var site = _context.Site(invocation);
        // Nested lowering has its own region state. Capture the caller's lexical
        // search/unwind context before expanding arguments or the callee body.
        var callerRegion = _regionGraph == null ? null : _regionSource.EnclosingRegion;
        var callerFilter = callerRegion == null ? null : EnclosingRegionFilter(callerRegion);
        var externalFilterSearch = _externalFilterSearch || callerFilter != null || HasEnclosingCatchFilter(callerRegion);
        var returned = callee.Result is { } result ? _context.Temporary(_context.Factory.GetVariableInfo(result).Type) : (IrVarId?)null;
        // A generic container's declaration types bridge to the caller's.
        IrTerm marker = returned is { } resultStorage ? Bridge(_context.Factory.Variable(resultStorage), _context.Type(method.ReturnType))
            : _context.Factory.Boolean(false);
        IrTerm? receiver = null;
        if (instance != null && !_expressions.IsImplicitThis(instance))
        {
            var lowered = _expressions.LowerBodyValue(instance, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return new(marker, lowered.Continuation, lowered.Classification); }
            receiver = lowered.Value;
            block = lowered.Continuation;
        }
        // The callee's `this` is the receiver as evaluated before the
        // arguments; an unmodeled caller `this` is unknown.
        if (callee.Receiver is { } self)
        {
            if ((receiver ?? _context.ReceiverValue()) is { } value)
            { _builder.Assign(block, site, self.Entry, Bridge(value, _context.Factory.GetVariableInfo(self.Entry).Type)); }
            else
            { _builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, self.Entry); }
        }
        if (returned is { } initialized)
        {
            // A fault never reads a call result. Keeping its internal storage
            // initialized preserves normal-only writes across shared finally.
            var type = _context.Factory.GetVariableInfo(initialized).Type;
            _builder.Assign(block, site, initialized, CSharpOperationSemantics.DefaultValue(_context.Factory, type));
        }
        var arguments = new IrTerm[callee.Parameters.Length];
        foreach (var argument in callArguments)
        {
            SpendRegion();
            var parameter = argument.Parameter!;
            if (TotalSourceCallSession.IsEmptyParamsArray(argument))
            {
                if (_calls.PrepareEmptyParamsArray(argument) is not { } prepared ||
                    prepared.Model.Apply([]) is not { Classification.IsExact: true, Throws.IsEmpty: true } empty)
                { return new(marker, block, FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape)); }
                _expressions.RecordScalarEffects(argument, prepared.Method, prepared.Model, [], block);
                arguments[parameter.Ordinal] = empty.Value;
                continue;
            }
            var value = _expressions.LowerReceiverValue(argument.Value, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            arguments[parameter.Ordinal] = Bridge(value.Value,
                _context.Factory.GetVariableInfo(callee.Parameters[parameter.Ordinal].Entry).Type);
            if (constructedReceiver != null)
            {
                var pinned = _context.Temporary(arguments[parameter.Ordinal].Type);
                _builder.Assign(block, site, pinned, arguments[parameter.Ordinal]);
                arguments[parameter.Ordinal] = _context.Factory.Variable(pinned);
            }
        }
        if (assigned != null)
        {
            var value = _expressions.LowerBodyValue(assigned, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            arguments[arguments.Length - 1] = Bridge(value.Value,
                _context.Factory.GetVariableInfo(callee.Parameters[callee.Parameters.Length - 1].Entry).Type);
            marker = value.Value;
        }
        if (receiver != null)
        { block = _expressions.CheckReceiver(invocation, receiver, block); }
        if (prelude != null)
        { block = prelude(block); }
        if (constructedReceiver?.Invoke() is { } freshReceiver && callee.Receiver is { } constructorReceiver)
        { _builder.Assign(block, site, constructorReceiver.Entry, Bridge(freshReceiver, _context.Factory.GetVariableInfo(constructorReceiver.Entry).Type)); }
        ImmutableArray<TotalShadowCallHop> callAncestry = [];
        if (_captureShadowCallAncestry)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var callSpan = _context.Factory.GetOperationInfo(site).SourceSpan!;
            callAncestry = _shadowAncestry.Add(new(CompilerIdentityBridge.CreateSymbolDisplay(_context.Target),
                CompilerIdentityBridge.CreateSymbolDisplay(callee.Target), callSpan));
        }
        RecordCallPreconditions(callee, arguments, block, site, callAncestry);
        // A recursive call checks its callee's preconditions, then runs the
        // callee as an unknown call: it may do anything, throw included. So
        // does a call to an iterator, which has none, and a call whose
        // callee body does not lower.
        if (graph == null)
        { return Opaque(); }
        if (externalFilterSearch)
        {
            if (!_calls.Spend(callee.Parameters.Length + 2))
            { throw new RegionIncompleteException(); }
            var continued = _builder.CreateBlock("call:continued");
            var composed = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch, shadowAncestry: callAncestry, captureShadowCallAncestry: _captureShadowCallAncestry)
            {
                _builder = _builder
            };
            composed._frame = new((source, operation, value) =>
                {
                    RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
                    RepositoryParameterGuard.ThrowIfInvalid(source.IsDefault, nameof(source));
                    if (returned is { } storage && value != null)
                    { _builder.Assign(source, operation, storage, value); }
                    _builder.Goto(source, operation, continued);
                }, (kind, operation, unwind) =>
                {
                    RepositoryParameterGuard.ThrowIfInvalid(operation.IsDefault, nameof(operation));
                    unwind = RepositoryParameterGuard.NotNull(unwind, nameof(unwind));
                    ImportExplicitThrowTypes(composed);
                    return ContinueSourceException(callerRegion, callerFilter, kind, operation, unwind);
                });
            composed.LowerSharedFrame(graph!);
            foreach (var obligation in composed._callPreconditions)
            { _callPreconditions.Add(obligation.Key, obligation.Value); }
            foreach (var parameter in callee.Parameters)
            { _builder.Assign(block, site, parameter.Entry, arguments[parameter.Parameter.Ordinal]); }
            _builder.Goto(block, site, composed._frame.Entry);
            return new(marker, continued, FrontendSubsetClassification.Exact);
        }
        var expanded = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch, shadowAncestry: callAncestry, captureShadowCallAncestry: _captureShadowCallAncestry);
        var lowering = expanded.Lower(graph!);
        if (!lowering.IsExact)
        { return _expressions.AllowOpaqueCalls ? Opaque() : new(marker, block, lowering.Classification); }
        ImportExplicitThrowTypes(expanded);
        var program = lowering.Program;
        var instructionCount = 0;
        foreach (var source in program.Blocks)
        {
            SpendRegion();
            instructionCount += source.Instructions.Length;
        }
        if (!_calls.Spend(program.Blocks.Length + instructionCount + callee.Parameters.Length + 2))
        { throw new RegionIncompleteException(); }
        var continuation = _builder.CreateBlock("call:continued");
        var blocks = program.Blocks.ToDictionary(source => source is null ? throw new System.ArgumentNullException(nameof(source)) : (source.Id), source => source is null ? throw new System.ArgumentNullException(nameof(source)) : (_builder.CreateBlock("call:body")));
        foreach (var parameter in callee.Parameters)
        { _builder.Assign(block, site, parameter.Entry, arguments[parameter.Parameter.Ordinal]); }
        _builder.Goto(block, site, blocks[program.Entry]);
        foreach (var source in program.Blocks)
        {
            foreach (var instruction in source.Instructions)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var destination = blocks[source.Id];
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        _builder.Allocate(destination, allocation.Operation, allocation.AllocatedType, allocation.Target, allocation.Length, allocation.InitialValues);
                        break;
                    case IrLockInstruction synchronization:
                        _builder.Lock(destination, synchronization.Operation, synchronization.Receiver);
                        break;
                    case IrWriteInstruction { Target: { } stored, Field: null, Index: { } index, Value: { } value } write:
                        _builder.ElementStore(destination, write.Operation, stored, index, value, write.Length);
                        break;
                    case IrWriteInstruction { Target: { } storedReceiver, Field: { } field, Value: { } value } write:
                        _builder.FieldStore(destination, write.Operation, write.Region, storedReceiver, field, value, write.Index);
                        break;
                    case IrWriteInstruction write:
                        _builder.Write(destination, write.Operation, write.Region);
                        break;
                    case IrHavocInstruction { Origin: IrHavocOrigin.Approximation } havoc:
                        _builder.Havoc(destination, havoc.Operation, havoc.HavocKind, havoc.Origin, [.. havoc.Variables]);
                        break;
                    case IrCallInstruction { Receiver: null, Target: null } opaque:
                        _builder.Call(destination, opaque.Operation, null, opaque.Member, null, [.. opaque.Arguments]);
                        break;
                    case IrAssignInstruction assign:
                        var copied = _builder.Assign(destination, assign.Operation, assign.Target, assign.Value);
                        if (lowering.CallPreconditions.TryGetValue(assign, out var obligation))
                        { _callPreconditions.Add(copied, obligation); }
                        break;
                    case IrBranchInstruction branch:
                        _builder.Branch(destination, branch.Operation, branch.Condition, Target(branch.WhenTrue), Target(branch.WhenFalse));
                        break;
                    case IrGotoInstruction go:
                        _builder.Goto(destination, go.Operation, Target(go.Target));
                        break;
                    case IrThrowInstruction thrown:
                        var exceptionTarget = program.GetBlock(thrown.Target).Terminator is IrExceptionalExitInstruction
                            ? callerFilter != null ? callerFilter.Rejected : callerRegion == null ? _ordinaryExceptionalExit
                                : RegionExceptionTarget(callerRegion, Token(thrown.ExceptionKind, thrown.Operation))
                            : blocks[thrown.Target];
                        _builder.Throw(destination, thrown.Operation, thrown.ExceptionKind, exceptionTarget);
                        break;
                    case IrExceptionalExitInstruction exceptional:
                        _builder.ExceptionalExit(destination, exceptional.Operation);
                        break;
                    case IrReturnInstruction normal:
                        if (returned is { } resultTarget && normal.Value != null)
                        { _builder.Assign(destination, normal.Operation, resultTarget, normal.Value); }
                        _builder.Goto(destination, normal.Operation, continuation);
                        break;
                    default:
                        throw new RegionIncompleteException();
                }
            }
        }
        return new(marker, continuation, FrontendSubsetClassification.Exact);

        TotalBodyValue Opaque()
        {
            var called = _expressions.EmitOpaqueCall(invocation, method, arguments, block, assigned != null ? marker : null,
                IrOpaqueCallEffects.All);
            return method.ReturnsVoid && assigned == null ? new(marker, called.Continuation, called.Classification) : called;
        }

        IrBlockId Target(IrBlockId target)
        {
            RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
            // Programs lowered above retain escaping faults as kind/site-bearing
            // Throws; their generic edges do not target an exceptional exit.
            return blocks[target];
        }
    }

    private IrTerm Bridge(IrTerm value, IrTypeId type)
    { value = RepositoryParameterGuard.NotNull(value, nameof(value)); RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type)); return _context.BridgeValue(value, type); }

    private void RecordCallPreconditions(TotalLoweringContext callee, IrTerm[] arguments, IrBlockId block, OperationId site, ImmutableArray<TotalShadowCallHop> ancestry)
    {

        RepositoryParameterGuard.RequireInitialized(ancestry.IsDefault, nameof(ancestry), "Array must be initialized.");
        arguments = RepositoryParameterGuard.NotNull(arguments, nameof(arguments));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (callee.SourceCallPreconditions.IsEmpty)
        { return; }
        var replacements = callee.Parameters.ToDictionary(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Entry),
            parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (arguments[parameter.Parameter.Ordinal]));
        for (var ordinal = 0; ordinal < callee.SourceCallPreconditions.Length; ordinal++)
        {
            SpendRegion();
            if (!_calls!.Spend())
            { throw new RegionIncompleteException(); }
            var clause = callee.SourceCallPreconditions[ordinal];
            var safe = IrSubstitution.Substitute(_context.Factory, clause.Safe, replacements);
            var value = IrSubstitution.Substitute(_context.Factory, clause.Value, replacements);
            var identity = CompilerIdentityBridge.CreateSymbolDisplay(callee.Target);
            if (!IrCallPreconditionMarker.TryCreateName(identity, ordinal,
                _context.Factory.GetOperationInfo(site).SourceSpan,
                _context.Factory.GetOperationInfo(clause.ClauseSite).SourceSpan, out var name))
            { throw new RegionIncompleteException(); }
            // A false precondition is evidence, not a runtime branch or an
            // imported assumption. The callee still executes normally.
            var marker = _builder.Assign(block, site, _context.Temporary(_context.Factory.BooleanType, name),
                _context.Factory.Binary(IrBinaryOperator.AndAlso, safe, value));
            _callPreconditions.Add(marker, new(identity, ordinal, clause.ClauseSite, value, safe, ancestry: ancestry));
        }
    }

    private void LowerSharedFrame(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        if (_calls == null || !_calls.Enter(_context.Target))
        { throw new RegionIncompleteException(); }
        try
        {
            LowerCore(graph);
            if (_abstentions.Count != 0)
            { throw new RegionIncompleteException(); }
        }
        finally
        { _calls.Leave(_context.Target); }
    }

    private void ImportExplicitThrowTypes(RoslynTotalProgramLowerer callee)
    {
        callee = RepositoryParameterGuard.NotNull(callee, nameof(callee));
        foreach (var entry in callee._explicitThrowTypes)
        {
            SpendRegion();
            // Source frames use declaration symbols. An unresolved generic
            // throw type cannot exclude a caller's constructed catch type.
            if (CompilerIdentityBridge.IsClosedReferenceType(entry.Value.Type, _cancellationToken))
            { _explicitThrowTypes[entry.Key] = entry.Value; }
        }
    }

    private IrBlockId ContinueSourceException(ControlFlowRegion? callerRegion, RegionFilter? callerFilter,
        IrExceptionKind kind, OperationId site, Func<IrBlockId, IrBlockId> unwind)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        unwind = RepositoryParameterGuard.NotNull(unwind, nameof(unwind));
        SpendRegion();
        if (callerFilter != null)
        { return unwind(callerFilter.Rejected); }
        if (callerRegion != null)
        { return RegionExceptionSearch(callerRegion, Token(kind, site), unwind); }
        return _frame != null ? _frame.Search(kind, site, unwind) : unwind(_ordinaryExceptionalExit);
    }

    private bool HasEnclosingCatchFilter(ControlFlowRegion? region)
    {
        for (; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { Kind: ControlFlowRegionKind.TryAndCatch } parent)
            { continue; }
            foreach (var handler in parent.NestedRegions)
            {
                SpendRegion();
                if (handler.Kind == ControlFlowRegionKind.FilterAndHandler)
                { return true; }
            }
        }
        return false;
    }

}
