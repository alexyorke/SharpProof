namespace SharpProof.Frontend;

public sealed partial class TotalLoweringContext
{
    private void TrackFreshCollections(ControlFlowGraph graph, bool lists, bool dictionaryOrigins = false)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var identityOnly = lists || dictionaryOrigins;
        var tracked = lists ? _exactLists : dictionaryOrigins ? _defaultDictionaryOrigins : _defaultDictionaries;
        var trackedCaptures = lists ? _exactListCaptures : dictionaryOrigins ? _defaultDictionaryOriginCaptures : _defaultDictionaryCaptures;
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : block.Operations.Concat(block.BranchValue == null ? ImmutableArray<IOperation>.Empty : ImmutableArray.Create(block.BranchValue)))
            .SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : operation.DescendantsAndSelf()).ToImmutableArray();
        var definitions = ImmutableDictionary.CreateBuilder<ISymbol, ImmutableArray<IOperation>.Builder>(SymbolEqualityComparer.Default);
        var captures = ImmutableDictionary.CreateBuilder<CaptureId, ImmutableArray<IOperation>.Builder>();
        var definitionOrder = ImmutableArray.CreateBuilder<ISymbol>();
        var captureOrder = ImmutableArray.CreateBuilder<CaptureId>();
        var excluded = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        var excludedCaptures = ImmutableHashSet.CreateBuilder<CaptureId>();
        var escapedSymbols = ImmutableQueue<ISymbol>.Empty;
        var escapedCaptures = ImmutableQueue<CaptureId>.Empty;
        foreach (var operation in graph.OriginalOperation.DescendantsAndSelf().Concat(operations))
        {
            if (operation is ISimpleAssignmentOperation assignment && Symbol(assignment.Target) is { } target)
            {
                if (!definitions.TryGetValue(target, out var values))
                {
                    values = ImmutableArray.CreateBuilder<IOperation>();
                    definitions.AddUnique(target, values);
                    definitionOrder.Add(target);
                }
                values.Add(assignment.Value);
            }
            if (operation is IFlowCaptureOperation capture)
            {
                if (!captures.TryGetValue(capture.Id, out var values))
                {
                    values = ImmutableArray.CreateBuilder<IOperation>();
                    captures.AddUnique(capture.Id, values);
                    captureOrder.Add(capture.Id);
                }
                values.Add(capture.Value);
            }
            // Ownership rejects publication of comparer/deserialization state.
            // Allocation lineage survives by-value publication;
            // opaque heap effects separately invalidate its content knowledge.
            if (operation is IArgumentOperation argument && !CSharpOperationSemantics.IsScalar(argument.Value.Type) &&
                (!identityOnly || argument.Parameter?.RefKind != RefKind.None))
            { Exclude(argument.Value); }
            if (operation is ISimpleAssignmentOperation { IsRef: true } referenceAssignment)
            { Exclude(referenceAssignment.Value); }
            if (operation is IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None, Initializer: { } initializer })
            { Exclude(initializer.Value); }
            if (!identityOnly && operation is ISimpleAssignmentOperation publication && Symbol(publication.Target) == null &&
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
        while (!escapedSymbols.IsEmpty || !escapedCaptures.IsEmpty)
        {
            if (!escapedSymbols.IsEmpty)
            {
                escapedSymbols = escapedSymbols.Dequeue(out var symbol);
                if (definitions.TryGetValue(symbol, out var values))
                {
                    foreach (var value in values)
                    { Exclude(value); }
                }
            }
            else
            {
                escapedCaptures = escapedCaptures.Dequeue(out var capture);
                if (captures.TryGetValue(capture, out var values))
                {
                    foreach (var value in values)
                    { Exclude(value); }
                }
            }
        }
        var remaining = 4096;
        foreach (var key in definitionOrder)
        {
            var pair = new KeyValuePair<ISymbol, ImmutableArray<IOperation>.Builder>(key, definitions[key]);
            if (pair.Key is ILocalSymbol { RefKind: RefKind.None } && !excluded.Contains(pair.Key) && All(pair.Value, 0))
            {
                tracked.Add(pair.Key);
            }
        }
        foreach (var key in captureOrder)
        {
            var pair = new KeyValuePair<CaptureId, ImmutableArray<IOperation>.Builder>(key, captures[key]);
            if (!excludedCaptures.Contains(pair.Key) && All(pair.Value, 0))
            {
                trackedCaptures.Add(pair.Key);
            }
        }

        void Exclude(IOperation operation, bool capturesReceiver = false)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            var pending = ImmutableStack<IOperation>.Empty.Push(operation);
            while (!pending.IsEmpty)
            {
                pending = pending.Pop(out var child);
                // A framework indexer publishes its stored value, not its owner.
                // A closure still captures the receiver used to perform that read.
                if (!capturesReceiver && child is IPropertyReferenceOperation
                    { Property: { IsIndexer: true, GetMethod: { } getter } } && (lists ? CSharpOperationSemantics.IsListStateMember(getter) : CSharpOperationSemantics.IsDefaultDictionaryStateMember(getter)))
                { continue; }
                if (Symbol(child) is { } symbol && excluded.Add(symbol))
                { escapedSymbols = escapedSymbols.Enqueue(symbol); }
                if (child is IFlowCaptureReferenceOperation capture && excludedCaptures.Add(capture.Id))
                { escapedCaptures = escapedCaptures.Enqueue(capture.Id); }
                foreach (var nested in child.ChildOperations)
                { pending = pending.Push(nested); }
            }
        }
        bool All(ImmutableArray<IOperation>.Builder values, int depth)
        {
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            return values.Count != 0 && values.All(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : Resolve(value, depth + 1));
        }
        bool Resolve(IOperation operation, int depth)
        {
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            if (depth > 64 || --remaining < 0)
            { return false; }
            if (operation is IConversionOperation { OperatorMethod: null } conversion &&
                (conversion.Conversion.IsIdentity || identityOnly && conversion.Conversion.IsReference && conversion.Conversion.IsImplicit))
            { return Resolve(conversion.Operand, depth + 1); }
            if (identityOnly && operation is ISimpleAssignmentOperation { IsRef: false } assignment)
            { return Resolve(assignment.Value, depth + 1); }
            if (Symbol(operation) is { } symbol)
            {
                return !excluded.Contains(symbol) && (identityOnly && symbol is IParameterSymbol && tracked.Contains(symbol) ||
                    symbol is ILocalSymbol && definitions.TryGetValue(symbol, out var values) && All(values, depth + 1));
            }
            if (operation is IFlowCaptureReferenceOperation capture && !excludedCaptures.Contains(capture.Id) && captures.TryGetValue(capture.Id, out var captured))
            { return All(captured, depth + 1); }
            return lists ? CSharpOperationSemantics.IsEmptyListCreation(operation) : CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation);
        }
    }

}
