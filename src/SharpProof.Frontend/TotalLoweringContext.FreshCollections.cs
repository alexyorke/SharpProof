namespace SharpProof.Frontend;

public sealed partial class TotalLoweringContext
{
    private void TrackFreshCollections(ControlFlowGraph graph, bool lists)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var tracked = lists ? _exactLists : _defaultDictionaries;
        var trackedCaptures = lists ? _exactListCaptures : _defaultDictionaryCaptures;
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
            // Ownership rejects publication of comparer/deserialization state.
            // A List's allocation identity survives by-value publication;
            // opaque heap effects separately invalidate its content knowledge.
            if (operation is IArgumentOperation argument && !CSharpOperationSemantics.IsScalar(argument.Value.Type) &&
                (!lists || argument.Parameter?.RefKind != RefKind.None))
            { Exclude(argument.Value); }
            if (operation is ISimpleAssignmentOperation { IsRef: true } referenceAssignment)
            { Exclude(referenceAssignment.Value); }
            if (operation is IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None, Initializer: { } initializer })
            { Exclude(initializer.Value); }
            if (!lists && operation is ISimpleAssignmentOperation publication && Symbol(publication.Target) == null &&
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
            { tracked.Add(pair.Key); }
        }
        foreach (var pair in captures)
        {
            if (!excludedCaptures.Contains(pair.Key) && All(pair.Value, 0))
            { trackedCaptures.Add(pair.Key); }
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
                    { Property: { IsIndexer: true, GetMethod: { } getter } } && (lists ? CSharpOperationSemantics.IsListStateMember(getter) : CSharpOperationSemantics.IsDefaultDictionaryStateMember(getter)))
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
            if (operation is IConversionOperation { OperatorMethod: null } conversion &&
                (conversion.Conversion.IsIdentity || lists && conversion.Conversion.IsReference && conversion.Conversion.IsImplicit))
            { return Resolve(conversion.Operand, depth + 1); }
            if (lists && operation is ISimpleAssignmentOperation { IsRef: false } assignment)
            { return Resolve(assignment.Value, depth + 1); }
            if (Symbol(operation) is { } symbol)
            {
                return !excluded.Contains(symbol) && (lists && symbol is IParameterSymbol && tracked.Contains(symbol) ||
                    symbol is ILocalSymbol && definitions.TryGetValue(symbol, out var values) && All(values, depth + 1));
            }
            if (operation is IFlowCaptureReferenceOperation capture && !excludedCaptures.Contains(capture.Id) && captures.TryGetValue(capture.Id, out var captured))
            { return All(captured, depth + 1); }
            return lists ? CSharpOperationSemantics.IsEmptyListCreation(operation) || IsListRangeAllocation(operation) : CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation);
        }
    }

}
