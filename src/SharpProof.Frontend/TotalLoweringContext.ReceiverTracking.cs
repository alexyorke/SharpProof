namespace SharpProof.Frontend;

internal sealed partial class TotalLoweringContext
{
    private readonly ImmutableHashSet<ISymbol>.Builder _entryNullableDefaultComparers = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
    private readonly ImmutableHashSet<ISymbol>.Builder _nullableDefaultComparers = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
    private readonly ImmutableHashSet<CaptureId>.Builder _nullableDefaultComparerCaptures = ImmutableHashSet.CreateBuilder<CaptureId>();

    private readonly ImmutableDictionary<ISymbol, IArrayTypeSymbol>.Builder _arrayReceivers = ImmutableDictionary.CreateBuilder<ISymbol, IArrayTypeSymbol>(SymbolEqualityComparer.Default);
    private readonly ImmutableDictionary<CaptureId, IArrayTypeSymbol>.Builder _arrayCaptures = ImmutableDictionary.CreateBuilder<CaptureId, IArrayTypeSymbol>();
    private readonly ImmutableDictionary<ITypeSymbol, ITypeSymbol>.Builder _receiverElements = ImmutableDictionary.CreateBuilder<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);

    private readonly ImmutableArray<ISymbol>.Builder _entryNullableDefaultComparerOrder = ImmutableArray.CreateBuilder<ISymbol>();
    private readonly ImmutableArray<ITypeSymbol>.Builder _receiverElementOrder = ImmutableArray.CreateBuilder<ITypeSymbol>();

    // These are representation facts, not interface contracts. Every definition
    // must have the same scalar SZ-array representation. Joins with an unknown
    // implementation, cycles, ref escapes and captured mutable storage abstain.
    internal void TrackArrayReceivers(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        // Every resolution ends at a scalar array value or at a receiver
        // already known; with neither, the pass would record nothing.
        if (!TrackingBases(graph).Arrays && _arrayReceivers.Count == 0 && _arrayCaptures.Count == 0)
        { return; }
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue is { } branchValue ? ImmutableArray.Create(branchValue) : ImmutableArray<IOperation>.Empty)))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).ToImmutableArray();
        var definitions = ImmutableDictionary.CreateBuilder<ISymbol, ImmutableArray<IOperation>.Builder>(SymbolEqualityComparer.Default);
        var definitionOrder = ImmutableArray.CreateBuilder<ISymbol>();
        var captures = ImmutableDictionary.CreateBuilder<CaptureId, ImmutableArray<IOperation>.Builder>();
        var captureOrder = ImmutableArray.CreateBuilder<CaptureId>();
        var excluded = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var operation in graph.OriginalOperation.DescendantsAndSelf())
        {
            // Source writes also guard assignments whose CFG target is captured.
            // Deconstruction can hide several storage targets: abstain for them.
            if (operation is IDeconstructionAssignmentOperation deconstruction)
            {
                foreach (var reference in deconstruction.Target.DescendantsAndSelf())
                { Exclude(reference); }
            }
            else if (operation is IAssignmentOperation assignment && Symbol(assignment.Target) is { } target)
            {
                if (!definitions.TryGetValue(target, out var values))
                { definitions.Add(target, values = ImmutableArray.CreateBuilder<IOperation>()); definitionOrder.Add(target); }
                values.Add(assignment.Value);
            }
            if (operation is IArgumentOperation { Parameter.RefKind: not RefKind.None } argument)
            { Exclude(argument.Value); }
            if (operation is IAnonymousFunctionOperation ||
                operation is ILocalFunctionOperation localFunction && !SymbolEqualityComparer.Default.Equals(localFunction.Symbol, Target))
            {
                foreach (var reference in operation.Descendants())
                { Exclude(reference); }
            }
        }
        foreach (var operation in operations)
        {
            if (operation is ISimpleAssignmentOperation assignment && Symbol(assignment.Target) is { } target)
            {
                if (!definitions.TryGetValue(target, out var values))
                { definitions.Add(target, values = ImmutableArray.CreateBuilder<IOperation>()); definitionOrder.Add(target); }
                values.Add(assignment.Value);
            }
            if (operation is IFlowCaptureOperation capture)
            {
                if (!captures.TryGetValue(capture.Id, out var values))
                { captures.Add(capture.Id, values = ImmutableArray.CreateBuilder<IOperation>()); captureOrder.Add(capture.Id); }
                values.Add(capture.Value);
            }
        }
        var remaining = 4096;
        foreach (var definitionKey in definitionOrder)
        {
            var pair = new KeyValuePair<ISymbol, ImmutableArray<IOperation>.Builder>(definitionKey, definitions[definitionKey]);
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local && IsArrayInterface(local.Type) &&
                !excluded.Contains(local) && ResolveDefinitions(pair.Value, 0) is { } array)
            { _arrayReceivers[local] = array; }
        }
        foreach (var captureKey in captureOrder)
        {
            var pair = new KeyValuePair<CaptureId, ImmutableArray<IOperation>.Builder>(captureKey, captures[captureKey]);
            if (ResolveDefinitions(pair.Value, 0) is { } array)
            { _arrayCaptures[pair.Key] = array; }
        }

        void Exclude(IOperation operation)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (Symbol(operation) is { } symbol)
            { excluded.Add(symbol); }
        }

        IArrayTypeSymbol? ResolveDefinitions(ImmutableArray<IOperation>.Builder values, int depth)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            IArrayTypeSymbol? result = null;
            foreach (var value in values)
            {
                var array = Resolve(value, depth + 1);
                if (array == null || result != null && !SymbolEqualityComparer.Default.Equals(result, array))
                { return null; }
                result = array;
            }
            return result;
        }

        IArrayTypeSymbol? Resolve(IOperation operation, int depth)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (depth > 64 || --remaining < 0)
            { return null; }
            if (operation is IConversionOperation { OperatorMethod: null } conversion &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && IsArrayInterface(conversion.Type)))
            {
                var array = Resolve(conversion.Operand, depth + 1);
                return conversion.Conversion.IsIdentity || MatchesInterface(conversion.Type, array) ? array : null;
            }
            if (Symbol(operation) is { } symbol)
            {
                if (excluded.Contains(symbol))
                { return null; }
                // A later assignment does not describe the parameter's entry value.
                if (symbol is IParameterSymbol)
                { return ConcreteArray(operation); }
                if (definitions.TryGetValue(symbol, out var values))
                { return ResolveDefinitions(values, depth + 1); }
            }
            if (operation is IFlowCaptureReferenceOperation capture && captures.TryGetValue(capture.Id, out var captured))
            { return ResolveDefinitions(captured, depth + 1); }
            return ConcreteArray(operation);
        }
    }

    private readonly ImmutableHashSet<ISymbol>.Builder _defaultComparers = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
    private readonly ImmutableHashSet<CaptureId>.Builder _defaultComparerCaptures = ImmutableHashSet.CreateBuilder<CaptureId>();

    // Default comparer identity is immutable. All definitions must prove it;
    // unknown implementations, escaping storage and cycles fail closed.
    internal void TrackDefaultComparers(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue is { } branchValue ? ImmutableArray.Create(branchValue) : ImmutableArray<IOperation>.Empty)))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).ToImmutableArray();
        var definitions = ImmutableDictionary.CreateBuilder<ISymbol, ImmutableArray<IOperation>.Builder>(SymbolEqualityComparer.Default);
        var definitionOrder = ImmutableArray.CreateBuilder<ISymbol>();
        var captures = ImmutableDictionary.CreateBuilder<CaptureId, ImmutableArray<IOperation>.Builder>();
        var captureOrder = ImmutableArray.CreateBuilder<CaptureId>();
        var excluded = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var operation in graph.OriginalOperation.DescendantsAndSelf())
        {
            // Source writes also guard assignments whose CFG target is captured.
            // Deconstruction can hide several storage targets: abstain for them.
            if (operation is IDeconstructionAssignmentOperation deconstruction)
            {
                foreach (var reference in deconstruction.Target.DescendantsAndSelf())
                { Exclude(reference); }
            }
            else if (operation is IAssignmentOperation assignment && Symbol(assignment.Target) is { } target)
            {
                if (!definitions.TryGetValue(target, out var values))
                { definitions.Add(target, values = ImmutableArray.CreateBuilder<IOperation>()); definitionOrder.Add(target); }
                values.Add(assignment.Value);
            }
            if (operation is IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None, Initializer: { } initializer })
            { Exclude(initializer.Value); }
            if (operation is IArgumentOperation { Parameter.RefKind: not RefKind.None } argument)
            { Exclude(argument.Value); }
            if (operation is IAnonymousFunctionOperation ||
                operation is ILocalFunctionOperation localFunction && !SymbolEqualityComparer.Default.Equals(localFunction.Symbol, Target))
            {
                foreach (var reference in operation.Descendants())
                { Exclude(reference); }
            }
        }
        foreach (var operation in operations)
        {
            if (operation is ISimpleAssignmentOperation assignment && Symbol(assignment.Target) is { } target)
            {
                if (!definitions.TryGetValue(target, out var values))
                { definitions.Add(target, values = ImmutableArray.CreateBuilder<IOperation>()); definitionOrder.Add(target); }
                values.Add(assignment.Value);
            }
            if (operation is IFlowCaptureOperation capture)
            {
                if (!captures.TryGetValue(capture.Id, out var values))
                { captures.Add(capture.Id, values = ImmutableArray.CreateBuilder<IOperation>()); captureOrder.Add(capture.Id); }
                values.Add(capture.Value);
            }
        }
        var remaining = 4096;
        foreach (var definitionKey in definitionOrder)
        {
            var pair = new KeyValuePair<ISymbol, ImmutableArray<IOperation>.Builder>(definitionKey, definitions[definitionKey]);
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local &&
                !excluded.Contains(local) && ResolveDefinitions(pair.Value, 0))
            { _defaultComparers.Add(local); }
        }
        foreach (var captureKey in captureOrder)
        {
            var pair = new KeyValuePair<CaptureId, ImmutableArray<IOperation>.Builder>(captureKey, captures[captureKey]);
            if (ResolveDefinitions(pair.Value, 0))
            { _defaultComparerCaptures.Add(pair.Key); }
        }

        // A known null/default entry and every write must preserve the same
        // domain. Mutable parameters require this invariant, not a claim that
        // their entry value is their value at every use.
        var resolvingNullable = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in _entryNullableDefaultComparerOrder)
        {
            if (ResolveNullableSymbol(parameter, 0))
            { _nullableDefaultComparers.Add(parameter); }
        }
        foreach (var definitionKey in definitionOrder)
        {
            var pair = new KeyValuePair<ISymbol, ImmutableArray<IOperation>.Builder>(definitionKey, definitions[definitionKey]);
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local && IsIntegerComparerType(local.Type) &&
                ResolveNullableSymbol(local, 0))
            { _nullableDefaultComparers.Add(local); }
        }
        foreach (var captureKey in captureOrder)
        {
            var pair = new KeyValuePair<CaptureId, ImmutableArray<IOperation>.Builder>(captureKey, captures[captureKey]);
            if (ResolveNullableDefinitions(pair.Value, 0))
            { _nullableDefaultComparerCaptures.Add(pair.Key); }
        }

        bool ResolveNullableSymbol(ISymbol symbol, int depth)
        {
            symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
            if (depth > 64 || --remaining < 0 || excluded.Contains(symbol))
            { return false; }
            var entryKnown = _entryNullableDefaultComparers.Contains(symbol);
            if (symbol is IParameterSymbol && !entryKnown)
            { return false; }
            // Only a parameter with an independently proven entry domain can
            // close a self-reference in the invariant's write definitions.
            if (!resolvingNullable.Add(symbol))
            { return entryKnown; }
            var result = definitions.TryGetValue(symbol, out var writes)
                ? ResolveNullableDefinitions(writes, depth + 1) : entryKnown;
            resolvingNullable.Remove(symbol);
            return result;
        }

        bool ResolveNullableDefinitions(ImmutableArray<IOperation>.Builder values, int depth)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            return values.Count != 0 && values.All(value => value is null ? throw new ArgumentNullException(nameof(value)) : ResolveNullable(value, depth + 1));
        }

        bool ResolveNullable(IOperation operation, int depth)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (depth > 64 || --remaining < 0)
            { return false; }
            if (operation.ConstantValue is { HasValue: true, Value: null })
            { return true; }
            if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion &&
                (conversion.Conversion.IsIdentity || conversion.Conversion is { IsReference: true, IsImplicit: true }))
            { return ResolveNullable(conversion.Operand, depth + 1); }
            if (operation is ICoalesceOperation coalesce)
            { return ResolveNullable(coalesce.Value, depth + 1) && ResolveNullable(coalesce.WhenNull, depth + 1); }
            if (operation is IConditionalOperation { WhenFalse: { } otherwise } conditional)
            { return ResolveNullable(conditional.WhenTrue, depth + 1) && ResolveNullable(otherwise, depth + 1); }
            if (Symbol(operation) is { } symbol)
            { return ResolveNullableSymbol(symbol, depth + 1); }
            if (operation is IFlowCaptureReferenceOperation capture && captures.TryGetValue(capture.Id, out var values))
            { return ResolveNullableDefinitions(values, depth + 1); }
            return IsDefaultIntegerComparer(operation);
        }

        void Exclude(IOperation operation)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (Symbol(operation) is { } symbol)
            { excluded.Add(symbol); }
        }

        bool ResolveDefinitions(ImmutableArray<IOperation>.Builder values, int depth)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            var result = values.Count != 0;
            foreach (var value in values)
            {
                var resolved = Resolve(value, depth + 1);
                if (!resolved)
                { return false; }
                result = resolved;
            }
            return result;
        }

        bool Resolve(IOperation operation, int depth)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (depth > 64 || --remaining < 0)
            { return false; }
            if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion.IsIdentity: true } conversion)
            { return Resolve(conversion.Operand, depth + 1); }
            if (Symbol(operation) is { } symbol)
            {
                if (excluded.Contains(symbol))
                { return false; }
                if (symbol is IParameterSymbol)
                { return IsDefaultIntegerComparer(operation); }
                if (definitions.TryGetValue(symbol, out var values))
                { return ResolveDefinitions(values, depth + 1); }
            }
            if (operation is IFlowCaptureReferenceOperation capture && captures.TryGetValue(capture.Id, out var captured))
            { return ResolveDefinitions(captured, depth + 1); }
            return IsDefaultIntegerComparer(operation);
        }
    }

    internal bool IsDefaultIntegerComparer(IOperation operation, int depth = 0)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (depth > 64)
        { return false; }
        if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Conversion.IsIdentity: true } conversion)
        { return IsDefaultIntegerComparer(conversion.Operand, depth + 1); }
        return CSharpOperationSemantics.DefaultComparerElement(operation) is { } element &&
            CSharpOperationSemantics.IsComparisonIntegerType(SpecializeReceiverElement(element)) ||
            Symbol(operation) is { } symbol && _defaultComparers.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultComparerCaptures.Contains(capture.Id);
    }

    internal bool IsConstrainedIntegerComparable(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        if (invocation.Instance?.Type is not ITypeParameterSymbol receiver ||
            !CSharpOperationSemantics.IsGenericComparableDeclaration(invocation.TargetMethod) ||
            CSharpOperationSemantics.HasRefComparableReceiver(invocation))
        { return false; }
        var element = KnownScalarType(receiver);
        return CSharpOperationSemantics.IsComparisonIntegerType(element) &&
            SymbolEqualityComparer.Default.Equals(element, KnownScalarType(invocation.TargetMethod.ContainingType.TypeArguments[0]));
    }

    internal bool IsIntegerComparerType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Comparer", TypeArguments.Length: 1 } named &&
            named.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Comparer`1" &&
            CSharpOperationSemantics.IsComparisonIntegerType(SpecializeReceiverElement(named.TypeArguments[0])) &&
            named.GetMembers("Compare").OfType<IMethodSymbol>().Any(CSharpOperationSemantics.IsIntegerComparerDeclaration);
    }

    internal bool IsNullableDefaultIntegerComparer(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation.ConstantValue is { HasValue: true, Value: null } || IsDefaultIntegerComparer(operation) ||
            Symbol(operation) is { } symbol && _nullableDefaultComparers.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _nullableDefaultComparerCaptures.Contains(capture.Id);
    }

    internal void SpecializeNullableDefaultComparerParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        if (_entryNullableDefaultComparers.Add(parameter))
        {
            _entryNullableDefaultComparerOrder.Add(parameter);
        }
    }

    internal bool IsDefaultIntegerComparison(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        return CSharpOperationSemantics.IsIntegerComparerDeclaration(invocation.TargetMethod) &&
            invocation.Instance != null && IsIntegerComparerType(invocation.Instance.Type) && IsNullableDefaultIntegerComparer(invocation.Instance);
    }

    internal void SpecializeDefaultComparerParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        _defaultComparers.Add(parameter);
    }
    internal IArrayTypeSymbol? ConcreteArray(IOperation operation, int depth = 0)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (depth > 64)
        { return null; }
        if (operation.Type is IArrayTypeSymbol { IsSZArray: true } array && CSharpOperationSemantics.IsScalar(array.ElementType))
        { return array; }
        if (operation is IConversionOperation { OperatorMethod: null } conversion &&
            (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && IsArrayInterface(conversion.Type)))
        {
            var converted = ConcreteArray(conversion.Operand, depth + 1);
            return conversion.Conversion.IsIdentity || MatchesInterface(conversion.Type, converted) ? converted : null;
        }
        if (Symbol(operation) is { } symbol && _arrayReceivers.TryGetValue(symbol, out var known))
        { return known; }
        return operation is IFlowCaptureReferenceOperation capture && _arrayCaptures.TryGetValue(capture.Id, out var captured)
            ? captured : null;
    }

    private bool MatchesInterface(ITypeSymbol? type, IArrayTypeSymbol? array)
    {
        return type is INamedTypeSymbol named && array != null &&
            Type(named.TypeArguments[0]) == Type(array.ElementType);
    }

    internal ITypeSymbol? SpecializeSourceType(ITypeSymbol? type)
    {
        var remaining = 4096;
        return Substitute(type, 0);

        ITypeSymbol? Substitute(ITypeSymbol? value, int depth)
        {
            if (value == null)
            { return null; }
            if (depth > 64 || --remaining < 0)
            { throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType); }
            var direct = SpecializeReceiverElementCore(value);
            if (!SymbolEqualityComparer.Default.Equals(value, direct))
            { return Substitute(direct, depth + 1); }
            if (value is IArrayTypeSymbol array && Substitute(array.ElementType, depth + 1) is { } element &&
                !SymbolEqualityComparer.Default.Equals(element, array.ElementType))
            { return array.IsSZArray || array.Rank != 1 ? (Compilation ?? _sourceTypeCompilation)?.CreateArrayTypeSymbol(element, array.Rank, array.NullableAnnotation) ?? throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType) : throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType); }
            if (value is INamedTypeSymbol named)
            {
                var definition = named.ConstructedFrom;
                if (named.ContainingType is { } owner && Substitute(owner, depth + 1) is INamedTypeSymbol actualOwner &&
                    !SymbolEqualityComparer.Default.Equals(owner, actualOwner))
                {
                    definition = actualOwner.GetTypeMembers(named.Name, named.Arity).Single(candidate =>
                        candidate is null ? throw new ArgumentNullException(nameof(candidate)) : SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, named.OriginalDefinition));
                }
                var arguments = named.TypeArguments.Select(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : Substitute(argument, depth + 1)!).ToImmutableArray();
                if (!SymbolEqualityComparer.Default.Equals(definition, named.ConstructedFrom) ||
                    arguments.Where((argument, index) => argument is null ? throw new ArgumentNullException(nameof(argument)) : !SymbolEqualityComparer.Default.Equals(argument, named.TypeArguments[index])).Any())
                {
                    var annotations = arguments.Select(static argument => argument is null
                        ? throw new ArgumentNullException(nameof(argument))
                        : argument.NullableAnnotation).ToImmutableArray();
                    return arguments.Length == 0 ? definition : definition.Construct(arguments, annotations);
                }
            }
            return value;
        }
    }

    private ITypeSymbol? SpecializeReceiverElement(ITypeSymbol? type)
    { return SpecializeSourceType(type); }

    private ITypeSymbol? SpecializeReceiverElementCore(ITypeSymbol? type)
    {
        if (type == null)
        { return null; }
        if (_receiverElements.TryGetValue(type, out var concrete))
        { return concrete; }
        // Roslyn clones local-function type parameters for its CFG. Match only
        // the same source declaration and ordinal in the same assembly; a name
        // or ordinal alone cannot establish a generic argument's representation.
        if (type is not ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter ||
            parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.LocalFunction, DeclaringSyntaxReferences.Length: 1 } owner)
        { return type; }
        foreach (var receiverType in _receiverElementOrder)
        {
            var pair = new KeyValuePair<ITypeSymbol, ITypeSymbol>(receiverType, _receiverElements[receiverType]);
            if (pair.Key is ITypeParameterSymbol declared && declared.Ordinal == parameter.Ordinal &&
                declared.TypeParameterKind == parameter.TypeParameterKind &&
                declared.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction, DeclaringSyntaxReferences.Length: 1 } original &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingAssembly, declared.ContainingAssembly) &&
                ReferenceEquals(owner.DeclaringSyntaxReferences[0].SyntaxTree, original.DeclaringSyntaxReferences[0].SyntaxTree) &&
                owner.DeclaringSyntaxReferences[0].Span == original.DeclaringSyntaxReferences[0].Span)
            { return pair.Value; }
        }
        return type;
    }
    internal ITypeSymbol? KnownScalarType(ITypeSymbol? type)
    {
        if (type == null)
        { return null; }
        var known = SpecializeReceiverElement(type);
        return CSharpOperationSemantics.IsScalar(known) ? known : null;
    }

    internal ITypeSymbol? KnownValueType(ITypeSymbol? type)
    {
        if (type == null)
        { return null; }
        var known = SpecializeReceiverElement(type);
        return CSharpOperationSemantics.IsValueDomain(known) ? known : null;
    }

    internal void SpecializeTypeParameter(ITypeParameterSymbol parameter, ITypeSymbol scalar)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        scalar = RepositoryParameterGuard.NotNull(scalar, nameof(scalar));
        if (!_receiverElements.ContainsKey(parameter))
        { _receiverElementOrder.Add(parameter); }
        _receiverElements[parameter] = scalar;
        _types.TypeSpecializer = SpecializeReceiverElement;
        if (Target.ReturnType is ITypeParameterSymbol && Result is { } result &&
            Factory.GetVariableInfo(result).Type != Type(Target.ReturnType))
        { Result = Factory.CreateVariable("result", Type(Target.ReturnType)); }
    }

    internal void SpecializeSourceReferenceType(ITypeSymbol declared, ITypeSymbol actual)
    {
        declared = RepositoryParameterGuard.NotNull(declared, nameof(declared));
        actual = RepositoryParameterGuard.NotNull(actual, nameof(actual));
        _receiverElements[declared] = actual;
        _types.TypeSpecializer = SpecializeReceiverElement;
        var type = Type(actual);
        Parameters = [.. Parameters.Select(binding => binding is null ? throw new ArgumentNullException(nameof(binding)) :
            SymbolEqualityComparer.Default.Equals(binding.Parameter.Type, declared) && Factory.GetVariableInfo(binding.Entry).Type != type
                ? new TotalParameterBinding(binding.Parameter, Factory.CreateVariable("entry:" + binding.Parameter.Ordinal, type),
                    Factory.CreateVariable("current:" + binding.Parameter.Ordinal, type), Factory.CreateVariable("old:" + binding.Parameter.Ordinal, type)) : binding)];
        if (SymbolEqualityComparer.Default.Equals(Target.ReturnType, declared) && Result is { } result && Factory.GetVariableInfo(result).Type != type)
        { Result = Factory.CreateVariable("result", type); }
    }

    internal void RefreshSourceBindingTypes()
    {
        Parameters = [.. Parameters.Select(binding =>
        {
            binding = RepositoryParameterGuard.NotNull(binding, nameof(binding));
            // Concrete array provenance already refines an interface parameter.
            // Preserve it when refreshing compound types dependent on an owner.
            var type = Type(_arrayReceivers.TryGetValue(binding.Parameter, out var array) ? array : binding.Parameter.Type);
            return Factory.GetVariableInfo(binding.Entry).Type == type ? binding :
                new TotalParameterBinding(binding.Parameter, Factory.CreateVariable("entry:" + binding.Parameter.Ordinal, type),
                    Factory.CreateVariable("current:" + binding.Parameter.Ordinal, type), Factory.CreateVariable("old:" + binding.Parameter.Ordinal, type));
        })];
        if (Result is { } result && Factory.GetVariableInfo(result).Type != Type(Target.ReturnType))
        { Result = Factory.CreateVariable("result", Type(Target.ReturnType)); }
    }

    internal void SpecializeScalarParameter(IParameterSymbol parameter, ITypeSymbol scalar)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        scalar = RepositoryParameterGuard.NotNull(scalar, nameof(scalar));
        SpecializeTypeParameter((ITypeParameterSymbol)parameter.Type, scalar);
        Parameters = [.. Parameters.Select(binding => binding is null ? throw new ArgumentNullException(nameof(binding)) :
            SymbolEqualityComparer.Default.Equals(binding.Parameter, parameter)
                ? new TotalParameterBinding(parameter, Factory.CreateVariable("entry:" + parameter.Ordinal, Type(scalar)),
                    Factory.CreateVariable("current:" + parameter.Ordinal, Type(scalar)), Factory.CreateVariable("old:" + parameter.Ordinal, Type(scalar))) : binding)];
    }
    internal void SpecializeArrayParameter(IParameterSymbol parameter, IArrayTypeSymbol array)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        array = RepositoryParameterGuard.NotNull(array, nameof(array));
        _arrayReceivers[parameter] = array;
        var declaredElement = parameter.Type is IArrayTypeSymbol declaredArray ? declaredArray.ElementType :
            ((INamedTypeSymbol)parameter.Type).TypeArguments[0];
        if (declaredElement is ITypeParameterSymbol element)
        {
            SpecializeTypeParameter(element, array.ElementType);

        }
        Parameters = [.. Parameters.Select(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (SymbolEqualityComparer.Default.Equals(binding.Parameter, parameter)
            ? new TotalParameterBinding(parameter, Factory.CreateVariable("entry:" + parameter.Ordinal, Type(array)),
                Factory.CreateVariable("current:" + parameter.Ordinal, Type(array)),
                Factory.CreateVariable("old:" + parameter.Ordinal, Type(array))) : binding))];
    }

    internal IOperation? ArrayCollectionReceiver(IPropertyReferenceOperation property)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (CSharpOperationSemantics.ArrayCollectionReceiver(property) is { } direct)
        { return direct; }
        return CSharpOperationSemantics.IsArrayCollectionDeclaration(property.Property) && property.Instance is { } receiver &&
            ConcreteArray(receiver) is { } array &&
            Type(array.ElementType) == Type(property.Property.ContainingType.TypeArguments[0])
            ? receiver : null;
    }

    internal IrTerm BridgeValue(IrTerm value, IrTypeId type)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        // Transport admits sequence-to-object and reference-to-reference
        // identities. Keep both steps when an opaque interface stores an array.
        if (Factory.GetTypeInfo(value.Type).Kind == IrTypeKind.Sequence &&
            Factory.GetTypeInfo(type).Kind == IrTypeKind.Reference && type != Factory.ObjectType)
        { value = Factory.Cast(Factory.ObjectType, value); }
        return value.Type == type ? value : Factory.Cast(type, value);
    }

    internal static bool IsArrayInterface(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "IList" or "ICollection", Arity: 1 } named && named.OriginalDefinition.GetDocumentationCommentId() is
            "T:System.Collections.Generic.IList`1" or "T:System.Collections.Generic.ICollection`1" &&
            named.GetMembers().OfType<IPropertySymbol>().Any(CSharpOperationSemantics.IsArrayCollectionDeclaration);
    }

    private static ISymbol? Symbol(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };
    }
}
