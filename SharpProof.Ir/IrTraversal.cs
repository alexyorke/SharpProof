namespace SharpProof.Ir;
internal static class IrTraversal
{
    internal static ImmutableArray<IrTerm> GetChildren(IrTerm term)
    {
        return term switch
        {
            IrOpaqueTerm opaque =>
                opaque.Receiver == null
                    ? opaque.Arguments
                    : opaque.Arguments.Insert(0, opaque.Receiver),
            IrUnaryTerm unary => [unary.Operand],
            IrBinaryTerm binary => [binary.Left, binary.Right],
            IrConditionalTerm conditional =>
                [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            IrCastTerm cast => [cast.Operand],
            IrLengthTerm length => [length.Value],
            IrSequenceAccessTerm access => [access.Sequence, access.Index],
            _ => []
        };
    }

    internal static bool Any(IrTerm root, Func<IrTerm, bool> predicate)
    {
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var term = pending.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }

            if (predicate(term))
            {
                return true;
            }

            foreach (var child in GetChildren(term))
            {
                pending.Push(child);
            }
        }

        return false;
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root)
    {
        return CollectVariables([root]);
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(
        IEnumerable<IrTerm> roots)
    {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        var pending = new Stack<IrTerm>(roots);
        var visited = new HashSet<IrId>();
        while (pending.Count != 0)
        {
            var term = pending.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }

            if (term is IrVariableTerm variable)
            {
                result.Add(variable.Variable);
            }

            foreach (var child in GetChildren(term))
            {
                pending.Push(child);
            }
        }
        return result.ToImmutable();
    }

    internal static T FoldBottomUp<T>(
        IrTerm root,
        Dictionary<IrId, T> memo,
        Func<IrTerm, ImmutableArray<IrTerm>, Dictionary<IrId, T>, T> combine,
        Func<IrTerm, (bool HasValue, T Value)>? shortCircuit = null)
    {
        var pending = new Stack<(
            IrTerm Term,
            bool ChildrenReady,
            ImmutableArray<IrTerm> Children)>();
        pending.Push((root, false, []));
        while (pending.Count != 0)
        {
            var (term, childrenReady, children) = pending.Pop();
            if (memo.ContainsKey(term.Id))
            {
                continue;
            }

            if (!childrenReady)
            {
                if (shortCircuit?.Invoke(term) is (true, var value))
                {
                    memo.Add(term.Id, value);
                    continue;
                }

                children = GetChildren(term);
                if (children.Length != 0)
                {
                    pending.Push((term, true, children));
                    foreach (var child in children)
                    {
                        if (!memo.ContainsKey(child.Id))
                        {
                            pending.Push((child, false, []));
                        }
                    }

                    continue;
                }
            }

            memo.Add(term.Id, combine(term, children, memo));
        }

        return memo[root.Id];
    }
}
