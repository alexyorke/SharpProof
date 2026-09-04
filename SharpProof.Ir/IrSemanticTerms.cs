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

        if (!RequiresDefinednessWitness(evaluated))
        {
            return IrFactory.RequireBooleanTerm(
                factory,
                predicate,
                nameof(predicate),
                "The term must be boolean.");
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

        return Visit(0, terms.Count);

        IrTerm Visit(int start, int count)
        {
            if (count == 1)
            {
                return IrFactory.RequireBooleanTerm(
                    factory,
                    terms[start],
                    nameof(terms),
                    "The term must be boolean.");
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
        return IrTraversal.FoldBottomUp(
            root,
            memo,
            static (term, children, depths) =>
            {
                var depth = 1;
                foreach (var child in children)
                {
                    depth = Math.Max(depth, 1 + depths[child.Id]);
                }
                return depth;
            });
    }
}
