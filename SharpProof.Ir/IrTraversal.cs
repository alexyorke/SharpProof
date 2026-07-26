namespace SharpProof.Ir;

internal static class IrTraversal {
    internal static ImmutableArray<IrTerm> GetChildren(IrTerm term) =>
        term switch {
            IrOpaqueTerm opaque =>
                [.. opaque.Receiver == null
                    ? opaque.Arguments
                    : opaque.Arguments.Insert(0, opaque.Receiver)],
            IrUnaryTerm unary => [unary.Operand],
            IrBinaryTerm binary => [binary.Left, binary.Right],
            IrConditionalTerm conditional =>
                [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            IrCastTerm cast => [cast.Operand],
            IrLengthTerm length => [length.Value],
            IrSequenceAccessTerm access => [access.Sequence, access.Index],
            _ => []
        };

    internal static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root) =>
        CollectVariables([root]);

    internal static ImmutableHashSet<IrVarId> CollectVariables(
        IEnumerable<IrTerm> roots) {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        var pending = new Stack<IrTerm>(roots);
        var visited = new HashSet<IrId>();
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable)
                result.Add(variable.Variable);
            foreach (var child in GetChildren(term)) pending.Push(child);
        }
        return result.ToImmutable();
    }
}
