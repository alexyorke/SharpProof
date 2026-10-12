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
        return Rewrite(
            factory,
            root,
            replacementMap,
            memo,
            allowedVariables: null,
            out _);
    }

    // Substitutes variables and rebuilds each element or field read,
    // bottom-up, from the original read and its rewritten operands; a null
    // result rebuilds it as usual.
    internal static IrTerm SubstituteWithReads(
        IrFactory factory,
        IrTerm root,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements,
        Func<IrTerm, Func<IrTerm, IrTerm>, IrTerm?> read)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(read, nameof(read));
        factory.EnsureTerm(root, nameof(root));
        var replacementMap = CreateReplacementMap(factory, replacements);
        return Rewrite(factory, root, replacementMap, [], allowedVariables: null, out _, read);
    }

    internal static bool TrySubstitute(
        IrFactory factory,
        IrTerm root,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements,
        ISet<IrVarId>? freeVariables,
        out IrTerm result)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(replacements, nameof(replacements));

        factory.EnsureTerm(root, nameof(root));
        var replacementMap = CreateReplacementMap(factory, replacements);
        var memo = new Dictionary<IrId, IrTerm>();
        result = Rewrite(
            factory,
            root,
            replacementMap,
            memo,
            freeVariables,
            out var variablesValid);
        return variablesValid;
    }

    public static ImmutableArray<IrTerm> SubstituteMany(
        IrFactory factory,
        IReadOnlyList<IrTerm> roots,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(roots, nameof(roots));
        ArgumentNullGuard.NotNull(replacements, nameof(replacements));

        // IReadOnlyList is a caller-owned view, not an immutable snapshot.
        // Validate and process the same materialized roots so a changing view
        // cannot substitute terms that were never checked for factory ownership.
        var rootSnapshot = roots.ToArray();
        foreach (var root in rootSnapshot)
        {
            ArgumentNullGuard.NotNull(root, nameof(roots));
            factory.EnsureTerm(root, nameof(roots));
        }

        var replacementMap = CreateReplacementMap(factory, replacements);
        if (replacementMap.Count == 0)
        {
            return [.. rootSnapshot];
        }

        var memo = new Dictionary<IrId, IrTerm>();
        var result = ImmutableArray.CreateBuilder<IrTerm>(rootSnapshot.Length);
        foreach (var root in rootSnapshot)
        {
            result.Add(Rewrite(
                factory,
                root,
                replacementMap,
                memo,
                allowedVariables: null,
                out _));
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
        Dictionary<IrId, IrTerm> memo,
        ISet<IrVarId>? allowedVariables,
        out bool variablesValid,
        Func<IrTerm, Func<IrTerm, IrTerm>, IrTerm?>? read = null)
    {
        var allVariablesValid = true;
        var result = IrTraversal.FoldBottomUp(
            root,
            memo,
            (term, _, rewritten) => (read != null && term is IrSequenceAccessTerm or IrOpaqueTerm
                ? read(term, child => rewritten[child.Id]) : null) ?? RewriteNode(factory, term, rewritten),
            term =>
            {
                if (term is not IrVariableTerm variable)
                {
                    return (false, null!);
                }

                if (replacements.TryGetValue(
                        variable.Variable,
                        out var replacement))
                {
                    return (true, replacement);
                }

                if (allowedVariables == null ||
                    !allowedVariables.Contains(variable.Variable))
                {
                    allVariablesValid = false;
                }

                return (false, null!);
            });
        variablesValid = allVariablesValid;
        return result;
    }

    private static IrTerm RewriteNode(
        IrFactory factory,
        IrTerm term,
        Dictionary<IrId, IrTerm> memo)
    {
        return term.Accept(RewriteVisitor.Instance, (factory, memo));
    }

    private sealed class RewriteVisitor : IIrTermVisitor<(IrFactory Factory, Dictionary<IrId, IrTerm> Memo), IrTerm>
    {
        internal static RewriteVisitor Instance { get; } = new();

        public IrTerm Visit(IrBooleanTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrIntegerTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrStringTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrNullTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrEmptyArrayTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrVariableTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return term;
        }

        public IrTerm Visit(IrOpaqueTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteOpaque(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrUnaryTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteUnary(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrBinaryTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteBinary(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrConditionalTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteConditional(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrCastTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteCast(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrLengthTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteLength(state.Factory, term, state.Memo);
        }

        public IrTerm Visit(IrSequenceAccessTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            return RewriteSequenceAccess(state.Factory, term, state.Memo);
        }
    }

    private static IrTerm Visit(Dictionary<IrId, IrTerm> memo, IrTerm child)
    {
        return memo[child.Id];
    }

    private static IrTerm? VisitNullable(Dictionary<IrId, IrTerm> memo, IrTerm? child)
    {
        return child == null ? null : Visit(memo, child);
    }

    private static IrTerm RewriteOpaque(IrFactory termFactory, IrOpaqueTerm opaque, Dictionary<IrId, IrTerm> memo)
    {
        var receiver = VisitNullable(memo, opaque.Receiver);
        IrTerm[]? arguments = null;
        for (var index = 0; index < opaque.Arguments.Length; index++)
        {
            var argument = Visit(memo, opaque.Arguments[index]);
            if (ReferenceEquals(argument, opaque.Arguments[index]))
            {
                continue;
            }

            arguments ??= [.. opaque.Arguments];
            arguments[index] = argument;
        }

        if (ReferenceEquals(receiver, opaque.Receiver) &&
            arguments == null)
        {
            return opaque;
        }

        arguments ??= [.. opaque.Arguments];
        return opaque.Purity == IrOpaquePurity.Pure
            ? termFactory.PureOpaque(
                opaque.Member,
                receiver,
                arguments)
            : termFactory.ImpureOpaque(
                opaque.Operation,
                opaque.Member,
                receiver,
                arguments);
    }

    private static IrTerm RewriteUnary(IrFactory termFactory, IrUnaryTerm unary, Dictionary<IrId, IrTerm> memo)
    {
        var operand = Visit(memo, unary.Operand);
        return ReferenceEquals(operand, unary.Operand)
            ? unary
            : termFactory.Unary(unary.Operator, operand);
    }

    private static IrTerm RewriteBinary(IrFactory termFactory, IrBinaryTerm binary, Dictionary<IrId, IrTerm> memo)
    {
        var left = Visit(memo, binary.Left);
        var right = Visit(memo, binary.Right);
        return ReferenceEquals(left, binary.Left) &&
               ReferenceEquals(right, binary.Right)
            ? binary
            : termFactory.RewriteBinary(binary.Operator, left, right);
    }

    private static IrTerm RewriteConditional(
        IrFactory termFactory,
        IrConditionalTerm conditional, Dictionary<IrId, IrTerm> memo)
    {
        var condition = Visit(memo, conditional.Condition);
        var whenTrue = Visit(memo, conditional.WhenTrue);
        var whenFalse = Visit(memo, conditional.WhenFalse);
        return ReferenceEquals(condition, conditional.Condition) &&
               ReferenceEquals(whenTrue, conditional.WhenTrue) &&
               ReferenceEquals(whenFalse, conditional.WhenFalse)
            ? conditional
            : termFactory.Conditional(condition, whenTrue, whenFalse);
    }

    private static IrTerm RewriteCast(IrFactory termFactory, IrCastTerm cast, Dictionary<IrId, IrTerm> memo)
    {
        var operand = Visit(memo, cast.Operand);
        return ReferenceEquals(operand, cast.Operand)
            ? cast
            : termFactory.Cast(cast.Type, operand);
    }

    private static IrTerm RewriteLength(IrFactory termFactory, IrLengthTerm length, Dictionary<IrId, IrTerm> memo)
    {
        var value = Visit(memo, length.Value);
        return ReferenceEquals(value, length.Value)
            ? length
            : termFactory.Length(value);
    }

    private static IrTerm RewriteSequenceAccess(
        IrFactory termFactory,
        IrSequenceAccessTerm access, Dictionary<IrId, IrTerm> memo)
    {
        var sequence = Visit(memo, access.Sequence);
        var index = Visit(memo, access.Index);
        return ReferenceEquals(sequence, access.Sequence) &&
               ReferenceEquals(index, access.Index)
            ? access
            : termFactory.SequenceAccess(sequence, index);
    }
}
