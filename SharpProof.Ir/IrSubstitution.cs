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

        return Substitute(
            factory,
            root,
            new Dictionary<IrVarId, IrTerm> { [variable] = replacement });
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
        foreach (var replacement in replacements)
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
        if (replacements.Count == 0)
        {
            return root;
        }

        var memo = new Dictionary<IrId, IrTerm>();
        return Rewrite(factory, root, replacements, memo);
    }

    private static IrTerm Rewrite(
        IrFactory factory,
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements,
        IDictionary<IrId, IrTerm> memo)
    {
        if (term is IrVariableTerm variable &&
            replacements.TryGetValue(variable.Variable, out var replacement))
        {
            return replacement;
        }

        if (memo.TryGetValue(term.Id, out var existing))
        {
            return existing;
        }

        IrTerm Visit(IrTerm child)
        {
            return Rewrite(factory, child, replacements, memo);
        }

        IrTerm? VisitNullable(IrTerm? child)
        {
            return child == null ? null : Visit(child);
        }

        IrTerm[] VisitAll(ImmutableArray<IrTerm> children)
        {
            return [.. children.Select(Visit)];
        }

        var rewritten = term switch
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
        memo.Add(term.Id, rewritten);
        return rewritten;
    }
}
