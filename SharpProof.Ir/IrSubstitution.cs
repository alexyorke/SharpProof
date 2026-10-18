namespace SharpProof.Ir;

public static class IrSubstitution
{
    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IrVarId variable,
        IrTerm replacement)
    {
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(replacement, nameof(replacement));
        factory.EnsureTerm(root, nameof(root));

        var replacementMap = CreateReplacementMap(
            factory,
            ImmutableDictionary<IrVarId, IrTerm>.Empty.Add(variable, replacement));
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
        ImmutableDictionary<IrVarId, IrTerm> replacementMap)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        root = RepositoryParameterGuard.NotNull(root, nameof(root));
        if (replacementMap.Count == 0)
        {
            return root;
        }

        var memo = ImmutableDictionary<IrId, IrTerm>.Empty;
        return Rewrite(
            factory,
            root,
            replacementMap,
            memo,
            allowedVariables: null,
            out _, out _);
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
        replacements = RepositoryParameterGuard.NotNull(replacements, nameof(replacements));
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(root, nameof(root));
        ArgumentNullGuard.NotNull(read, nameof(read));
        factory.EnsureTerm(root, nameof(root));
        var replacementMap = CreateReplacementMap(factory, replacements);
        return Rewrite(factory, root, replacementMap, ImmutableDictionary<IrId, IrTerm>.Empty, allowedVariables: null, out _, out _, read);
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
        var memo = ImmutableDictionary<IrId, IrTerm>.Empty;
        result = Rewrite(
            factory,
            root,
            replacementMap,
            memo,
            freeVariables?.ToImmutableHashSet(),
            out var variablesValid, out _);
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
        var rootSnapshot = roots.ToImmutableArray();
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

        var memo = ImmutableDictionary<IrId, IrTerm>.Empty;
        var result = ImmutableList<IrTerm>.Empty;
        foreach (var root in rootSnapshot)
        {
            result = result.Add(Rewrite(
                factory,
                root,
                replacementMap,
                memo,
                allowedVariables: null,
                out _, out memo));
        }

        return result.ToImmutableArray();
    }

    private static ImmutableDictionary<IrVarId, IrTerm> CreateReplacementMap(
        IrFactory factory,
        IReadOnlyDictionary<IrVarId, IrTerm> replacements)
    {
        // Materialize the caller-supplied view once. IReadOnlyDictionary is an
        // interface, not an immutable snapshot; validation and rewriting must
        // operate on the same mapping.
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var replacementMap = ImmutableDictionary<IrVarId, IrTerm>.Empty;
        var replacementOrder = ImmutableList<KeyValuePair<IrVarId, IrTerm>>.Empty;
        foreach (var replacement in replacements)
        {
            if (replacementMap.ContainsKey(replacement.Key))
            {
                throw new ArgumentException("An item with the same key has already been added. Key: " + replacement.Key);
            }
            replacementMap = replacementMap.Add(replacement.Key, replacement.Value);
            replacementOrder = replacementOrder.Add(replacement);
        }
        foreach (var replacement in replacementOrder)
        {
            var variable = factory.GetVariableInfo(replacement.Key);
            ArgumentNullGuard.NotNull(replacement.Value, nameof(replacements));
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
        ImmutableDictionary<IrVarId, IrTerm> replacements,
        ImmutableDictionary<IrId, IrTerm> memo,
        ImmutableHashSet<IrVarId>? allowedVariables,
        out bool variablesValid,
        out ImmutableDictionary<IrId, IrTerm> updatedMemo,
        Func<IrTerm, Func<IrTerm, IrTerm>, IrTerm?>? read = null)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        memo = RepositoryParameterGuard.NotNull(memo, nameof(memo));
        replacements = RepositoryParameterGuard.NotNull(replacements, nameof(replacements));
        root = RepositoryParameterGuard.NotNull(root, nameof(root));
        var allVariablesValid = true;
        var result = IrTraversal.FoldBottomUp(
            root,
            memo,
            (term, _, rewritten) => rewritten is null ? throw new System.ArgumentNullException(nameof(rewritten)) : term is null ? throw new System.ArgumentNullException(nameof(term)) : ((read != null && term is IrSequenceAccessTerm or IrOpaqueTerm
                ? read(term, child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (rewritten[child.Id])) : null) ?? RewriteNode(factory, term, rewritten)),
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
        updatedMemo = result.Memo;
        return result.Value;
    }

    private static IrTerm RewriteNode(
        IrFactory factory,
        IrTerm term,
        ImmutableDictionary<IrId, IrTerm> memo)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        memo = RepositoryParameterGuard.NotNull(memo, nameof(memo));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        return term.Accept(RewriteVisitor.Instance, (factory, memo));
    }

    private sealed class RewriteVisitor : IIrTermVisitor<(IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo), IrTerm>
    {
        internal static RewriteVisitor Instance { get; } = new();

        public IrTerm Visit(IrBooleanTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrIntegerTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrStringTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrNullTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrEmptyArrayTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrVariableTerm term, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return term;
        }

        public IrTerm Visit(IrOpaqueTerm opaque, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            var receiver = IrSubstitution.VisitNullable(state.Memo, opaque.Receiver);
            var arguments = opaque.Arguments;
            var argumentsChanged = false;
            for (var index = 0; index < opaque.Arguments.Length; index++)
            {
                var argument = IrSubstitution.Visit(state.Memo, opaque.Arguments[index]);
                if (ReferenceEquals(argument, opaque.Arguments[index]))
                {
                    continue;
                }

                arguments = arguments.SetItem(index, argument);
                argumentsChanged = true;
            }

            if (ReferenceEquals(receiver, opaque.Receiver) &&
                !argumentsChanged)
            {
                return opaque;
            }

            return opaque.Purity == IrOpaquePurity.Pure
                ? state.Factory.PureOpaque(
                    opaque.Member,
                    receiver,
                    arguments.ToArray())
                : state.Factory.ImpureOpaque(
                    opaque.Operation,
                    opaque.Member,
                    receiver,
                    arguments.ToArray());
        }

        public IrTerm Visit(IrUnaryTerm unary, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            unary = RepositoryParameterGuard.NotNull(unary, nameof(unary));
            var operand = IrSubstitution.Visit(state.Memo, unary.Operand);
            return ReferenceEquals(operand, unary.Operand)
                ? unary
                : state.Factory.Unary(unary.Operator, operand);
        }

        public IrTerm Visit(IrBinaryTerm binary, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            binary = RepositoryParameterGuard.NotNull(binary, nameof(binary));
            var left = IrSubstitution.Visit(state.Memo, binary.Left);
            var right = IrSubstitution.Visit(state.Memo, binary.Right);
            return ReferenceEquals(left, binary.Left) &&
                   ReferenceEquals(right, binary.Right)
                ? binary
                : state.Factory.RewriteBinary(binary.Operator, left, right);
        }

        public IrTerm Visit(IrConditionalTerm conditional, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            conditional = RepositoryParameterGuard.NotNull(conditional, nameof(conditional));
            var condition = IrSubstitution.Visit(state.Memo, conditional.Condition);
            var whenTrue = IrSubstitution.Visit(state.Memo, conditional.WhenTrue);
            var whenFalse = IrSubstitution.Visit(state.Memo, conditional.WhenFalse);
            return ReferenceEquals(condition, conditional.Condition) &&
                   ReferenceEquals(whenTrue, conditional.WhenTrue) &&
                   ReferenceEquals(whenFalse, conditional.WhenFalse)
                ? conditional
                : state.Factory.Conditional(condition, whenTrue, whenFalse);
        }

        public IrTerm Visit(IrCastTerm cast, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            cast = RepositoryParameterGuard.NotNull(cast, nameof(cast));
            var operand = IrSubstitution.Visit(state.Memo, cast.Operand);
            return ReferenceEquals(operand, cast.Operand)
                ? cast
                : state.Factory.Cast(cast.Type, operand);
        }

        public IrTerm Visit(IrLengthTerm length, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            length = RepositoryParameterGuard.NotNull(length, nameof(length));
            var value = IrSubstitution.Visit(state.Memo, length.Value);
            return ReferenceEquals(value, length.Value)
                ? length
                : state.Factory.Length(value);
        }

        public IrTerm Visit(IrSequenceAccessTerm access, (IrFactory Factory, ImmutableDictionary<IrId, IrTerm> Memo) state)
        {
            access = RepositoryParameterGuard.NotNull(access, nameof(access));
            var sequence = IrSubstitution.Visit(state.Memo, access.Sequence);
            var index = IrSubstitution.Visit(state.Memo, access.Index);
            return ReferenceEquals(sequence, access.Sequence) &&
                   ReferenceEquals(index, access.Index)
                ? access
                : state.Factory.SequenceAccess(sequence, index);
        }
    }

    private static IrTerm Visit(ImmutableDictionary<IrId, IrTerm> memo, IrTerm child)
    {
        child = RepositoryParameterGuard.NotNull(child, nameof(child));
        memo = RepositoryParameterGuard.NotNull(memo, nameof(memo));
        return memo[child.Id];
    }

    private static IrTerm? VisitNullable(ImmutableDictionary<IrId, IrTerm> memo, IrTerm? child)
    {
        memo = RepositoryParameterGuard.NotNull(memo, nameof(memo));
        return child == null ? null : Visit(memo, child);
    }
}
