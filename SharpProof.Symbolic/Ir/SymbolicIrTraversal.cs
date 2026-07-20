namespace SharpProof.Symbolic.Ir;

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

    internal void Visit(SymbolicAtom atom) {
        switch (atom) {
            case SymbolicTruthAtom truth:
                Visit(truth.Condition);
                break;
            case SymbolicRelationAtom relation:
                Visit(relation.Left);
                Visit(relation.Right);
                break;
            case SymbolicStringPredicateAtom predicate:
                Visit(predicate.Value);
                Visit(predicate.Argument);
                break;
            case SymbolicBoundsAtom bounds:
                Visit(bounds.Index);
                Visit(bounds.Length);
                break;
            case SymbolicFreshnessAtom freshness:
                Visit(freshness.Value);
                break;
            case SymbolicOwnershipAtom ownership:
                Visit(ownership.Value);
                break;
            case SymbolicAliasAtom alias:
                Visit(alias.Source);
                Visit(alias.Target);
                break;
            case SymbolicBorrowAtom borrow:
                Visit(borrow.Owner);
                Visit(borrow.Borrow);
                break;
            case SymbolicEscapeAtom escape:
                Visit(escape.Value);
                break;
            case SymbolicReturnedOwnershipAtom returned:
                Visit(returned.Value);
                break;
            case SymbolicMutationAtom mutation:
                Visit(mutation.Target);
                break;
            case SymbolicDisposalAtom disposal:
                Visit(disposal.Resource);
                break;
            case SymbolicResourceLifetimeAtom lifetime:
                Visit(lifetime.Resource);
                break;
            case SymbolicTypeTestAtom typeTest:
                Visit(typeTest.Value);
                break;
            case SymbolicExceptionPreconditionAtom precondition:
                if (precondition.Subject != null) Visit(precondition.Subject);
                Visit(precondition.Trigger);
                break;
        }
    }

    internal void Visit(SymbolicTerm term) {
        OnTerm(term);
        switch (term) {
            case SymbolicVariableTerm variable:
                OnVariableLikeName(variable.Name);
                break;
            case SymbolicNullableHasValueTerm nullableHasValue:
                OnVariableLikeName(nullableHasValue.NullableName);
                break;
            case SymbolicNullableValueTerm nullableValue:
                OnVariableLikeName(nullableValue.NullableName);
                break;
            case SymbolicMemberTerm member:
                Visit(member.Receiver);
                break;
            case SymbolicElementTerm element:
                Visit(element.Receiver);
                Visit(element.Index);
                break;
            case SymbolicMultiElementTerm element:
                Visit(element.Receiver);
                foreach (var index in element.Indices) Visit(index);
                break;
            case SymbolicFromEndIndexTerm fromEnd:
                Visit(fromEnd.Value);
                break;
            case SymbolicStringContentTerm content:
                Visit(content.Reference);
                break;
            case SymbolicStringConcatTerm concat:
                Visit(concat.Left);
                Visit(concat.Right);
                break;
            case SymbolicLengthTerm length:
                Visit(length.Value);
                break;
            case SymbolicArrayDimensionLengthTerm length:
                Visit(length.Value);
                break;
            case SymbolicCountTerm count:
                Visit(count.Value);
                break;
            case SymbolicBinaryTerm binary:
                Visit(binary.Left);
                Visit(binary.Right);
                break;
            case SymbolicConditionalTerm conditional:
                Visit(conditional.Condition);
                Visit(conditional.WhenTrue);
                Visit(conditional.WhenFalse);
                break;
        }
    }

    protected virtual void OnTerm(SymbolicTerm term) {
    }

    protected virtual void OnVariableLikeName(string name) {
    }
}
