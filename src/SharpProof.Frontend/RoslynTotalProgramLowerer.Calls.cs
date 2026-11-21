using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private sealed class SourceCallFrame(IrBlockId entry, Action<IrBlockId, OperationId, IrTerm?> returned,
        Func<IrExceptionKind, OperationId, Func<IrBlockId, IrBlockId>, IrBlockId> search)
    {
        private readonly Func<IrExceptionKind, OperationId, Func<IrBlockId, IrBlockId>, IrBlockId> _guarded_search = search ?? throw new System.ArgumentNullException(nameof(search));
        private readonly Action<IrBlockId, OperationId, IrTerm?> _guarded_returned = returned ?? throw new System.ArgumentNullException(nameof(returned));
        internal IrBlockId Entry { get; } = entry.IsDefault ? throw new ArgumentOutOfRangeException(nameof(entry)) : entry;
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
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                !CSharpOperationSemantics.IsValueDomain(parameter.Type)) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType))
        { return null; }
        var ordinals = ImmutableHashSet.CreateBuilder<int>();
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
        var arguments = ImmutableArray.CreateBuilder<IrTerm>(method.Parameters.Length);
        arguments.Count = method.Parameters.Length;
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
        var member = _context.Factory.GetOrCreateMemberSnapshot(
            CompilerIdentityBridge.InternSymbol(_context.Factory, method), _context.Type(method.ContainingType),
            "shadow-call:" + CompilerIdentityBridge.CreateSymbolDisplay(method), resultType, true,
            method.Parameters.Select(parameter => _context.Type(parameter.Type)).ToImmutableArray());
        var call = _builder.CallSnapshot(block, _context.Site(invocation), target, member, null, arguments.ToImmutable());
        _preservedSourceCalls.AddUnique(call, method.OriginalDefinition);
        return new(value, block, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? InlineSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (CSharpOperationSemantics.IsChainedConstructorCall(invocation, _context.Target))
        { return InlineChainedConstruction(invocation, block, depth); }
        if (_calls?.PrepareScalarCall(invocation) is { } model)
        { return _expressions.LowerScalarCall(invocation, model, block, depth); }
        if (_preserveSourceCall != null)
        { return PreserveSourceCall(invocation, block, depth); }
        // Claim lowering takes a metadata body it cannot expand as an opaque
        // call, rather than giving up on the caller's whole body.
        if (invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty && _calls?.PrepareIl(invocation.TargetMethod) is { } body &&
            (!_expressions.AllowOpaqueCalls || TotalIlStack.TryValidate(body, _calls.Spend, out _)))
        { return InlineMetadataCall(invocation, body, block, depth); }
        if (ExactVirtualTarget(invocation.TargetMethod, invocation.Instance, baseCall: !invocation.IsVirtual) is { } exact)
        { return InlineSourceMember(invocation, exact, invocation.Instance, invocation.Arguments, block, depth, exactTarget: true); }
        return InlineSourceMember(invocation, invocation.TargetMethod, invocation.Instance, invocation.Arguments, block, depth);
    }

    // A constructor body whose `this` is only an unobserved receiver: every
    // use reads or writes one of its fields, calls object's constructor, or
    // chains with `: this(...)` or `: base(...)` to a constructor that does
    // the same. An implicitly declared class constructor runs initializers,
    // which cannot read `this`, then its base class's constructor.
    internal static bool KeepsConstructedReceiver(IOperation body, Compilation? compilation, int depth, CancellationToken cancellationToken)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        return body.Descendants().OfType<IInstanceReferenceOperation>().All(receiver =>
            receiver.Parent is IFieldReferenceOperation field && field.Instance == receiver ||
            // A get-only auto-property names its backing field, as an
            // auto-property of a mutable modeled struct does.
            receiver.Parent is IPropertyReferenceOperation property && property.Instance == receiver &&
                (CSharpOperationSemantics.GetOnlyAutoPropertyField(property.Property) != null ||
                    CSharpOperationSemantics.StructAutoPropertyField(property.Property) != null) ||
            CSharpOperationSemantics.IsObjectConstructorCall(receiver.Parent) ||
            receiver.Parent is IInvocationOperation chained && chained.Instance == receiver &&
                (SymbolEqualityComparer.Default.Equals(chained.TargetMethod.ContainingType, receiver.Type) ||
                    CSharpOperationSemantics.IsSourceBaseConstructorCall(chained.TargetMethod, receiver.Type as INamedTypeSymbol)) &&
                Chained(chained.TargetMethod, depth));

        bool Chained(IMethodSymbol target, int level)
        {
            if (target.MethodKind != MethodKind.Constructor || level >= 16)
            { return false; }
            if (target.IsImplicitlyDeclared && target.ContainingType.TypeKind == TypeKind.Class)
            {
                return CSharpOperationSemantics.TryImplicitBaseConstructor(target.ContainingType, cancellationToken, out var implicitBase) &&
                    (implicitBase == null || Chained(implicitBase, level + 1));
            }
            if (target.IsImplicitlyDeclared)
            { return target.ContainingType.TypeKind == TypeKind.Struct; }
            return compilation != null && target.DeclaringSyntaxReferences.Length == 1 &&
                target.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is { } syntax &&
                compilation.ContainsSyntaxTree(syntax.SyntaxTree) &&
                CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree).GetOperation(syntax, cancellationToken) is { } targetBody &&
                KeepsConstructedReceiver(targetBody, compilation, level + 1, cancellationToken);
        }
    }

    // The body a dispatched call runs when the program text fixes it: a base
    // call, a sealed override or a member of a sealed type, or a receiver
    // whose static type is sealed, whose runtime type is then exactly that
    // type (its final override along the base types). Null for any other
    // virtual, abstract or interface member, and for a nonvirtual one, which
    // needs no resolution.
    internal static IMethodSymbol? ExactVirtualTarget(IMethodSymbol method, IOperation? instance, bool baseCall)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        if (!(method.IsVirtual || method.IsOverride || method.IsAbstract) || instance == null ||
            method.ContainingType.TypeKind == TypeKind.Interface)
        { return null; }
        if (baseCall)
        { return method.IsAbstract ? null : method; }
        if (!method.IsAbstract && (method.IsSealed || method.ContainingType.IsSealed))
        { return method; }
        if (instance.Type is not INamedTypeSymbol { IsSealed: true, TypeKind: TypeKind.Class or TypeKind.Struct } receiver)
        { return null; }
        for (INamedTypeSymbol? type = receiver; type != null; type = type.BaseType)
        {
            foreach (var candidate in type.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                for (var overridden = candidate; overridden != null; overridden = overridden.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, method.OriginalDefinition))
                    { return candidate.IsAbstract ? null : candidate; }
                }
            }
        }
        return null;
    }

    // `left + right` with an operator declared in source calls it like a
    // static method of its parameters, left to right.
    private TotalBodyValue? InlineSourceOperator(IOperation operation, IMethodSymbol method, ImmutableArray<IOperation> operands,
        IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (operands.IsDefault)
        { throw new ArgumentNullException(nameof(operands)); }
        return _preserveSourceCall != null ? null : InlineSourceMember(operation, method, null, [], block, depth, operands: operands);
    }

    // `new C(arguments)` evaluates the arguments, allocates the object and
    // runs instance initializers, the constructor body, then object initializers.
    // A body CFG does not contain the separate field/property initializers.
    // A class with source base classes runs its own initializers before the
    // base constructor, which the body graph calls first. Any other declared
    // class constructor runs as an unknown call after its preconditions.
    private TotalBodyValue? InlineSourceConstruction(IObjectCreationOperation creation, IrBlockId block, int depth)
    {
        if (_preserveSourceCall != null || creation.Constructor is not { } constructor)
        { return null; }
        var directConstructor = CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken);
        var canCallOpaque = _expressions.AllowOpaqueCalls && !constructor.IsImplicitlyDeclared &&
            constructor.ContainingType.TypeKind == TypeKind.Class;
        var opaqueConstructor = false;
        if (!directConstructor && !canCallOpaque)
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
        {
            if (!canCallOpaque || constructor.ContainingType.BaseType?.SpecialType == SpecialType.System_Object)
            { return null; }
            // Keep the existing opaque construction when a newly admitted
            // chain's own initializers cannot be separated from its body.
            directConstructor = false;
            opaqueConstructor = true;
        }
        var valueStruct = CSharpOperationSemantics.IsModeledStruct(creation.Type);
        if (valueStruct && constructor.IsImplicitlyDeclared)
        {
            return creation.Arguments.IsEmpty && (creation.Initializer?.Initializers ?? []).IsEmpty
                ? new(_context.Factory.Null(_context.Type(creation.Type)), block, FrontendSubsetClassification.Exact)
                : null;
        }
        IrTerm? created = null;
        IrBlockId Allocate(IrBlockId current)
        {
            RepositoryParameterGuard.ThrowIfInvalid(current.IsDefault, nameof(current));
            var allocatedType = _context.Type(creation.Type);
            if (valueStruct)
            { _context.Factory.RegisterValueStructType(allocatedType); }
            var initializeDefaults = valueStruct;
            if (!valueStruct && CSharpOperationSemantics.CompleteObjectStorage(creation.Type, _cancellationToken) is { } storage &&
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
                    if (!_expressions.StoreInitializer(field, created, creation, value, current, zeroInitialization: true))
                    { throw new RegionIncompleteException(); }
                }
            }
            // An object of a class with source base classes starts with every
            // field of every class zeroed too. A field whose storage is not
            // modeled is left unknown, which covers its zero.
            else if (directConstructor && !valueStruct &&
                creation.Type is INamedTypeSymbol { BaseType.SpecialType: not SpecialType.System_Object } derived)
            {
                for (INamedTypeSymbol? level = derived; level is { SpecialType: not SpecialType.System_Object }; level = level.BaseType)
                {
                    foreach (var member in level.GetMembers())
                    {
                        if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
                        { continue; }
                        SpendRegion();
                        var value = CSharpOperationSemantics.DefaultValue(_context.Factory, _context.Type(field.Type));
                        _ = _expressions.StoreInitializer(field, created, creation, value, current, zeroInitialization: true);
                    }
                }
            }
            // An initializer cannot read `this`, so the object is still unseen.
            return StoreConstructionInitializers(initializers, created, current, depth, fresh: true);
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
            if (constructor.ContainingType.BaseType?.SpecialType == SpecialType.System_Object)
            {
                block = Allocate(block);
                return Complete(block);
            }
            // The implicit constructor of a class with a source base class runs
            // this class's initializers, then the base class's constructor.
            var baseInitializers = ImmutableArray<(IFieldSymbol Field, IOperation Value)>.Empty;
            if (!CSharpOperationSemantics.TryImplicitBaseConstructor(constructor.ContainingType, _cancellationToken, out var baseConstructor) ||
                baseConstructor == null ||
                !baseConstructor.IsImplicitlyDeclared && !TryDeclaredBaseInitializers(baseConstructor, out baseInitializers))
            { return null; }
            var based = baseConstructor.IsImplicitlyDeclared
                ? InlineImplicitConstruction(creation, baseConstructor.ContainingType, () => created, block, depth, fresh: true, allocate: Allocate)
                : InlineSourceMember(creation, baseConstructor, null, [], block, depth,
                    prelude: InitializeBase,
                    constructedReceiver: () => created);
            if (based is not { } ran)
            { return null; }
            return ran.Classification.IsExact ? Complete(ran.Continuation) : new(created!, ran.Continuation, ran.Classification);

            IrBlockId InitializeBase(IrBlockId current)
            {
                current = Allocate(current);
                return StoreConstructionInitializers(baseInitializers, created!, current, depth, fresh: true);
            }
        }
        if (InlineSourceMember(creation, constructor, null, creation.Arguments, block, depth, prelude: Allocate,
            constructedReceiver: () => created, opaqueConstructor: opaqueConstructor) is not { } body)
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

    // `: this(arguments)` evaluates its arguments, then runs the other
    // constructor on the object under construction: its instance
    // initializers, unless it chains on, and its body. The object was
    // allocated and zeroed when the outermost constructor was entered. The
    // implicit parameterless constructor of a struct only zeroes it.
    //
    // `: base(arguments)`, written or implicit, to a source base class runs
    // the same way: C# has already run this class's initializers, then
    // evaluates the arguments and runs the base constructor on the object:
    // the base class's initializers, its own base constructor, then its
    // body. This constructor's body follows. The object's runtime type may
    // be a more derived class, whose override a virtual call on `this`
    // would run, so a base constructor that makes one is refused.
    private TotalBodyValue? InlineChainedConstruction(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var constructor = invocation.TargetMethod;
        if (_preserveSourceCall != null || _context.ReceiverValue() is not { } receiver)
        { return null; }
        var baseChain = !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, _context.Target.ContainingType);
        if (constructor.IsImplicitlyDeclared)
        {
            if (CSharpOperationSemantics.IsModeledStruct(constructor.ContainingType))
            {
                return invocation.Arguments.IsEmpty
                    ? new(_context.Factory.Boolean(false), block, FrontendSubsetClassification.Exact) : null;
            }
            return baseChain && invocation.Arguments.IsEmpty && constructor.ContainingType.TypeKind == TypeKind.Class
                ? InlineImplicitConstruction(invocation, constructor.ContainingType, () => receiver, block, depth, _expressions.FreshReceiver)
                : null;
        }
        if (!CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken) ||
            baseChain && CSharpOperationSemantics.ConstructorDispatchesOnThis(constructor, _context.Compilation, _cancellationToken) ||
            !TryConstructionInitializers(constructor, out var initializers))
        { return null; }
        var fresh = _expressions.FreshReceiver;
        return InlineSourceMember(invocation, constructor, null, invocation.Arguments, block, depth,
            prelude: current => StoreConstructionInitializers(initializers, receiver, current, depth, fresh),
            constructedReceiver: () => receiver);
    }

    // An implicitly declared class constructor runs its class's instance
    // initializers on the object, then its base class's parameterless
    // constructor, down to object's, which does nothing. Every implicit
    // level is checked before any of them emits a store.
    private TotalBodyValue? InlineImplicitConstruction(IOperation site, INamedTypeSymbol type, Func<IrTerm?> constructedReceiver,
        IrBlockId block, int depth, bool fresh, Func<IrBlockId, IrBlockId>? allocate = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        var levels = ImmutableArray.CreateBuilder<ImmutableArray<(IFieldSymbol Field, IOperation Value)>>();
        IMethodSymbol? declaredBase = null;
        for (var current = type; ;)
        {
            SpendRegion();
            if (levels.Count >= 16 || _calls == null || !_calls.HasNoTypeInitialization(current) ||
                current.InstanceConstructors.FirstOrDefault(static candidate => candidate.IsImplicitlyDeclared && candidate.Parameters.IsEmpty) is not { } implicitConstructor ||
                !TryConstructionInitializers(implicitConstructor, out var initializers) ||
                !CSharpOperationSemantics.TryImplicitBaseConstructor(current, _cancellationToken, out var next))
            { return null; }
            levels.Add(initializers);
            if (next == null)
            { break; }
            if (!next.IsImplicitlyDeclared)
            {
                declaredBase = next;
                break;
            }
            current = next.ContainingType;
        }
        var baseInitializers = ImmutableArray<(IFieldSymbol Field, IOperation Value)>.Empty;
        if (declaredBase != null && !TryDeclaredBaseInitializers(declaredBase, out baseInitializers))
        { return null; }
        if (declaredBase == null)
        { return new(_context.Factory.Boolean(false), Initialize(block), FrontendSubsetClassification.Exact); }
        return InlineSourceMember(site, declaredBase, null, [], block, depth,
            prelude: Initialize, constructedReceiver: constructedReceiver);

        IrBlockId Initialize(IrBlockId current)
        {
            if (allocate != null)
            { current = allocate(current); }
            var receiver = constructedReceiver()!;
            foreach (var initializers in levels)
            { current = StoreConstructionInitializers(initializers, receiver, current, depth, fresh); }
            return StoreConstructionInitializers(baseInitializers, receiver, current, depth, fresh);
        }
    }

    // A declared base class constructor the lowerer runs on an object of a
    // derived class, and the initializers it runs first.
    private bool TryDeclaredBaseInitializers(IMethodSymbol constructor, out ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers)
    {
        initializers = [];
        return CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken) &&
            !CSharpOperationSemantics.ConstructorDispatchesOnThis(constructor, _context.Compilation, _cancellationToken) &&
            TryConstructionInitializers(constructor, out initializers);
    }

    // Instance initializers store into the object in declaration order. They
    // cannot read `this`.
    private IrBlockId StoreConstructionInitializers(ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers, IrTerm receiver,
        IrBlockId current, int depth, bool fresh)
    {
        RepositoryParameterGuard.ThrowIfInvalid(current.IsDefault, nameof(current));
        receiver = RepositoryParameterGuard.NotNull(receiver, nameof(receiver));
        foreach (var (field, initializer) in initializers)
        {
            SpendRegion();
            var lowered = _expressions.LowerBodyValue(initializer, current, depth + 1);
            if (!lowered.Classification.IsExact ||
                !_expressions.StoreInitializer(field, receiver, initializer, lowered.Value, lowered.Continuation, fresh: fresh))
            { throw new RegionIncompleteException(); }
            current = lowered.Continuation;
        }
        return current;
    }

    private bool TryConstructionInitializers(IMethodSymbol constructor, out ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers)
    {
        initializers = [];
        if (CSharpOperationSemantics.IsPlainConstructor(constructor, _cancellationToken) ||
            CSharpOperationSemantics.ChainsToThis(constructor, _cancellationToken) ||
            !CSharpOperationSemantics.IsDirectObjectConstructor(constructor, true, _cancellationToken))
        { return true; }
        var type = constructor.ContainingType;
        if (_calls is not { } calls || _context.Compilation is not { } compilation || type.IsGenericType ||
            type.DeclaringSyntaxReferences.Length != 1 ||
            type.DeclaringSyntaxReferences[0].GetSyntax(_cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration)
        { return false; }
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        return TryConsumeConstructionInitializerValues(type, Harvest(), _cancellationToken, out initializers);

        IEnumerable<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> Harvest()
        {
            foreach (var member in declaration.Members)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!calls.Spend())
                { throw new ConstructionInitializerHarvestRejectedException(); }
                if (member is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field &&
                    !field.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) || modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword)))
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        if (!calls.Spend())
                        { throw new ConstructionInitializerHarvestRejectedException(); }
                        if (variable.Initializer == null)
                        { continue; }
                        yield return (true, model.GetOperation(variable.Initializer, _cancellationToken), null);
                    }
                }
                else if (member is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax { Initializer: { } value } property &&
                    !property.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)))
                {
                    var symbol = model.GetDeclaredSymbol(property, _cancellationToken) as IPropertySymbol;
                    var operation = symbol == null ? null : model.GetOperation(value, _cancellationToken);
                    yield return (false, operation, symbol);
                }
                else if (member is Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax)
                { throw new ConstructionInitializerHarvestRejectedException(); }
            }
        }
    }

    internal static bool TryConstructionInitializerValues(INamedTypeSymbol type,
        ImmutableArray<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> candidates,
        CancellationToken cancellationToken, out ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        return TryConsumeConstructionInitializerValues(type, candidates, cancellationToken, out initializers);
    }

    // Consume one compiler candidate at a time, validating before MoveNext can
    // spend on or query another declaration. Publish only the complete inventory.
    internal static bool TryConsumeConstructionInitializerValues(INamedTypeSymbol type,
        IEnumerable<(bool IsField, IOperation? Operation, IPropertySymbol? Property)> candidates,
        CancellationToken cancellationToken, out ImmutableArray<(IFieldSymbol Field, IOperation Value)> initializers)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        candidates = RepositoryParameterGuard.NotNull(candidates, nameof(candidates));
        cancellationToken.ThrowIfCancellationRequested();
        initializers = [];
        var values = ImmutableArray.CreateBuilder<(IFieldSymbol Field, IOperation Value)>();
        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryConstructionInitializerValue(type, candidate, cancellationToken, out var initializer))
                { return false; }
                values.Add(initializer);
            }
        }
        catch (ConstructionInitializerHarvestRejectedException)
        { return false; }
        initializers = values.ToImmutable();
        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Private initializer-harvest control flow.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064", Justification = "Private harvest rejection is caught by the atomic initializer consumer.")]
    private sealed class ConstructionInitializerHarvestRejectedException : Exception;

    private static bool TryConstructionInitializerValue(INamedTypeSymbol type,
        (bool IsField, IOperation? Operation, IPropertySymbol? Property) candidate,
        CancellationToken cancellationToken, out (IFieldSymbol Field, IOperation Value) value)
    {
        cancellationToken.ThrowIfCancellationRequested();
        value = default;
        if (candidate.IsField)
        {
            if (candidate.Operation is not IFieldInitializerOperation { InitializedFields.Length: 1 } initializer)
            { return false; }
            value = (initializer.InitializedFields[0], initializer.Value);
        }
        else
        {
            if (candidate.Property is not { } symbol || candidate.Operation is not IPropertyInitializerOperation initializer ||
                type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, symbol)) is not { } backing)
            { return false; }
            value = (backing, initializer.Value);
        }
        return true;
    }
    // A static whose type's initialization ran to completion at analysis
    // time holds the value it left, when that initialization never runs the
    // callable being lowered: the callable, and everything it calls, then
    // reaches the static only after the initialization has finished. A
    // callable the initialization does run may see the static before it is
    // set.
    private InitializedValue? InitializedStaticValue(IFieldSymbol field)
    {
        if (_preserveSourceCall != null || _calls == null || _context.Compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation ||
            !_calls.HasNoTypeInitialization(field.ContainingType) ||
            TypeInitialization.InitializedStatic(compilation, field, _cancellationToken) is not { } initialized ||
            _calls.Root is not { } root || initialized.Runs.Contains(root.OriginalDefinition))
        { return null; }
        return initialized.Value;
    }

    // A readonly static of a type whose initializer is admitted, holding an
    // object no code can change. The read runs that initializer, which has no
    // effect a body can observe, and then always yields the same object.
    private bool HoldsImmutableStatic(IFieldSymbol field)
    {
        return _preserveSourceCall == null && _calls != null &&
            _context.Compilation is Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation &&
            TypeInitialization.HoldsImmutableValue(compilation, field, _cancellationToken) &&
            _calls.HasNoTypeInitialization(field.ContainingType);
    }

    private TotalBodyValue? InlineSourceGetter(IPropertyReferenceOperation property, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (_preserveSourceCall != null || property.Property.GetMethod is not { } getter)
        { return null; }
        return ExactVirtualTarget(getter, property.Instance, baseCall: property.Instance?.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax) is { } exact
            ? InlineSourceMember(property, exact, property.Instance, property.Arguments, block, depth, exactTarget: true)
            : InlineSourceMember(property, getter, property.Instance, property.Arguments, block, depth);
    }

    // `target[arguments] = value` evaluates the receiver, the arguments and the
    // value, then runs the setter; the expression's value is the assigned one.
    private TotalBodyValue? InlineSourceSetter(ISimpleAssignmentOperation assignment, IrBlockId block, int depth)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        return _preserveSourceCall != null || assignment.Target is not IPropertyReferenceOperation { Property.SetMethod: { } setter } property
            ? null : InlineSourceMember(assignment, setter, property.Instance, property.Arguments, block, depth, assignment.Value);
    }

    private TotalBodyValue? InlineSourceMember(IOperation invocation, IMethodSymbol method, IOperation? instance,
        ImmutableArray<IArgumentOperation> callArguments, IrBlockId block, int depth, IOperation? assigned = null,
        Func<IrBlockId, IrBlockId>? prelude = null, Func<IrTerm?>? constructedReceiver = null, ImmutableArray<IOperation> operands = default,
        bool exactTarget = false, bool opaqueConstructor = false)
    {
        var shared = new HashSet<int>();
        var references = ReferenceArguments(callArguments, shared);
        var readOnly = ReadOnlyArguments(callArguments, references, shared);
        IrVarId? liveReceiver = null;
        IrVarId? writeBack = null;
        var copiesReceiver = false;
        if (constructedReceiver == null &&
            !TryStructReceiver(method, instance, references == null && readOnly == null ? null : [.. references ?? [], .. readOnly ?? []],
                out liveReceiver, out writeBack, out copiesReceiver))
        { return null; }
        // An unknown call could change the variable a mutating call is on,
        // so only an expanded body may write it back.
        if (_calls == null || opaqueConstructor && references != null ||
            !_calls.TryPrepare(_context, method, instance, callArguments, out var frame, out var graph, assigned != null,
                constructorInitializers: constructedReceiver != null, operands: operands, referenceArguments: references != null,
                exactTarget: exactTarget, contractOnly: opaqueConstructor, readOnlyArguments: readOnly != null,
                sharedReferences: [.. (references ?? []).Concat(readOnly ?? [])
                    .Where(item => shared.Contains(item.Ordinal)).Select(static item => (item.Ordinal, item.Local!.Value))]) &&
            (opaqueConstructor || !(_expressions.AllowOpaqueCalls && references == null && writeBack == null &&
                _calls.TryPrepare(_context, method, instance, callArguments, out frame, out graph, assigned != null, contractOnly: true,
                    constructorInitializers: constructedReceiver != null, operands: operands, exactTarget: exactTarget))))
        { return null; }
        var callee = frame!;
        callee.CopiesReceiver = copiesReceiver;
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
        if (instance != null && !_expressions.IsImplicitThis(instance) && liveReceiver == null)
        {
            var lowered = _expressions.LowerBodyValue(instance, block, depth + 1);
            if (!lowered.Classification.IsExact)
            { return new(marker, lowered.Continuation, lowered.Classification); }
            receiver = lowered.Value;
            block = lowered.Continuation;
        }
        // The callee's `this` is the receiver as evaluated before the
        // arguments; an unmodeled caller `this` is unknown. A struct variable
        // is passed by reference, so it is read once the arguments ran.
        // Construction binds its fresh receiver after argument evaluation and
        // allocation below; it has no caller receiver to approximate here.
        if (constructedReceiver == null && liveReceiver == null && callee.Receiver is { } self)
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
        var argumentBuffer = ImmutableArray.CreateBuilder<IrTerm>(callee.Parameters.Length);
        argumentBuffer.Count = callee.Parameters.Length;
        for (var ordinal = 0; !operands.IsDefault && ordinal < operands.Length; ordinal++)
        {
            SpendRegion();
            var value = _expressions.LowerReceiverValue(operands[ordinal], block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            argumentBuffer[ordinal] = Bridge(value.Value, _context.Factory.GetVariableInfo(callee.Parameters[ordinal].Entry).Type);
        }
        foreach (var argument in callArguments)
        {
            SpendRegion();
            var parameter = argument.Parameter!;
            // An out argument's value is never read: the callee assigns it
            // first. A ref argument passes the variable itself, so its value
            // is read when the callee is entered, after every argument.
            if (parameter.RefKind == RefKind.Out && !shared.Contains(parameter.Ordinal))
            {
                argumentBuffer[parameter.Ordinal] = CSharpOperationSemantics.DefaultValue(_context.Factory,
                    _context.Factory.GetVariableInfo(callee.Parameters[parameter.Ordinal].Entry).Type);
                continue;
            }
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && references != null ||
                parameter.RefKind == RefKind.In && readOnly?.Any(item => item.Ordinal == parameter.Ordinal && item.Local != null) == true)
            { continue; }
            if (TotalSourceCallSession.IsEmptyParamsArray(argument))
            {
                if (_calls.PrepareEmptyParamsArray(argument) is not { } prepared ||
                    prepared.Model.Apply([]) is not { Classification.IsExact: true, Throws.IsEmpty: true } empty)
                { return new(marker, block, FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape)); }
                _expressions.RecordScalarEffects(argument, prepared.Method, prepared.Model, [], block);
                argumentBuffer[parameter.Ordinal] = empty.Value;
                continue;
            }
            var value = _expressions.LowerReceiverValue(argument.Value, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            argumentBuffer[parameter.Ordinal] = Bridge(value.Value,
                _context.Factory.GetVariableInfo(callee.Parameters[parameter.Ordinal].Entry).Type);
            if (constructedReceiver != null)
            {
                var pinned = _context.Temporary(argumentBuffer[parameter.Ordinal].Type);
                _builder.Assign(block, site, pinned, argumentBuffer[parameter.Ordinal]);
                argumentBuffer[parameter.Ordinal] = _context.Factory.Variable(pinned);
            }
        }
        if (assigned != null)
        {
            var value = _expressions.LowerBodyValue(assigned, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            argumentBuffer[argumentBuffer.Count - 1] = Bridge(value.Value,
                _context.Factory.GetVariableInfo(callee.Parameters[callee.Parameters.Length - 1].Entry).Type);
            marker = value.Value;
        }
        // A shared parameter's entry is its reference cell itself.
        foreach (var (ordinal, local) in references ?? [])
        {
            if (local is { } storage && (method.Parameters[ordinal].RefKind == RefKind.Ref || shared.Contains(ordinal)))
            {
                argumentBuffer[ordinal] = Bridge(_context.Factory.Variable(storage),
                    _context.Factory.GetVariableInfo(callee.Parameters[ordinal].Entry).Type);
            }
        }
        foreach (var (ordinal, local) in readOnly ?? [])
        {
            if (local is { } storage)
            {
                argumentBuffer[ordinal] = Bridge(_context.Factory.Variable(storage),
                    _context.Factory.GetVariableInfo(callee.Parameters[ordinal].Entry).Type);
            }
        }
        var arguments = argumentBuffer.ToImmutable();
        if (liveReceiver is { } variable && callee.Receiver is { } entered)
        {
            _builder.Assign(block, site, entered.Entry, Bridge(_context.Factory.Variable(variable),
                _context.Factory.GetVariableInfo(entered.Entry).Type));
        }
        if (receiver != null && !CSharpOperationSemantics.IsModeledStruct(method.ContainingType))
        { block = _expressions.CheckReceiver(invocation, receiver, block); }
        if (prelude != null)
        { block = prelude(block); }
        if (constructedReceiver?.Invoke() is { } freshReceiver && callee.Receiver is { } constructorReceiver)
        { _builder.Assign(block, site, constructorReceiver.Entry, Bridge(freshReceiver, _context.Factory.GetVariableInfo(constructorReceiver.Entry).Type)); }
        ImmutableArray<TotalShadowCallHop> callAncestry = [];
        if (_captureShadowCallAncestry || _context.CaptureCallFrames)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var callSpan = _context.Factory.GetOperationInfo(site).SourceSpan!;
            callAncestry = _shadowAncestry.Add(new(CompilerIdentityBridge.CreateSymbolDisplay(_context.Target),
                CompilerIdentityBridge.CreateSymbolDisplay(callee.Target), callSpan));
        }
        RecordCallPreconditions(callee, arguments, block, site, callAncestry);
        // A recursive call checks its callee's preconditions, then runs the
        // callee as an unknown call: it may do anything, throw included. So
        // does a call whose callee body does not lower. An unknown call
        // could write a local it received by reference to anything, so such
        // a call is not expanded.
        if (graph == null)
        {
            return references != null || writeBack != null
                ? new(marker, block, FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape))
                : Opaque(inductiveRoot: callee.SourceCallPreconditions.IsEmpty && _calls.IsRecursiveRootCall(method));
        }
        if (externalFilterSearch)
        {
            if (!_calls.Spend(callee.Parameters.Length + 2))
            { throw new RegionIncompleteException(); }
            var continued = _builder.CreateBlock("call:continued");
            var composed = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch, shadowAncestry: callAncestry, captureShadowCallAncestry: _captureShadowCallAncestry)
            {
                _builder = _builder
            };
            composed.LowerSharedFrame(graph!, entry => new SourceCallFrame(entry.IsDefault ? throw new ArgumentOutOfRangeException(nameof(entry)) : entry, (source, operation, value) =>
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
                }));
            foreach (var obligation in composed._callPreconditions)
            { _callPreconditions.AddUnique(obligation.Key, obligation.Value); }
            foreach (var parameter in callee.Parameters)
            { _builder.Assign(block, site, parameter.Entry, arguments[parameter.Parameter.Ordinal]); }
            _builder.Goto(block, site, composed._frame!.Entry);
            WriteBack(continued);
            return new(marker, continued, FrontendSubsetClassification.Exact);
        }
        // A callee whose expansion runs out of work, or does not fit in what
        // is left, runs as the same unknown call as one whose body does not
        // lower: its preconditions were recorded above, and it may do
        // anything, throw included. The abandoned work is given back, within
        // the session's bounded allowance, so the caller's body can go on.
        // An unknown call could change the variable a mutating struct call
        // writes back, so such a call has no fallback.
        var fallback = _expressions.AllowOpaqueCalls && references == null && writeBack == null;
        var mark = _calls.Mark();
        var expanded = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch, shadowAncestry: callAncestry, captureShadowCallAncestry: _captureShadowCallAncestry);
        var lowering = expanded.Lower(graph!);
        if (!lowering.IsExact)
        {
            if (fallback && lowering.ConstructionLimitExceeded)
            { _ = _calls.TryRecover(mark); }
            return fallback ? Opaque() : new(marker, block, lowering.Classification);
        }
        var program = lowering.Program;
        var instructionCount = 0;
        foreach (var source in program.Blocks)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            instructionCount += source.Instructions.Length;
        }
        // Counting the blocks costs one step each, as copying them does.
        if (!_calls.Spend(2 * program.Blocks.Length + instructionCount + callee.Parameters.Length + 2))
        {
            if (fallback && _calls.TryRecover(mark))
            { return Opaque(); }
            throw new RegionIncompleteException();
        }
        ImportExplicitThrowTypes(expanded);
        var continuation = _builder.CreateBlock("call:continued");
        var blocks = ImmutableDictionaryCapture.Create(program.Blocks, source => source.Id, source => _builder.CreateBlock("call:body"));
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
                    case IrWriteInstruction { Target: { } named, Value: null } write:
                        _builder.Write(destination, write.Operation, write.Region, named);
                        break;
                    case IrWriteInstruction write:
                        _builder.Write(destination, write.Operation, write.Region);
                        break;
                    case IrHavocInstruction { Origin: IrHavocOrigin.Approximation } havoc:
                        _builder.Havoc(destination, havoc.Operation, havoc.HavocKind, havoc.Origin, [.. havoc.Variables]);
                        break;
                    case IrCallInstruction { Receiver: null, Target: null } opaque:
                        _builder.CallSnapshot(destination, opaque.Operation, null, opaque.Member, null, opaque.Arguments);
                        break;
                    case IrAssignInstruction assign:
                        var copied = _builder.Assign(destination, assign.Operation, assign.Target, assign.Value);
                        if (lowering.CallPreconditions.TryGetValue(assign, out var obligation))
                        { _callPreconditions.AddUnique(copied, obligation); }
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
        WriteBack(continuation);
        return new(marker, continuation, FrontendSubsetClassification.Exact);

        // On normal return a ref or out local holds what the callee last
        // assigned to the parameter.
        void WriteBack(IrBlockId continued)
        {
            foreach (var (ordinal, local) in references ?? [])
            {
                if (local is not { } storage || shared.Contains(ordinal))
                { continue; }
                var value = Bridge(_context.Factory.Variable(callee.Parameters[ordinal].Input.Current),
                    _context.Factory.GetVariableInfo(storage).Type);
                var write = _builder.Assign(continued, site, storage, value);
                _builder.Write(continued, write.Operation, IrWriteRegion.Local);
            }
            // So does the variable a mutating struct member was called on,
            // with the callee's final `this`.
            if (writeBack is { } receiverStorage && callee.Receiver is { } mutated)
            {
                var write = _builder.Assign(continued, site, receiverStorage,
                    Bridge(_context.Factory.Variable(mutated.Current), _context.Factory.GetVariableInfo(receiverStorage).Type));
                _builder.Write(continued, write.Operation, IrWriteRegion.Local);
            }
        }

        // A recursive call into a root without preconditions is marked so
        // that purity alone may assume it by induction; see
        // IrOpaqueCallSite.DescribeInductiveRootCall.
        TotalBodyValue Opaque(bool inductiveRoot = false)
        {
            var called = _expressions.EmitOpaqueCall(invocation, method, arguments, block, assigned != null ? marker : null,
                IrOpaqueCallEffects.All, inductiveRoot);
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

    // The ref and out arguments of a call, with the caller variable each one
    // writes back to (none for a discard), when every one is a local or
    // parameter of the caller passed once and no handler of the caller can
    // observe a variable the call
    // leaves half written: an exception that leaves this frame ends its
    // locals. Null when the call has no such argument or one is not admitted.
    private ImmutableArray<(int Ordinal, IrVarId? Local)>? ReferenceArguments(ImmutableArray<IArgumentOperation> arguments)
    {
        return ReferenceArguments(arguments, null);
    }

    // With `shared`, a source callee may also take the caller's own ref or
    // out parameter that is the caller's caller's storage (a root's, or one
    // an inlined caller itself shares); its ordinal is added to `shared`.
    // The callee's parameter is then that same reference cell, not a copy:
    // each store the callee makes to it is the caller's store, so the
    // root's alias rules (a store is a write to argument state, and any
    // write forgets every by-reference parameter) apply to it as they do to
    // the root's own body, on every path out of the call. It is not
    // written back.
    private ImmutableArray<(int Ordinal, IrVarId? Local)>? ReferenceArguments(ImmutableArray<IArgumentOperation> arguments, HashSet<int>? shared)
    {
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        var references = ImmutableArray.CreateBuilder<(int Ordinal, IrVarId? Local)>();
        var variables = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { RefKind: RefKind.Ref or RefKind.Out } parameter)
            { continue; }
            for (var region = _regionGraph == null ? null : _regionSource.EnclosingRegion; region != null; region = region.EnclosingRegion)
            {
                if (region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime))
                { return null; }
            }
            var value = argument.Value is IDeclarationExpressionOperation { Expression: var declared } ? declared : argument.Value;
            if (value is IDiscardOperation && parameter.RefKind == RefKind.Out)
            {
                references.Add((parameter.Ordinal, null));
                continue;
            }
            ISymbol? symbol = value switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation owned => owned.Parameter,
                _ => null
            };
            // A root's own ref or out parameter may be a field, an element
            // or another parameter's variable; a copy-backed callee would
            // treat it as independent storage.
            if (symbol == null || !variables.Add(symbol) || !_expressions.TryReferenceArgumentStorage(value, out var storage))
            { return null; }
            if (symbol is IParameterSymbol callerReference && _context.IsCallerStorageReference(callerReference))
            {
                if (shared == null)
                { return null; }
                _ = shared.Add(parameter.Ordinal);
            }
            references.Add((parameter.Ordinal, storage));
        }
        return references.Count == 0 ? null : references.ToImmutable();
    }

    // The `in` arguments of a call, with the caller variable whose value
    // each one passes (none for a value read where the argument is). C#
    // passes the place itself, which the callee cannot write, so the callee
    // sees the place's value at each read; a copy is the same only for a
    // place nothing can write while the call runs:
    //  - a value that is not a variable, which C# copies into a temporary;
    //  - a local or by-value parameter of the caller, not passed by
    //    reference or as a struct receiver in the same call, which only the
    //    caller writes (no anonymous or local function may write a variable
    //    it captures); its value is read once the arguments ran, as a ref
    //    argument's is;
    //  - a readonly field of a class or a static readonly field, read
    //    outside every constructor and init accessor, which no other code
    //    can store into.
    // The caller's own `in` parameter, or a parameter that is its caller's
    // storage, is not copied but shared, as such a ref argument is (see
    // ReferenceArguments): the context that owns it forgets its current
    // value after any write that may alias it. One such variable is shared
    // with at most one callee parameter. Any other place (a mutable
    // field, an element, a ref local, `this` of a struct) may change through
    // an alias the callee writes. Null when the call has no `in` argument or
    // one is not admitted.
    private ImmutableArray<(int Ordinal, IrVarId? Local)>? ReadOnlyArguments(ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<(int Ordinal, IrVarId? Local)>? references, HashSet<int> shared)
    {
        RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
        var admitted = ImmutableArray.CreateBuilder<(int Ordinal, IrVarId? Local)>();
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { RefKind: RefKind.In } parameter)
            { continue; }
            switch (argument.Value)
            {
                case ILocalReferenceOperation or IParameterReferenceOperation:
                    var owned = (argument.Value as IParameterReferenceOperation)?.Parameter;
                    var sharing = owned is { RefKind: not RefKind.None };
                    if (sharing && !(owned!.RefKind == RefKind.In && _context.OwnsParameter(owned) || _context.IsCallerStorageReference(owned)) ||
                        !_expressions.TryReferenceArgumentStorage(argument.Value, out var storage) ||
                        references?.Any(reference => reference.Local == storage) == true ||
                        sharing && admitted.Any(other => other.Local == storage))
                    { return null; }
                    if (sharing)
                    { _ = shared.Add(parameter.Ordinal); }
                    admitted.Add((parameter.Ordinal, storage));
                    break;
                case IFieldReferenceOperation { Field: { IsReadOnly: true } field }
                    when (field.IsStatic || field.ContainingType.IsReferenceType) &&
                        _context.Target.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor) && !_context.Target.IsInitOnly:
                    admitted.Add((parameter.Ordinal, null));
                    break;
                case var value when !IsVariable(value):
                    admitted.Add((parameter.Ordinal, null));
                    break;
                default:
                    return null;
            }
        }
        return admitted.Count == 0 ? null : admitted.ToImmutable();

        // An operation that may name storage C# can pass by reference. An
        // operation without a public kind, such as a pointer indirection,
        // counts as one.
        static bool IsVariable(IOperation value)
        {
            return value.Kind == OperationKind.None || value is ILocalReferenceOperation or IParameterReferenceOperation or
                IFieldReferenceOperation or IArrayElementReferenceOperation or IInstanceReferenceOperation or
                IInlineArrayAccessOperation or IImplicitIndexerReferenceOperation or
                IDynamicMemberReferenceOperation or IDynamicIndexerAccessOperation or IFlowCaptureReferenceOperation or
                IInvocationOperation { TargetMethod.RefKind: not RefKind.None } or
                IPropertyReferenceOperation { Property.RefKind: not RefKind.None } or
                IConditionalOperation { IsRef: true } or ISimpleAssignmentOperation { IsRef: true } ||
                value is IConversionOperation { Conversion.IsIdentity: true, Operand: var operand } && IsVariable(operand);
        }
    }

    // How `this` of an instance member of a modeled struct reaches the
    // callee. C# passes a variable by reference, so the callee's `this` is
    // read from it once the arguments ran (live). A member that is not
    // readonly may change it: the callee gets a copy and, on normal return,
    // the caller assigns the callee's final `this` to the variable (write
    // back). Outside every try region an exception leaves the variable
    // unread, as for a ref argument. On a value, such as a call result, a
    // readonly local or `this` of a readonly member, C# itself calls on a
    // copy, which a mutation changes unseen. False for a mutating call on any
    // other place: a field of an object, an element, a ref local, or the
    // caller's storage that a root struct member's `this` is.
    private bool TryStructReceiver(IMethodSymbol method, IOperation? instance, ImmutableArray<(int Ordinal, IrVarId? Local)>? references,
        out IrVarId? live, out IrVarId? writeBack, out bool copies)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        live = null;
        writeBack = null;
        copies = false;
        if (instance == null || method.IsStatic || method.MethodKind == MethodKind.Constructor ||
            !CSharpOperationSemantics.IsModeledStruct(method.ContainingType) ||
            !SymbolEqualityComparer.Default.Equals(instance.Type, method.ContainingType))
        { return true; }
        var mutating = CSharpOperationSemantics.IsMutableModeledStruct(method.ContainingType) && !method.IsReadOnly;
        if (_expressions.TryStructStorage(instance, out var storage))
        {
            // A ref argument naming the same variable would alias `this`.
            if (references?.Any(reference => reference.Local == storage) == true ||
                mutating && !OutsideTryRegions())
            { return false; }
            live = storage;
            writeBack = mutating ? storage : null;
            copies = true;
            return true;
        }
        // `this` of a readonly member is the caller's storage, which a call
        // C# makes on a copy of it leaves alone.
        var copied = _expressions.IsCopiedStructReceiver(instance);
        copies = copied && (mutating || !_expressions.IsImplicitThis(instance));
        // A member that cannot change `this` gets the caller's `this` by
        // reference too, so it reads it once the arguments ran, which may
        // have changed the storage it names. C# copies `this` for a mutating
        // member before the arguments run.
        if (!mutating && _expressions.IsImplicitThis(instance) && _context.Receiver is { } self)
        { live = self.Current; }
        return copied || !mutating;
    }

    // Whether this frame lowers the current block outside every try region.
    private bool OutsideTryRegions()
    {
        for (var region = _regionGraph == null ? null : _regionSource.EnclosingRegion; region != null; region = region.EnclosingRegion)
        {
            if (region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime))
            { return false; }
        }
        return true;
    }

    private IrTerm Bridge(IrTerm value, IrTypeId type)
    { RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type)); return _context.BridgeValue(value, type); }

    private void RecordCallPreconditions(TotalLoweringContext callee, ImmutableArray<IrTerm> arguments, IrBlockId block, OperationId site, ImmutableArray<TotalShadowCallHop> ancestry)
    {

        RepositoryParameterGuard.RequireInitialized(ancestry.IsDefault, nameof(ancestry), "Array must be initialized.");
        RepositoryParameterGuard.ThrowIfInvalid(arguments.IsDefault, nameof(arguments));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (callee.SourceCallPreconditions.IsEmpty)
        { return; }
        var replacements = ImmutableDictionaryCapture.Create(callee.Parameters, parameter => parameter.Entry,
            parameter => arguments[parameter.Parameter.Ordinal]);
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
            _callPreconditions.AddUnique(marker, new(identity, ordinal, clause.ClauseSite, value, safe, ancestry: ancestry));
        }
    }

    private void LowerSharedFrame(ControlFlowGraph graph, Func<IrBlockId, SourceCallFrame> createFrame)
    {
        if (_calls == null || !_calls.Enter(_context.Target))
        { throw new RegionIncompleteException(); }
        try
        {
            LowerCore(graph, createFrame);
            if (_abstentions.Count != 0)
            { throw new RegionIncompleteException(); }
        }
        finally
        { _calls.Leave(_context.Target); }
    }

    private void ImportExplicitThrowTypes(RoslynTotalProgramLowerer callee)
    {
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
