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

    internal static void PushChildren(
        IrTerm term,
        Stack<IrTerm> pending)
    {
        ArgumentNullGuard.NotNull(term, nameof(term));
        ArgumentNullGuard.NotNull(pending, nameof(pending));
        switch (term)
        {
            case IrOpaqueTerm opaque:
                if (opaque.Receiver is { } receiver)
                {
                    pending.Push(receiver);
                }
                for (var index = 0; index < opaque.Arguments.Length; index++)
                {
                    pending.Push(opaque.Arguments[index]);
                }
                break;
            case IrUnaryTerm unary:
                pending.Push(unary.Operand);
                break;
            case IrBinaryTerm binary:
                pending.Push(binary.Left);
                pending.Push(binary.Right);
                break;
            case IrConditionalTerm conditional:
                pending.Push(conditional.Condition);
                pending.Push(conditional.WhenTrue);
                pending.Push(conditional.WhenFalse);
                break;
            case IrCastTerm cast:
                pending.Push(cast.Operand);
                break;
            case IrLengthTerm length:
                pending.Push(length.Value);
                break;
            case IrSequenceAccessTerm access:
                pending.Push(access.Sequence);
                pending.Push(access.Index);
                break;
        }
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
        var visited = ImmutableHashSet<IrId>.Empty;
        var variables = ImmutableHashSet<IrVarId>.Empty;
        while (!pending.IsEmpty)
        {
            var term = pending.Peek();
            pending = pending.Pop();
            if (visited.Contains(term.Id))
            {
                continue;
            }
            visited = visited.Add(term.Id);
            if (predicate != null && predicate(term))
            {
                return (true, variables);
            }
            if (collectVariables && term is IrVariableTerm variable)
            {
                variables = variables.Add(variable.Variable);
            }
            pending = PushChildren(term, pending);
        }
        return (false, variables);
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
        var pending = ImmutableStack<(
            IrTerm Term,
            bool ChildrenReady,
            ImmutableArray<IrTerm> Children)>.Empty;
        pending = pending.Push((root, false, []));
        while (!pending.IsEmpty)
        {
            var (term, childrenReady, children) = pending.Peek();
            pending = pending.Pop();
            if (memo.ContainsKey(term.Id))
            {
                continue;
            }

            if (!childrenReady)
            {
                if (shortCircuit?.Invoke(term) is (true, var value))
                {
                    memo = memo.Add(term.Id, value);
                    continue;
                }

                children = GetChildren(term);
                if (children.Length != 0)
                {
                    pending = pending.Push((term, true, children));
                    foreach (var child in children)
                    {
                        if (!memo.ContainsKey(child.Id))
                        {
                            pending = pending.Push((child, false, []));
                        }
                    }

                    continue;
                }
            }

            memo = memo.Add(term.Id, combine(term, children, memo));
        }

        return (memo[root.Id], memo);
    }
}
