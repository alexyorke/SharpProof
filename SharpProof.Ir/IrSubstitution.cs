namespace SharpProof.Ir;

public static class IrSubstitution
{
    public static IrTerm Substitute(
        IrFactory factory,
        IrTerm root,
        IrVarId variable,
        IrTerm replacement)
    {
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
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
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (root is null)
        {
            throw new System.ArgumentNullException(nameof(root));
        }
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
        if (replacements is null)
        {
            throw new System.ArgumentNullException(nameof(replacements));
        }
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
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        var replacementMap = replacements.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value);
        foreach (var replacement in replacementMap)
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
        Dictionary<IrVarId, IrTerm> replacements,
        Dictionary<IrId, IrTerm> memo,
        ISet<IrVarId>? allowedVariables,
        out bool variablesValid,
        Func<IrTerm, Func<IrTerm, IrTerm>, IrTerm?>? read = null)
    {
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (memo is null)
        {
            throw new System.ArgumentNullException(nameof(memo));
        }
        if (replacements is null)
        {
            throw new System.ArgumentNullException(nameof(replacements));
        }
        if (root is null)
        {
            throw new System.ArgumentNullException(nameof(root));
        }
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
        return result;
    }

    private static IrTerm RewriteNode(
        IrFactory factory,
        IrTerm term,
        Dictionary<IrId, IrTerm> memo)
    {
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (memo is null)
        {
            throw new System.ArgumentNullException(nameof(memo));
        }
        if (term is null)
        {
            throw new System.ArgumentNullException(nameof(term));
        }
        return term.Accept(RewriteVisitor.Instance, (factory, memo));
    }

    private sealed class RewriteVisitor : IIrTermVisitor<(IrFactory Factory, Dictionary<IrId, IrTerm> Memo), IrTerm>
    {
        internal static RewriteVisitor Instance { get; } = new();

        public IrTerm Visit(IrBooleanTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrIntegerTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrStringTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrNullTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrEmptyArrayTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrVariableTerm term, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (term is null)
            {
                throw new System.ArgumentNullException(nameof(term));
            }
            return term;
        }

        public IrTerm Visit(IrOpaqueTerm opaque, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            var receiver = IrSubstitution.VisitNullable(state.Memo, opaque.Receiver);
            IrTerm[]? arguments = null;
            for (var index = 0; index < opaque.Arguments.Length; index++)
            {
                var argument = IrSubstitution.Visit(state.Memo, opaque.Arguments[index]);
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
                ? state.Factory.PureOpaque(
                    opaque.Member,
                    receiver,
                    arguments)
                : state.Factory.ImpureOpaque(
                    opaque.Operation,
                    opaque.Member,
                    receiver,
                    arguments);
        }

        public IrTerm Visit(IrUnaryTerm unary, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (unary is null)
            {
                throw new System.ArgumentNullException(nameof(unary));
            }
            var operand = IrSubstitution.Visit(state.Memo, unary.Operand);
            return ReferenceEquals(operand, unary.Operand)
                ? unary
                : state.Factory.Unary(unary.Operator, operand);
        }

        public IrTerm Visit(IrBinaryTerm binary, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (binary is null)
            {
                throw new System.ArgumentNullException(nameof(binary));
            }
            var left = IrSubstitution.Visit(state.Memo, binary.Left);
            var right = IrSubstitution.Visit(state.Memo, binary.Right);
            return ReferenceEquals(left, binary.Left) &&
                   ReferenceEquals(right, binary.Right)
                ? binary
                : state.Factory.RewriteBinary(binary.Operator, left, right);
        }

        public IrTerm Visit(IrConditionalTerm conditional, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (conditional is null)
            {
                throw new System.ArgumentNullException(nameof(conditional));
            }
            var condition = IrSubstitution.Visit(state.Memo, conditional.Condition);
            var whenTrue = IrSubstitution.Visit(state.Memo, conditional.WhenTrue);
            var whenFalse = IrSubstitution.Visit(state.Memo, conditional.WhenFalse);
            return ReferenceEquals(condition, conditional.Condition) &&
                   ReferenceEquals(whenTrue, conditional.WhenTrue) &&
                   ReferenceEquals(whenFalse, conditional.WhenFalse)
                ? conditional
                : state.Factory.Conditional(condition, whenTrue, whenFalse);
        }

        public IrTerm Visit(IrCastTerm cast, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (cast is null)
            {
                throw new System.ArgumentNullException(nameof(cast));
            }
            var operand = IrSubstitution.Visit(state.Memo, cast.Operand);
            return ReferenceEquals(operand, cast.Operand)
                ? cast
                : state.Factory.Cast(cast.Type, operand);
        }

        public IrTerm Visit(IrLengthTerm length, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (length is null)
            {
                throw new System.ArgumentNullException(nameof(length));
            }
            var value = IrSubstitution.Visit(state.Memo, length.Value);
            return ReferenceEquals(value, length.Value)
                ? length
                : state.Factory.Length(value);
        }

        public IrTerm Visit(IrSequenceAccessTerm access, (IrFactory Factory, Dictionary<IrId, IrTerm> Memo) state)
        {
            if (access is null)
            {
                throw new System.ArgumentNullException(nameof(access));
            }
            var sequence = IrSubstitution.Visit(state.Memo, access.Sequence);
            var index = IrSubstitution.Visit(state.Memo, access.Index);
            return ReferenceEquals(sequence, access.Sequence) &&
                   ReferenceEquals(index, access.Index)
                ? access
                : state.Factory.SequenceAccess(sequence, index);
        }
    }

    private static IrTerm Visit(Dictionary<IrId, IrTerm> memo, IrTerm child)
    {
        if (child is null)
        {
            throw new System.ArgumentNullException(nameof(child));
        }
        if (memo is null)
        {
            throw new System.ArgumentNullException(nameof(memo));
        }
        return memo[child.Id];
    }

    private static IrTerm? VisitNullable(Dictionary<IrId, IrTerm> memo, IrTerm? child)
    {
        if (memo is null)
        {
            throw new System.ArgumentNullException(nameof(memo));
        }
        return child == null ? null : Visit(memo, child);
    }
}
