namespace SharpProof.Frontend;

public sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _entryNullableDefaultComparers = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _nullableDefaultComparers = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _nullableDefaultComparerCaptures = [];

    private readonly Dictionary<ISymbol, IArrayTypeSymbol> _arrayReceivers = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IArrayTypeSymbol> _arrayCaptures = [];
    private readonly Dictionary<ITypeSymbol, ITypeSymbol> _receiverElements = new(SymbolEqualityComparer.Default);

    // These are representation facts, not interface contracts. Every definition
    // must have the same scalar SZ-array representation. Joins with an unknown
    // implementation, cycles, ref escapes and captured mutable storage abstain.
    internal void TrackArrayReceivers(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue == null ? [] : new[] { block.BranchValue })))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).ToArray();
        var definitions = new Dictionary<ISymbol, List<IOperation>>(SymbolEqualityComparer.Default);
        var captures = new Dictionary<CaptureId, List<IOperation>>();
        var excluded = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
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
                { definitions.Add(target, values = []); }
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
                { definitions.Add(target, values = []); }
                values.Add(assignment.Value);
            }
            if (operation is IFlowCaptureOperation capture)
            {
                if (!captures.TryGetValue(capture.Id, out var values))
                { captures.Add(capture.Id, values = []); }
                values.Add(capture.Value);
            }
        }
        var remaining = 4096;
        foreach (var pair in definitions)
        {
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local && IsArrayInterface(local.Type) &&
                !excluded.Contains(local) && ResolveDefinitions(pair.Value, 0) is { } array)
            { _arrayReceivers[local] = array; }
        }
        foreach (var pair in captures)
        {
            if (ResolveDefinitions(pair.Value, 0) is { } array)
            { _arrayCaptures[pair.Key] = array; }
        }

        void Exclude(IOperation operation)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (Symbol(operation) is { } symbol)
            { excluded.Add(symbol); }
        }

        IArrayTypeSymbol? ResolveDefinitions(List<IOperation> values, int depth)
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

    private readonly HashSet<ISymbol> _defaultComparers = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultComparerCaptures = [];

    // Default comparer identity is immutable. All definitions must prove it;
    // unknown implementations, escaping storage and cycles fail closed.
    internal void TrackDefaultComparers(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue == null ? [] : new[] { block.BranchValue })))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).ToArray();
        var definitions = new Dictionary<ISymbol, List<IOperation>>(SymbolEqualityComparer.Default);
        var captures = new Dictionary<CaptureId, List<IOperation>>();
        var excluded = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
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
                { definitions.Add(target, values = []); }
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
                { definitions.Add(target, values = []); }
                values.Add(assignment.Value);
            }
            if (operation is IFlowCaptureOperation capture)
            {
                if (!captures.TryGetValue(capture.Id, out var values))
                { captures.Add(capture.Id, values = []); }
                values.Add(capture.Value);
            }
        }
        var remaining = 4096;
        foreach (var pair in definitions)
        {
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local &&
                !excluded.Contains(local) && ResolveDefinitions(pair.Value, 0))
            { _defaultComparers.Add(local); }
        }
        foreach (var pair in captures)
        {
            if (ResolveDefinitions(pair.Value, 0))
            { _defaultComparerCaptures.Add(pair.Key); }
        }

        // A known null/default entry and every write must preserve the same
        // domain. Mutable parameters require this invariant, not a claim that
        // their entry value is their value at every use.
        var resolvingNullable = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in _entryNullableDefaultComparers)
        {
            if (ResolveNullableSymbol(parameter, 0))
            { _nullableDefaultComparers.Add(parameter); }
        }
        foreach (var pair in definitions)
        {
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } local && IsIntegerComparerType(local.Type) &&
                ResolveNullableSymbol(local, 0))
            { _nullableDefaultComparers.Add(local); }
        }
        foreach (var pair in captures)
        {
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

        bool ResolveNullableDefinitions(List<IOperation> values, int depth)
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

        bool ResolveDefinitions(List<IOperation> values, int depth)
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

    internal bool IsIntegerComparerType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { TypeArguments.Length: 1 } named &&
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
        _entryNullableDefaultComparers.Add(parameter);
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

    private ITypeSymbol? SpecializeReceiverElement(ITypeSymbol? type)
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
        foreach (var pair in _receiverElements)
        {
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
        return CSharpOperationSemantics.IsScalar(known) || CSharpOperationSemantics.IsReferenceDomain(known) ||
            known is ITypeParameterSymbol { IsReferenceType: true } ? known : null;
    }

    internal void SpecializeTypeParameter(ITypeParameterSymbol parameter, ITypeSymbol scalar)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        scalar = RepositoryParameterGuard.NotNull(scalar, nameof(scalar));
        _receiverElements[parameter] = scalar;
        _types.TypeSpecializer = SpecializeReceiverElement;
        if (Target.ReturnType is ITypeParameterSymbol && Result is { } result &&
            Factory.GetVariableInfo(result).Type != Type(Target.ReturnType))
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
        return type is INamedTypeSymbol named && named.OriginalDefinition.GetDocumentationCommentId() is
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
