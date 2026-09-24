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
        out bool variablesValid)
    {
        var allVariablesValid = true;
        var result = IrTraversal.FoldBottomUp(
            root,
            memo,
            (term, _, rewritten) => RewriteNode(factory, term, rewritten),
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
        IrTerm Visit(IrTerm child)
        {
            return memo[child.Id];
        }

        IrTerm? VisitNullable(IrTerm? child)
        {
            return child == null ? null : Visit(child);
        }

        return term switch
        {
            IrBooleanTerm or IrIntegerTerm or IrStringTerm or IrNullTerm or IrVariableTerm => term,
            IrOpaqueTerm opaque => RewriteOpaque(factory, opaque),
            IrUnaryTerm unary => RewriteUnary(factory, unary),
            IrBinaryTerm binary => RewriteBinary(factory, binary),
            IrConditionalTerm conditional => RewriteConditional(factory, conditional),
            IrCastTerm cast => RewriteCast(factory, cast),
            IrLengthTerm length => RewriteLength(factory, length),
            IrSequenceAccessTerm access => RewriteSequenceAccess(factory, access),
            _ => throw new InvalidOperationException("Unknown IR term kind: " + term.Kind + ".")
        };

        IrTerm RewriteOpaque(IrFactory termFactory, IrOpaqueTerm opaque)
        {
            var receiver = VisitNullable(opaque.Receiver);
            IrTerm[]? arguments = null;
            for (var index = 0; index < opaque.Arguments.Length; index++)
            {
                var argument = Visit(opaque.Arguments[index]);
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

        IrTerm RewriteUnary(IrFactory termFactory, IrUnaryTerm unary)
        {
            var operand = Visit(unary.Operand);
            return ReferenceEquals(operand, unary.Operand)
                ? unary
                : termFactory.Unary(unary.Operator, operand);
        }

        IrTerm RewriteBinary(IrFactory termFactory, IrBinaryTerm binary)
        {
            var left = Visit(binary.Left);
            var right = Visit(binary.Right);
            return ReferenceEquals(left, binary.Left) &&
                   ReferenceEquals(right, binary.Right)
                ? binary
                : termFactory.RewriteBinary(binary.Operator, left, right);
        }

        IrTerm RewriteConditional(
            IrFactory termFactory,
            IrConditionalTerm conditional)
        {
            var condition = Visit(conditional.Condition);
            var whenTrue = Visit(conditional.WhenTrue);
            var whenFalse = Visit(conditional.WhenFalse);
            return ReferenceEquals(condition, conditional.Condition) &&
                   ReferenceEquals(whenTrue, conditional.WhenTrue) &&
                   ReferenceEquals(whenFalse, conditional.WhenFalse)
                ? conditional
                : termFactory.Conditional(condition, whenTrue, whenFalse);
        }

        IrTerm RewriteCast(IrFactory termFactory, IrCastTerm cast)
        {
            var operand = Visit(cast.Operand);
            return ReferenceEquals(operand, cast.Operand)
                ? cast
                : termFactory.Cast(cast.Type, operand);
        }

        IrTerm RewriteLength(IrFactory termFactory, IrLengthTerm length)
        {
            var value = Visit(length.Value);
            return ReferenceEquals(value, length.Value)
                ? length
                : termFactory.Length(value);
        }

        IrTerm RewriteSequenceAccess(
            IrFactory termFactory,
            IrSequenceAccessTerm access)
        {
            var sequence = Visit(access.Sequence);
            var index = Visit(access.Index);
            return ReferenceEquals(sequence, access.Sequence) &&
                   ReferenceEquals(index, access.Index)
                ? access
                : termFactory.SequenceAccess(sequence, index);
        }
    }
}
