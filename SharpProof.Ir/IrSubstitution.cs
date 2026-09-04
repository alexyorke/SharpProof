namespace SharpProof.Ir;

public static class IrSubstitution
{
    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IrVarId variable,
        IrTerm replacement)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(replacement, nameof(replacement));
        factory.EnsureTerm(root, nameof(root));

        var replacementMap = CreateReplacementMap(
            factory,
            new Dictionary<IrVarId, IrTerm> { [variable] = replacement });
        return SubstituteValidated(factory, root, replacementMap);
    }

    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(replacements, nameof(replacements));

        factory.EnsureTerm(root, nameof(root));
        var replacementMap = CreateReplacementMap(factory, replacements);
        return SubstituteValidated(factory, root, replacementMap);
    }

    private static IrTerm SubstituteValidated(
        IrFactory factory,
        IrTerm root,
        Dictionary<IrVarId, IrTerm> replacementMap)
    {
        if (replacementMap.Count == 0)
        {
            return root;
        }

        var memo = new Dictionary<IrId, IrTerm>();
        return Rewrite(factory, root, replacementMap, memo);
    }

    public static ImmutableArray<IrTerm> SubstituteMany(
        IrFactory factory,
        IReadOnlyList<IrTerm> roots,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(roots, nameof(roots));
        ArgumentNullGuard.NotNull(replacements, nameof(replacements));

        foreach (var root in roots)
        {
            ArgumentNullGuard.NotNull(root, nameof(roots));
            factory.EnsureTerm(root, nameof(roots));
        }

        var replacementMap = CreateReplacementMap(factory, replacements);
        if (replacementMap.Count == 0)
        {
            return [.. roots];
        }

        var memo = new Dictionary<IrId, IrTerm>();
        var result = ImmutableArray.CreateBuilder<IrTerm>(roots.Count);
        foreach (var root in roots)
        {
            result.Add(Rewrite(factory, root, replacementMap, memo));
        }

        return result.MoveToImmutable();
    }

    private static Dictionary<IrVarId, IrTerm> CreateReplacementMap(
        IrFactory factory,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements)
    {
        // Materialize the caller-supplied view once. IReadOnlyDictionary is an
        // interface, not an immutable snapshot; validation and rewriting must
        // operate on the same mapping.
        var replacementMap = replacements.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value);
        foreach (var replacement in replacementMap)
        {
            var variable = factory.GetVariableInfo(replacement.Key);
            factory.EnsureTerm(replacement.Value, nameof(replacements));
            if (variable.Type != replacement.Value.Type)
            {
                throw new ArgumentException(
                    "A replacement term must have the same type as its variable.",
                    nameof(replacements));
            }
        }

        return replacementMap;
    }

    /// <summary>
    /// Rewrites the term bottom-up using an explicit stack. Terms are a
    /// hash-consed DAG whose depth is bounded only by the source expression, and
    /// StackOverflowException is uncatchable, so this must not recurse.
    /// </summary>
    private static IrTerm Rewrite(
        IrFactory factory,
        IrTerm root,
        Dictionary<IrVarId, IrTerm> replacements,
        Dictionary<IrId, IrTerm> memo)
    {
        return IrTraversal.FoldBottomUp(
            root,
            memo,
            (term, rewritten) => RewriteNode(factory, term, rewritten),
            term => term is IrVariableTerm variable &&
                replacements.TryGetValue(variable.Variable, out var replacement)
                    ? (true, replacement)
                    : (false, null!));
    }

    private static IrTerm RewriteNode(
        IrFactory factory,
        IrTerm term,
        Dictionary<IrId, IrTerm> memo)
    {
        IrTerm Visit(IrTerm child)
        {
            return memo[child.Id];
        }

        IrTerm? VisitNullable(IrTerm? child)
        {
            return child == null ? null : Visit(child);
        }

        IrTerm[] VisitAll(ImmutableArray<IrTerm> children)
        {
            return [.. children.Select(Visit)];
        }

        return term switch
        {
            IrBooleanTerm or IrIntegerTerm or IrStringTerm or IrNullTerm or IrVariableTerm => term,
            IrOpaqueTerm { Purity: IrOpaquePurity.Pure } opaque =>
                factory.PureOpaque(
                    opaque.Member,
                    VisitNullable(opaque.Receiver),
                    VisitAll(opaque.Arguments)),
            IrOpaqueTerm opaque =>
                factory.ImpureOpaque(
                    opaque.Operation,
                    opaque.Member,
                    VisitNullable(opaque.Receiver),
                    VisitAll(opaque.Arguments)),
            IrUnaryTerm unary => factory.Unary(unary.Operator, Visit(unary.Operand)),
            IrBinaryTerm binary => factory.Binary(
                binary.Operator,
                Visit(binary.Left),
                Visit(binary.Right)),
            IrConditionalTerm conditional => factory.Conditional(
                Visit(conditional.Condition),
                Visit(conditional.WhenTrue),
                Visit(conditional.WhenFalse)),
            IrCastTerm cast => factory.Cast(cast.Type, Visit(cast.Operand)),
            IrLengthTerm length => factory.Length(Visit(length.Value)),
            IrSequenceAccessTerm access => factory.SequenceAccess(
                Visit(access.Sequence),
                Visit(access.Index)),
            _ => throw new InvalidOperationException("Unknown IR term kind: " + term.Kind + ".")
        };
    }
}
