namespace SharpProof.Ir;

internal static class IrTraversal
{
    internal static ImmutableArray<IrTerm> GetChildren(IrTerm term)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));
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

    internal static ImmutableStack<IrTerm> PushChildren(
        IrTerm term,
        ImmutableStack<IrTerm> pending)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));
        ArgumentNullGuard.NotNull(pending, nameof(pending));
        foreach (var child in GetChildren(term))
        {
            pending = pending.Push(child);
        }
        return pending;
    }

    internal static bool Any(IrTerm root, Func<IrTerm, bool> predicate)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(predicate, nameof(predicate));
        return Traverse(ImmutableStack<IrTerm>.Empty.Push(root), predicate, false).Found;
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        return Traverse(ImmutableStack<IrTerm>.Empty.Push(root), null, true).Variables;
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(IEnumerable<IrTerm> roots)
    {
        roots = RepositoryParameterGuard.NotNull(roots, nameof(roots));
        var pending = ImmutableStack<IrTerm>.Empty;
        foreach (var root in roots)
        {
            pending = pending.Push(root);
        }
        return Traverse(pending, null, true).Variables;
    }

    private static (bool Found, ImmutableHashSet<IrVarId> Variables) Traverse(
        ImmutableStack<IrTerm> pending,
        Func<IrTerm, bool>? predicate,
        bool collectVariables)
    {
        pending = RepositoryParameterGuard.NotNull(pending, nameof(pending));
        var visited = ImmutableHashSet.CreateBuilder<IrId>();
        var variables = ImmutableHashSet.CreateBuilder<IrVarId>();
        while (!pending.IsEmpty)
        {
            var term = pending.Peek();
            pending = pending.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }
            if (predicate != null && predicate(term))
            {
                return (true, variables.ToImmutable());
            }
            if (collectVariables && term is IrVariableTerm variable)
            {
                variables.Add(variable.Variable);
            }
            pending = PushChildren(term, pending);
        }
        return (false, variables.ToImmutable());
    }

    internal static (T Value, ImmutableDictionary<IrId, T> Memo) FoldBottomUp<T>(
        IrTerm root,
        ImmutableDictionary<IrId, T> memo,
        Func<IrTerm, ImmutableArray<IrTerm>, ImmutableDictionary<IrId, T>, T> combine,
        Func<IrTerm, (bool HasValue, T Value)>? shortCircuit = null)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(memo, nameof(memo));
        ArgumentNullGuard.NotNull(combine, nameof(combine));
        var memoBuilder = memo.ToBuilder();
        var pending = ImmutableStack<(
            IrTerm Term,
            bool ChildrenReady,
            ImmutableArray<IrTerm> Children)>.Empty;
        pending = pending.Push((root, false, []));
        while (!pending.IsEmpty)
        {
            var (term, childrenReady, children) = pending.Peek();
            pending = pending.Pop();
            if (memoBuilder.ContainsKey(term.Id))
            {
                continue;
            }

            if (!childrenReady)
            {
                if (shortCircuit?.Invoke(term) is (true, var value))
                {
                    memoBuilder.Add(term.Id, value);
                    continue;
                }

                children = GetChildren(term);
                if (children.Length != 0)
                {
                    pending = pending.Push((term, true, children));
                    foreach (var child in children)
                    {
                        if (!memoBuilder.ContainsKey(child.Id))
                        {
                            pending = pending.Push((child, false, []));
                        }
                    }

                    continue;
                }
            }

            memoBuilder.Add(term.Id, combine(term, children, memoBuilder.ToImmutable()));
        }

        return (memoBuilder[root.Id], memoBuilder.ToImmutable());
    }
}
