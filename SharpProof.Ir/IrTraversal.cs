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

    internal static void PushChildren(
        IrTerm term,
        Stack<IrTerm> pending)
    {
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
        var pending = new Stack<IrTerm>(1);
        pending.Push(root);
        return Traverse(pending, predicate, variables: null);
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root)
    {
        var pending = new Stack<IrTerm>();
        pending.Push(root);
        return CollectVariablesCore(pending);
    }

    internal static ImmutableHashSet<IrVarId> CollectVariables(
        IEnumerable<IrTerm> roots)
    {
        return CollectVariablesCore(new Stack<IrTerm>(roots));
    }

    private static ImmutableHashSet<IrVarId> CollectVariablesCore(
        Stack<IrTerm> pending)
    {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        Traverse(pending, predicate: null, result);
        return result.ToImmutable();
    }

    private static bool Traverse(
        Stack<IrTerm> pending,
        Func<IrTerm, bool>? predicate,
        ImmutableHashSet<IrVarId>.Builder? variables)
    {
        var visited = new HashSet<IrId>();
        while (pending.Count != 0)
        {
            var term = pending.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }

            if (predicate != null && predicate(term))
            {
                return true;
            }

            if (variables != null && term is IrVariableTerm variable)
            {
                variables.Add(variable.Variable);
            }

            PushChildren(term, pending);
        }
        return false;
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
