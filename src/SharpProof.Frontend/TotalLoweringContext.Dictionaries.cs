namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsDictionaryStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) && symbol.OriginalDefinition.GetDocumentationCommentId() is
            "M:System.Collections.Generic.Dictionary`2.#ctor" or "M:System.Collections.Generic.Dictionary`2.Add(`0,`1)" or
            "M:System.Collections.Generic.Dictionary`2.ContainsKey(`0)" or "P:System.Collections.Generic.Dictionary`2.Count" or
            "P:System.Collections.Generic.Dictionary`2.Item(`0)";
    }

    internal static bool IsIntegerKeyDictionaryMember(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsFrameworkCollectionMember(method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 2 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Dictionary`2" &&
            IsComparisonIntegerType(type.TypeArguments[0]) &&
            // Empty construction, Count and key membership do not inspect values.
            // Contents still require the supported scalar/reference value domain.
            (method.MethodKind == MethodKind.Constructor || method.Name == "ContainsKey" ||
                method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsScalar(type.TypeArguments[1]) || IsReferenceDomain(type.TypeArguments[1]) ||
                type.TypeArguments[1] is ITypeParameterSymbol { IsReferenceType: true });
    }

    internal static bool IsEmptyIntegerKeyDictionaryCreation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Constructor: { Parameters.IsEmpty: true } constructor, Initializer: null } &&
            IsIntegerKeyDictionaryMember(constructor);
    }
}

public sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _defaultDictionaries = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultDictionaryCaptures = [];

    // The comparer is a constructor-established invariant. A dictionary-typed
    // incoming value, callback, ref escape or cyclic definition supplies no proof.
    internal void TrackDefaultDictionaries(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : block.Operations.Concat(block.BranchValue == null ? [] : new[] { block.BranchValue }))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf()).ToArray();
        var definitions = new Dictionary<ISymbol, List<IOperation>>(SymbolEqualityComparer.Default);
        var captures = new Dictionary<CaptureId, List<IOperation>>();
        var excluded = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var excludedCaptures = new HashSet<CaptureId>();
        var escapedSymbols = new Queue<ISymbol>();
        var escapedCaptures = new Queue<CaptureId>();
        foreach (var operation in graph.OriginalOperation.DescendantsAndSelf().Concat(operations))
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
            // Publishing the reference can expose comparer/deserialization state.
            // Until call ownership is proven, reject every escaping binding.
            if (operation is IArgumentOperation argument && !CSharpOperationSemantics.IsScalar(argument.Value.Type))
            { Exclude(argument.Value); }
            if (operation is ISimpleAssignmentOperation { IsRef: true } referenceAssignment)
            { Exclude(referenceAssignment.Value); }
            if (operation is IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None, Initializer: { } initializer })
            { Exclude(initializer.Value); }
            if (operation is ISimpleAssignmentOperation publication && Symbol(publication.Target) == null &&
                publication.Target is not IFlowCaptureReferenceOperation && !CSharpOperationSemantics.IsScalar(publication.Value.Type))
            { Exclude(publication.Value); }
            if (operation is IDeconstructionAssignmentOperation deconstruction)
            { Exclude(deconstruction.Target); }
            if (operation is IAnonymousFunctionOperation || operation is ILocalFunctionOperation local &&
                !SymbolEqualityComparer.Default.Equals(local.Symbol, Target))
            { Exclude(operation, capturesReceiver: true); }
        }
        // Escape is an identity property, not a property of one local spelling.
        // Walk every definition backward once, including CFG captures and joins.
        // The deduplicated queues also terminate on cyclic alias definitions.
        while (escapedSymbols.Count != 0 || escapedCaptures.Count != 0)
        {
            if (escapedSymbols.Count != 0)
            {
                var symbol = escapedSymbols.Dequeue();
                if (definitions.TryGetValue(symbol, out var values))
                {
                    foreach (var value in values)
                    { Exclude(value); }
                }
            }
            else
            {
                var capture = escapedCaptures.Dequeue();
                if (captures.TryGetValue(capture, out var values))
                {
                    foreach (var value in values)
                    { Exclude(value); }
                }
            }
        }
        var remaining = 4096;
        foreach (var pair in definitions)
        {
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } && !excluded.Contains(pair.Key) && All(pair.Value, 0))
            { _defaultDictionaries.Add(pair.Key); }
        }
        foreach (var pair in captures)
        {
            if (!excludedCaptures.Contains(pair.Key) && All(pair.Value, 0))
            { _defaultDictionaryCaptures.Add(pair.Key); }
        }

        void Exclude(IOperation operation, bool capturesReceiver = false)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            while (pending.Count != 0)
            {
                var child = pending.Pop();
                // A framework indexer publishes its stored value, not its owner.
                // A closure still captures the receiver used to perform that read.
                if (!capturesReceiver && child is IPropertyReferenceOperation
                    { Property: { IsIndexer: true, GetMethod: { } getter } } && CSharpOperationSemantics.IsIntegerKeyDictionaryMember(getter))
                { continue; }
                if (Symbol(child) is { } symbol && excluded.Add(symbol))
                { escapedSymbols.Enqueue(symbol); }
                if (child is IFlowCaptureReferenceOperation capture && excludedCaptures.Add(capture.Id))
                { escapedCaptures.Enqueue(capture.Id); }
                foreach (var nested in child.ChildOperations)
                { pending.Push(nested); }
            }
        }
        bool All(List<IOperation> values, int depth)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            return values.Count != 0 && values.All(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : Resolve(value, depth + 1));
        }
        bool Resolve(IOperation operation, int depth)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (depth > 64 || --remaining < 0)
            { return false; }
            if (operation is IConversionOperation { OperatorMethod: null, Conversion.IsIdentity: true } conversion)
            { return Resolve(conversion.Operand, depth + 1); }
            if (Symbol(operation) is { } symbol)
            { return symbol is ILocalSymbol && !excluded.Contains(symbol) && definitions.TryGetValue(symbol, out var values) && All(values, depth + 1); }
            if (operation is IFlowCaptureReferenceOperation capture && !excludedCaptures.Contains(capture.Id) && captures.TryGetValue(capture.Id, out var captured))
            { return All(captured, depth + 1); }
            return CSharpOperationSemantics.IsEmptyIntegerKeyDictionaryCreation(operation);
        }
    }

    internal bool IsDefaultIntegerKeyDictionary(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return CSharpOperationSemantics.IsEmptyIntegerKeyDictionaryCreation(operation) ||
            Symbol(operation) is { } symbol && _defaultDictionaries.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultDictionaryCaptures.Contains(capture.Id);
    }
}
