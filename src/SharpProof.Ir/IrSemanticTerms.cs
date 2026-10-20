namespace SharpProof.Ir;

/// <summary>
/// Canonical Boolean constructions used by callable verification.
/// Each input belongs to the same factory and has Boolean type.
/// </summary>
internal static class IrSemanticTerms
{
    public static IrTerm Guard(
        IrFactory factory,
        IrTerm condition,
        IrTerm consequence)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(condition, nameof(condition));
        ArgumentNullGuard.NotNull(consequence, nameof(consequence));
        return factory.Binary(
            IrBinaryOperator.OrElse,
            factory.Unary(IrUnaryOperator.Not, condition),
            consequence);
    }

    public static IrTerm Conjoin(
        IrFactory factory,
        ImmutableArray<IrTerm> terms)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        if (terms.IsDefault)
        { throw new ArgumentNullException(nameof(terms)); }
        return Combine(factory, terms, IrBinaryOperator.AndAlso, identity: true);
    }

    public static IrTerm Disjoin(
        IrFactory factory,
        ImmutableArray<IrTerm> terms)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        if (terms.IsDefault)
        { throw new ArgumentNullException(nameof(terms)); }
        return Combine(factory, terms, IrBinaryOperator.OrElse, identity: false);
    }

    private static IrTerm Combine(
        IrFactory factory,
        ImmutableArray<IrTerm> terms,
        IrBinaryOperator @operator,
        bool identity)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        if (terms.Length == 0)
        {
            return factory.Boolean(identity);
        }

        return Visit(0, terms.Length);

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

internal static class IrTermAnalysis
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
        var memo = ImmutableDictionary<IrId, int>.Empty;
        return GetDepth(root, memo).Value;
    }

    internal static (int Value, ImmutableDictionary<IrId, int> Memo) GetDepth(
        IrTerm root,
        ImmutableDictionary<IrId, int> memo)
    {
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(memo, nameof(memo));
        return IrTraversal.FoldBottomUp(
            root,
            memo,
            static (term, children, depths) =>
            {
                RepositoryParameterGuard.RequireInitialized(children.IsDefault, nameof(children), "Children must be initialized.");

                depths = RepositoryParameterGuard.NotNull(depths, nameof(depths));
                term = RepositoryParameterGuard.NotNull(term, nameof(term));
                var depth = 1;
                foreach (var child in children)
                {
                    depth = Math.Max(depth, 1 + depths[child.Id]);
                }
                return depth;
            });
    }
}
