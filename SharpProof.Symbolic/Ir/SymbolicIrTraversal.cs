namespace SharpProof.Symbolic.Ir;

/// <summary>
/// The direct children of an IR atom or term, in the one shape every structural
/// walker needs: at most two term children, an optional condition child, and the
/// variable-length indices carried by a multi-element term.
/// </summary>
/// <remarks>
/// This is deliberately a struct returned by value rather than an iterator: the
/// walkers below run per symbolic state on the analysis hot path, where an
/// allocation per visited node would be charged against the SMT wall-clock budget.
/// Children are consumed in field order, so a walker observes terms before the
/// condition child. Both <see cref="SymbolicIrVisitor"/> subclasses are
/// order-insensitive (one sorts its output by key, the other sets a found flag),
/// and the folds built on this are boolean, so the order is not load-bearing.
/// Rewriting is not expressible here because it must reconstruct each record with
/// its own constructor; see <see cref="SymbolicIrRewriter"/>.
/// </remarks>
internal readonly record struct SymbolicIrChildren(
    SymbolicTerm? First = null,
    SymbolicTerm? Second = null,
    SymbolicCondition? Condition = null,
    ImmutableArray<SymbolicTerm> Rest = default) {
    internal static SymbolicIrChildren OfAtom(SymbolicAtom atom) => atom switch {
        // Named Condition, but declared as a term: SymbolicTruthAtom(SymbolicTerm Condition).
        SymbolicTruthAtom truth => new(truth.Condition),
        SymbolicRelationAtom relation => new(relation.Left, relation.Right),
        SymbolicStringPredicateAtom predicate => new(predicate.Value, predicate.Argument),
        SymbolicBoundsAtom bounds => new(bounds.Index, bounds.Length),
        SymbolicFreshnessAtom freshness => new(freshness.Value),
        SymbolicOwnershipAtom ownership => new(ownership.Value),
        SymbolicAliasAtom alias => new(alias.Source, alias.Target),
        SymbolicBorrowAtom borrow => new(borrow.Owner, borrow.Borrow),
        SymbolicEscapeAtom escape => new(escape.Value),
        SymbolicReturnedOwnershipAtom returnedOwnership => new(returnedOwnership.Value),
        SymbolicMutationAtom mutation => new(mutation.Target),
        SymbolicDisposalAtom disposal => new(disposal.Resource),
        SymbolicResourceLifetimeAtom lifetime => new(lifetime.Resource),
        SymbolicTypeTestAtom typeTest => new(typeTest.Value),
        SymbolicExceptionPreconditionAtom precondition =>
            new(precondition.Subject, Condition: precondition.Trigger),
        _ => default,
    };

    /// <summary>
    /// Children of the terms whose sub-terms are traversed uniformly. Leaves and the
    /// name-carrying terms yield nothing; <see cref="SymbolicBinaryTerm"/> and
    /// <see cref="SymbolicConditionalTerm"/> are included, but callers that treat
    /// either specially — the divide/remainder predicate, or refining a context across
    /// a conditional — must match those before falling back to this.
    /// </summary>
    internal static SymbolicIrChildren OfTerm(SymbolicTerm term) => term switch {
        SymbolicMemberTerm member => new(member.Receiver),
        SymbolicElementTerm element => new(element.Receiver, element.Index),
        SymbolicMultiElementTerm element => new(element.Receiver, Rest: element.Indices),
        SymbolicFromEndIndexTerm fromEnd => new(fromEnd.Value),
        SymbolicStringContentTerm stringContent => new(stringContent.Reference),
        SymbolicStringConcatTerm stringConcat => new(stringConcat.Left, stringConcat.Right),
        SymbolicLengthTerm length => new(length.Value),
        SymbolicArrayDimensionLengthTerm arrayLength => new(arrayLength.Value),
        SymbolicCountTerm count => new(count.Value),
        SymbolicBinaryTerm binary => new(binary.Left, binary.Right),
        SymbolicConditionalTerm conditional =>
            new(conditional.WhenTrue, conditional.WhenFalse, conditional.Condition),
        _ => default,
    };

    /// <summary>
    /// Returns whether any child term satisfies <paramref name="predicate"/>. Pass a
    /// static method group so the delegate is cached rather than allocated per call.
    /// </summary>
    internal bool AnyTerm(Func<SymbolicTerm, bool> predicate) =>
        First != null && predicate(First) ||
        Second != null && predicate(Second) ||
        !Rest.IsDefaultOrEmpty && Rest.Any(predicate);
}

internal abstract class SymbolicIrRewriter {
    internal SymbolicFact Rewrite(SymbolicFact fact) {
        var atom = Rewrite(fact.Atom);
        return ReferenceEquals(atom, fact.Atom) ? fact : fact with { Atom = atom };
    }

    internal SymbolicCondition Rewrite(SymbolicCondition condition) => condition switch {
        SymbolicConstantCondition => condition,
        SymbolicFactCondition factCondition => RewriteFactCondition(factCondition),
        SymbolicNotCondition notCondition => RewriteNotCondition(notCondition),
        SymbolicBinaryCondition binaryCondition => RewriteBinaryCondition(binaryCondition),
        _ => condition
    };

    internal SymbolicAtom Rewrite(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => new SymbolicTruthAtom(Rewrite(truth.Condition)),
        SymbolicRelationAtom relation => new SymbolicRelationAtom(
            relation.Operator,
            Rewrite(relation.Left),
            Rewrite(relation.Right)),
        SymbolicStringPredicateAtom predicate => new SymbolicStringPredicateAtom(
            predicate.Predicate,
            Rewrite(predicate.Value),
            Rewrite(predicate.Argument),
            predicate.RegexOptions),
        SymbolicBoundsAtom bounds => new SymbolicBoundsAtom(
            Rewrite(bounds.Index),
            Rewrite(bounds.Length),
            bounds.IncludeLowerBound,
            bounds.IncludeUpperBound),
        SymbolicFreshnessAtom freshness => new SymbolicFreshnessAtom(Rewrite(freshness.Value)),
        SymbolicOwnershipAtom ownership => new SymbolicOwnershipAtom(
            Rewrite(ownership.Value),
            ownership.Escaped),
        SymbolicAliasAtom alias => new SymbolicAliasAtom(
            Rewrite(alias.Source),
            Rewrite(alias.Target),
            alias.MayAlias),
        SymbolicBorrowAtom borrow => new SymbolicBorrowAtom(
            Rewrite(borrow.Owner),
            Rewrite(borrow.Borrow),
            borrow.Kind),
        SymbolicEscapeAtom escape => new SymbolicEscapeAtom(Rewrite(escape.Value), escape.Kind),
        SymbolicReturnedOwnershipAtom returned => new SymbolicReturnedOwnershipAtom(Rewrite(returned.Value)),
        SymbolicMutationAtom mutation => new SymbolicMutationAtom(
            Rewrite(mutation.Target),
            mutation.CallerVisible),
        SymbolicDisposalAtom disposal => new SymbolicDisposalAtom(
            Rewrite(disposal.Resource),
            disposal.State),
        SymbolicResourceLifetimeAtom lifetime => new SymbolicResourceLifetimeAtom(
            Rewrite(lifetime.Resource),
            lifetime.State),
        SymbolicExactRuntimeTypeAtom exactRuntimeType => new SymbolicExactRuntimeTypeAtom(
            Rewrite(exactRuntimeType.Value),
            exactRuntimeType.TypeKey),
        SymbolicTypeTestAtom typeTest => new SymbolicTypeTestAtom(
            Rewrite(typeTest.Value),
            typeTest.TypeKey),
        SymbolicExceptionPreconditionAtom precondition => new SymbolicExceptionPreconditionAtom(
            precondition.Kind,
            precondition.Subject == null ? null : Rewrite(precondition.Subject),
            Rewrite(precondition.Trigger)),
        _ => atom
    };

    internal SymbolicTerm Rewrite(SymbolicTerm term) {
        if (TryRewriteTerm(term, out var rewritten)) return rewritten;

        return term switch {
            SymbolicBooleanConstantTerm or
                SymbolicIntegerConstantTerm or
                SymbolicStringConstantTerm or
                SymbolicNullTerm or
                SymbolicVariableTerm or
                SymbolicNullableHasValueTerm or
                SymbolicNullableValueTerm or
                SymbolicNumericConversionTerm => term,
            SymbolicMemberTerm member => new SymbolicMemberTerm(
                Rewrite(member.Receiver),
                member.MemberName,
                member.Kind),
            SymbolicElementTerm element => new SymbolicElementTerm(
                Rewrite(element.Receiver),
                Rewrite(element.Index),
                element.Kind),
            SymbolicMultiElementTerm element => new SymbolicMultiElementTerm(
                Rewrite(element.Receiver),
                element.Indices.Select(Rewrite).ToImmutableArray(),
                element.Kind),
            SymbolicFromEndIndexTerm fromEnd => new SymbolicFromEndIndexTerm(Rewrite(fromEnd.Value)),
            SymbolicStringContentTerm content => new SymbolicStringContentTerm(Rewrite(content.Reference)),
            SymbolicStringConcatTerm concat => new SymbolicStringConcatTerm(
                Rewrite(concat.Left),
                Rewrite(concat.Right)),
            SymbolicLengthTerm length => new SymbolicLengthTerm(Rewrite(length.Value)),
            SymbolicArrayDimensionLengthTerm length => new SymbolicArrayDimensionLengthTerm(
                Rewrite(length.Value),
                length.Dimension),
            SymbolicCountTerm count => new SymbolicCountTerm(Rewrite(count.Value)),
            SymbolicBinaryTerm binary => new SymbolicBinaryTerm(
                binary.Operator,
                Rewrite(binary.Left),
                Rewrite(binary.Right),
                binary.MayOverflow),
            SymbolicConditionalTerm conditional => new SymbolicConditionalTerm(
                Rewrite(conditional.Condition),
                Rewrite(conditional.WhenTrue),
                Rewrite(conditional.WhenFalse)),
            _ => term
        };
    }

    protected virtual bool TryRewriteTerm(SymbolicTerm term, out SymbolicTerm rewritten) {
        rewritten = null!;
        return false;
    }

    private SymbolicCondition RewriteFactCondition(SymbolicFactCondition condition) {
        var fact = Rewrite(condition.Fact);
        return ReferenceEquals(fact, condition.Fact) ? condition : new SymbolicFactCondition(fact);
    }

    private SymbolicCondition RewriteNotCondition(SymbolicNotCondition condition) {
        var operand = Rewrite(condition.Operand);
        return ReferenceEquals(operand, condition.Operand) ? condition : new SymbolicNotCondition(operand);
    }

    private SymbolicCondition RewriteBinaryCondition(SymbolicBinaryCondition condition) {
        var left = Rewrite(condition.Left);
        var right = Rewrite(condition.Right);
        return ReferenceEquals(left, condition.Left) && ReferenceEquals(right, condition.Right)
            ? condition
            : new SymbolicBinaryCondition(condition.Operator, left, right);
    }
}

internal abstract class SymbolicIrVisitor {
    internal void Visit(SymbolicFact fact) => Visit(fact.Atom);

    internal void Visit(SymbolicCondition condition) {
        switch (condition) {
            case SymbolicFactCondition factCondition:
                Visit(factCondition.Fact);
                break;
            case SymbolicNotCondition notCondition:
                Visit(notCondition.Operand);
                break;
            case SymbolicBinaryCondition binaryCondition:
                Visit(binaryCondition.Left);
                Visit(binaryCondition.Right);
                break;
        }
    }

    internal void Visit(SymbolicAtom atom) => VisitChildren(SymbolicIrChildren.OfAtom(atom));

    internal void Visit(SymbolicTerm term) {
        OnTerm(term);
        switch (term) {
            case SymbolicVariableTerm variable:
                OnVariableLikeName(variable.Name);
                return;
            case SymbolicNullableHasValueTerm nullableHasValue:
                OnVariableLikeName(nullableHasValue.NullableName);
                return;
            case SymbolicNullableValueTerm nullableValue:
                OnVariableLikeName(nullableValue.NullableName);
                return;
        }

        VisitChildren(SymbolicIrChildren.OfTerm(term));
    }

    private void VisitChildren(SymbolicIrChildren children) {
        if (children.First != null) Visit(children.First);
        if (children.Second != null) Visit(children.Second);
        if (!children.Rest.IsDefaultOrEmpty)
            foreach (var index in children.Rest)
                Visit(index);
        if (children.Condition != null) Visit(children.Condition);
    }

    protected virtual void OnTerm(SymbolicTerm term) {
    }

    protected virtual void OnVariableLikeName(string name) {
    }
}
