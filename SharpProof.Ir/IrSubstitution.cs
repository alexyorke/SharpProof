namespace SharpProof.Ir;

public static class IrSubstitution {
    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IrVarId variable,
        IrTerm replacement) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        return Substitute(
            factory,
            root,
            new Dictionary<IrVarId, IrTerm> { [variable] = replacement });
    }

    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));
        factory.EnsureTerm(root, nameof(root));
        foreach (var replacement in replacements) {
            var variable = factory.GetVariableInfo(replacement.Key);
            factory.EnsureTerm(replacement.Value, nameof(replacements));
            if (variable.Type != replacement.Value.Type)
                throw new ArgumentException(
                    "A replacement term must have the same type as its variable.",
                    nameof(replacements));
        }
        if (replacements.Count == 0) return root;
        var memo = new Dictionary<IrId, IrTerm>();
        return Rewrite(factory, root, replacements, memo);
    }

    private static IrTerm Rewrite(
        IrFactory factory,
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements,
        IDictionary<IrId, IrTerm> memo) {
        if (term is IrVariableTerm variable &&
            replacements.TryGetValue(variable.Variable, out var replacement))
            return replacement;
        if (memo.TryGetValue(term.Id, out var existing)) return existing;
        var rewritten = term switch {
            IrBooleanTerm or IrIntegerTerm or IrStringTerm or IrNullTerm or IrVariableTerm => term,
            IrOpaqueTerm opaque => RewriteOpaque(factory, opaque, replacements, memo),
            IrUnaryTerm unary => factory.Unary(
                unary.Operator,
                Rewrite(factory, unary.Operand, replacements, memo)),
            IrBinaryTerm binary => factory.Binary(
                binary.Operator,
                Rewrite(factory, binary.Left, replacements, memo),
                Rewrite(factory, binary.Right, replacements, memo)),
            IrConditionalTerm conditional => factory.Conditional(
                Rewrite(factory, conditional.Condition, replacements, memo),
                Rewrite(factory, conditional.WhenTrue, replacements, memo),
                Rewrite(factory, conditional.WhenFalse, replacements, memo)),
            IrCastTerm cast => factory.Cast(
                cast.Type,
                Rewrite(factory, cast.Operand, replacements, memo)),
            IrLengthTerm length => factory.Length(
                Rewrite(factory, length.Value, replacements, memo)),
            IrSequenceAccessTerm access => factory.SequenceAccess(
                Rewrite(factory, access.Sequence, replacements, memo),
                Rewrite(factory, access.Index, replacements, memo)),
            _ => throw new InvalidOperationException("Unknown IR term kind: " + term.Kind + ".")
        };
        memo.Add(term.Id, rewritten);
        return rewritten;
    }

    private static IrOpaqueTerm RewriteOpaque(
        IrFactory factory,
        IrOpaqueTerm opaque,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements,
        IDictionary<IrId, IrTerm> memo) {
        var receiver = opaque.Receiver == null
            ? null
            : Rewrite(factory, opaque.Receiver, replacements, memo);
        var arguments = opaque.Arguments
            .Select(argument => Rewrite(factory, argument, replacements, memo))
            .ToArray();
        return opaque.Purity == IrOpaquePurity.Pure
            ? factory.PureOpaque(opaque.Member, receiver, arguments)
            : factory.ImpureOpaque(opaque.Operation, opaque.Member, receiver, arguments);
    }
}
