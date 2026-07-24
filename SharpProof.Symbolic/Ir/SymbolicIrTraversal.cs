namespace SharpProof.Symbolic.Ir;
internal readonly record struct SymbolicIrChildren(
    SymbolicTerm? First = null,
    SymbolicTerm? Second = null,
    SymbolicCondition? Condition = null,
    ImmutableArray<SymbolicTerm> Rest = default) {
    internal static SymbolicIrChildren Of(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => new(truth.Condition),
        SymbolicRelationAtom relation => new(relation.Left, relation.Right),
        SymbolicStringPredicateAtom predicate => new(predicate.Value, predicate.Argument),
        SymbolicBoundsAtom bounds => new(bounds.Index, bounds.Length),
        SymbolicTypeTestAtom typeTest => new(typeTest.Value),
        SymbolicExceptionPreconditionAtom precondition => new(precondition.Subject, Condition: precondition.Trigger),
        _ => default
    };
    internal static SymbolicIrChildren Of(SymbolicTerm term) => term switch {
        SymbolicMemberTerm member => new(member.Receiver),
        SymbolicElementTerm element => new(element.Receiver, element.Index),
        SymbolicMultiElementTerm element => new(element.Receiver, Rest: element.Indices),
        SymbolicFromEndIndexTerm fromEnd => new(fromEnd.Value),
        SymbolicStringContentTerm content => new(content.Reference),
        SymbolicStringConcatTerm concat => new(concat.Left, concat.Right),
        SymbolicStringSliceTerm slice => new(slice.Value, slice.Offset, Rest: [slice.Length]),
        SymbolicLengthTerm length => new(length.Value),
        SymbolicArrayDimensionLengthTerm length => new(length.Value),
        SymbolicCountTerm count => new(count.Value),
        SymbolicBinaryTerm binary => new(binary.Left, binary.Right),
        SymbolicConditionalTerm conditional => new(conditional.WhenTrue, conditional.WhenFalse, conditional.Condition),
        _ => default
    };
}
internal static class SymbolicAlgebra {
    internal static SymbolicFact Rewrite(SymbolicFact fact, Func<SymbolicTerm, SymbolicTerm?> replace) =>
        fact with { Atom = Rewrite(fact.Atom, replace) };
    internal static SymbolicCondition Rewrite(SymbolicCondition condition, Func<SymbolicTerm, SymbolicTerm?> replace) =>
        condition switch {
            SymbolicFactCondition fact => fact with { Fact = Rewrite(fact.Fact, replace) },
            SymbolicNotCondition not => not with { Operand = Rewrite(not.Operand, replace) },
            SymbolicBinaryCondition binary => binary with {
                Left = Rewrite(binary.Left, replace),
                Right = Rewrite(binary.Right, replace)
            },
            _ => condition
        };
    internal static SymbolicAtom Rewrite(SymbolicAtom atom, Func<SymbolicTerm, SymbolicTerm?> replace) => atom switch {
        SymbolicTruthAtom truth => truth with { Condition = Rewrite(truth.Condition, replace) },
        SymbolicRelationAtom relation => relation with {
            Left = Rewrite(relation.Left, replace),
            Right = Rewrite(relation.Right, replace)
        },
        SymbolicStringPredicateAtom predicate => predicate with {
            Value = Rewrite(predicate.Value, replace),
            Argument = Rewrite(predicate.Argument, replace)
        },
        SymbolicBoundsAtom bounds => bounds with {
            Index = Rewrite(bounds.Index, replace),
            Length = Rewrite(bounds.Length, replace)
        },
        SymbolicExactRuntimeTypeAtom exact => exact with { Value = Rewrite(exact.Value, replace) },
        SymbolicTypeTestAtom typeTest => typeTest with { Value = Rewrite(typeTest.Value, replace) },
        SymbolicExceptionPreconditionAtom precondition => precondition with {
            Subject = precondition.Subject == null ? null : Rewrite(precondition.Subject, replace),
            Trigger = Rewrite(precondition.Trigger, replace)
        },
        _ => atom
    };
    internal static SymbolicTerm Rewrite(SymbolicTerm term, Func<SymbolicTerm, SymbolicTerm?> replace) {
        var replacement = replace(term);
        if (replacement != null) return replacement;
        return term switch {
            SymbolicMemberTerm member => member with { Receiver = Rewrite(member.Receiver, replace) },
            SymbolicElementTerm element => element with {
                Receiver = Rewrite(element.Receiver, replace),
                Index = Rewrite(element.Index, replace)
            },
            SymbolicMultiElementTerm element => element with {
                Receiver = Rewrite(element.Receiver, replace),
                Indices = [.. element.Indices.Select(term => Rewrite(term, replace))]
            },
            SymbolicFromEndIndexTerm fromEnd => fromEnd with { Value = Rewrite(fromEnd.Value, replace) },
            SymbolicStringContentTerm content => content with { Reference = Rewrite(content.Reference, replace) },
            SymbolicStringConcatTerm concat => concat with {
                Left = Rewrite(concat.Left, replace),
                Right = Rewrite(concat.Right, replace)
            },
            SymbolicStringSliceTerm slice => slice with {
                Value = Rewrite(slice.Value, replace),
                Offset = Rewrite(slice.Offset, replace),
                Length = Rewrite(slice.Length, replace)
            },
            SymbolicLengthTerm length => length with { Value = Rewrite(length.Value, replace) },
            SymbolicArrayDimensionLengthTerm length => length with { Value = Rewrite(length.Value, replace) },
            SymbolicCountTerm count => count with { Value = Rewrite(count.Value, replace) },
            SymbolicBinaryTerm binary => binary with {
                Left = Rewrite(binary.Left, replace),
                Right = Rewrite(binary.Right, replace)
            },
            SymbolicConditionalTerm conditional => conditional with {
                Condition = Rewrite(conditional.Condition, replace),
                WhenTrue = Rewrite(conditional.WhenTrue, replace),
                WhenFalse = Rewrite(conditional.WhenFalse, replace)
            },
            _ => term
        };
    }
    internal static bool Any(SymbolicFact fact, Func<SymbolicTerm, bool> predicate) => Any(fact.Atom, predicate);
    internal static bool Any(SymbolicAtom atom, Func<SymbolicTerm, bool> predicate) =>
        Any(SymbolicIrChildren.Of(atom), predicate);
    internal static bool Any(SymbolicCondition condition, Func<SymbolicTerm, bool> predicate) => condition switch {
        SymbolicFactCondition fact => Any(fact.Fact, predicate),
        SymbolicNotCondition not => Any(not.Operand, predicate),
        SymbolicBinaryCondition binary => Any(binary.Left, predicate) || Any(binary.Right, predicate),
        _ => false
    };
    internal static bool Any(SymbolicTerm term, Func<SymbolicTerm, bool> predicate) =>
        predicate(term) || Any(SymbolicIrChildren.Of(term), predicate);
    internal static void Visit(SymbolicFact fact, Action<SymbolicTerm> action) =>
        Any(fact, term => {
            action(term);
            return false;
        });
    internal static void Visit(SymbolicCondition condition, Action<SymbolicTerm> action) =>
        Any(condition, term => {
            action(term);
            return false;
        });
    private static bool Any(SymbolicIrChildren children, Func<SymbolicTerm, bool> predicate) =>
        children.First != null && Any(children.First, predicate) ||
        children.Second != null && Any(children.Second, predicate) ||
        !children.Rest.IsDefaultOrEmpty && children.Rest.Any(term => Any(term, predicate)) ||
        children.Condition != null && Any(children.Condition, predicate);
}
