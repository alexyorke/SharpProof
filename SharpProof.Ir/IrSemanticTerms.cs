namespace SharpProof.Ir;

/// <summary>
/// Canonical Boolean constructions used by symbolic execution and relational
/// summaries. Keeping these operations in the IR layer prevents each consumer
/// from inventing subtly different normal-completion semantics.
/// </summary>
public static class IrSemanticTerms
{
    public static bool RequiresDefinednessWitness(IrTerm? term)
    {
        return term is not (
            null or
            IrBooleanTerm or
            IrIntegerTerm or
            IrStringTerm or
            IrNullTerm or
            IrVariableTerm);
    }

    public static IrTerm ConstrainSuccessfulEvaluation(
        IrFactory factory,
        IrTerm predicate,
        IrTerm? evaluated)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(predicate, nameof(predicate));
        factory.EnsureTerm(predicate, nameof(predicate));
        if (predicate.Type != factory.BooleanType)
        {
            throw new ArgumentException(
                "The successful-evaluation predicate must be Boolean.",
                nameof(predicate));
        }

        if (evaluated != null)
        {
            factory.EnsureTerm(evaluated, nameof(evaluated));
        }

        if (!RequiresDefinednessWitness(evaluated))
        {
            return predicate;
        }

        var successfulEvaluation = factory.Binary(
            IrBinaryOperator.Equal,
            evaluated!,
            evaluated!);
        return factory.Binary(
            IrBinaryOperator.AndAlso,
            predicate,
            successfulEvaluation);
    }

    public static IrTerm Guard(
        IrFactory factory,
        IrTerm condition,
        IrTerm consequence)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        return factory.Binary(
            IrBinaryOperator.OrElse,
            factory.Unary(IrUnaryOperator.Not, condition),
            consequence);
    }

    public static IrTerm Conjoin(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(terms, nameof(terms));
        return Combine(factory, terms, IrBinaryOperator.AndAlso, identity: true);
    }

    public static IrTerm Disjoin(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(terms, nameof(terms));
        return Combine(factory, terms, IrBinaryOperator.OrElse, identity: false);
    }

    private static IrTerm Combine(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms,
        IrBinaryOperator @operator,
        bool identity)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(terms, nameof(terms));
        if (terms.Count == 0)
        {
            return factory.Boolean(identity);
        }

        for (var index = 0; index < terms.Count; index++)
        {
            var term = ArgumentNullGuard.NotNull(terms[index], nameof(terms));
            if (term.Type != factory.BooleanType)
            {
                throw new ArgumentException(
                    "Semantic conjunction and disjunction terms must be Boolean.",
                    nameof(terms));
            }
        }

        return Visit(0, terms.Count);

        IrTerm Visit(int start, int count)
        {
            if (count == 1)
            {
                return ArgumentNullGuard.NotNull(terms[start], nameof(terms));
            }

            var leftCount = count / 2;
            return factory.Binary(
                @operator,
                Visit(start, leftCount),
                Visit(start + leftCount, count - leftCount));
        }
    }
}

public static class IrTermAnalysis
{
    public static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        return IrTraversal.CollectVariables(root);
    }

    /// <summary>
    /// Measures term depth without recursion so checking an over-deep term
    /// cannot itself overflow the process stack.
    /// </summary>
    public static int GetDepth(IrTerm root)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        var memo = new Dictionary<IrId, int>();
        return GetDepth(root, memo);
    }

    internal static int GetDepth(
        IrTerm root,
        Dictionary<IrId, int> memo)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(memo, nameof(memo));
        var pending = new Stack<(IrTerm Term, bool ChildrenReady)>();
        pending.Push((root, false));
        while (pending.Count != 0)
        {
            var (term, childrenReady) = pending.Pop();
            if (memo.ContainsKey(term.Id))
            {
                continue;
            }

            var children = IrTraversal.GetChildren(term);
            if (!childrenReady && children.Length != 0)
            {
                pending.Push((term, true));
                foreach (var child in children)
                {
                    if (!memo.ContainsKey(child.Id))
                    {
                        pending.Push((child, false));
                    }
                }

                continue;
            }

            var depth = 1;
            foreach (var child in children)
            {
                depth = Math.Max(depth, 1 + memo[child.Id]);
            }

            memo.Add(term.Id, depth);
        }

        return memo[root.Id];
    }
}
