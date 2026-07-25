namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIrFormulaEncoder {
    internal static bool TryEncode(SymbolicCondition condition, out SmtFormula formula) =>
        Try(Encode(condition), out formula);
    internal static bool TryEncode(SymbolicFact fact, out SmtFormula formula) =>
        Try(Encode(fact), out formula);
    internal static bool TryEncode(SymbolicAtom atom, out SmtFormula formula) =>
        Try(Encode(atom), out formula);
    internal static bool TryEncodeTerm(SymbolicTerm term, out SmtFormula formula) =>
        Try(Encode(term), out formula);
    private static SmtFormula? Encode(SymbolicCondition condition) => condition switch {
        SymbolicConstantCondition constant => new SmtBooleanConstant(constant.Value),
        SymbolicFactCondition fact => Encode(fact.Fact),
        SymbolicNotCondition not => Unary(SmtUnaryOperator.Not, Encode(not.Operand)),
        SymbolicBinaryCondition binary => Binary(
            binary.Operator == SymbolicConditionOperator.And ? SmtBinaryOperator.And : SmtBinaryOperator.Or,
            Encode(binary.Left),
            Encode(binary.Right)),
        _ => null
    };
    private static SmtFormula? Encode(SymbolicFact fact) {
        if (fact.Confidence != SymbolicFactConfidence.Exact) return null;
        var formula = Encode(fact.Atom);
        return fact.Polarity ? formula : Unary(SmtUnaryOperator.Not, formula);
    }
    private static SmtFormula? Encode(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => Kind(Encode(truth.Condition), SmtValueKind.Bool),
        SymbolicRelationAtom relation => EncodeRelation(relation),
        SymbolicStringPredicateAtom predicate => EncodeStringPredicate(predicate),
        SymbolicBoundsAtom bounds => EncodeBounds(bounds),
        SymbolicTypeTestAtom typeTest => Kind(Encode(typeTest.Value), SmtValueKind.Reference) is { } value
            ? new SmtRuntimeTypeTestFormula(value, typeTest.TypeKey)
            : null,
        SymbolicExceptionPreconditionAtom precondition => Encode(precondition.Trigger),
        _ => null
    };
    private static SmtFormula? Encode(SymbolicTerm term) => term switch {
        SymbolicBooleanConstantTerm constant => new SmtBooleanConstant(constant.Value),
        SymbolicIntegerConstantTerm constant => new SmtIntegerConstant(constant.Value),
        SymbolicStringConstantTerm constant => new SmtStringConstant(constant.Value),
        SymbolicNullTerm => new SmtNullConstant(),
        SymbolicVariableTerm variable => new SmtVariable(variable.Name, variable.Kind),
        SymbolicMemberTerm member => EncodeProjection(member.Receiver, receiver => member with { Receiver = receiver }) ??
                                     EncodeReferenceProjection(member.Receiver, "." + member.MemberName, member.Kind),
        SymbolicElementTerm element => EncodeElement(element),
        SymbolicMultiElementTerm element => EncodeMultiElement(element),
        SymbolicFromEndIndexTerm => null,
        SymbolicStringContentTerm content =>
            EncodeProjection(content.Reference, reference => content with { Reference = reference }) ??
            EncodeStringContent(content.Reference),
        SymbolicStringConcatTerm concat => EncodeStringConcat(concat),
        SymbolicStringSliceTerm slice => EncodeStringSlice(slice),
        SymbolicNullableHasValueTerm nullable => new SmtVariable(nullable.NullableName + ".HasValue", SmtValueKind.Bool),
        SymbolicNullableValueTerm nullable => new SmtVariable(nullable.NullableName + ".Value", nullable.Kind),
        SymbolicLengthTerm length => EncodeLength(length.Value),
        SymbolicArrayDimensionLengthTerm length => EncodeArrayLength(length),
        SymbolicCountTerm count => EncodeProjection(count.Value, value => count with { Value = value }) ??
                                   EncodeReferenceProjection(count.Value, ".Count", SmtValueKind.Int),
        SymbolicBinaryTerm binary => EncodeIntegerBinary(binary),
        SymbolicConditionalTerm conditional => EncodeConditional(conditional),
        SymbolicNumericConversionTerm conversion =>
            new SmtVariable(SymbolicState.CreateProofTermKey(conversion), SmtValueKind.Int),
        _ => null
    };
    private static SmtFormula? EncodeElement(SymbolicElementTerm element) {
        var projected = EncodeProjection(element.Receiver, receiver => element with { Receiver = receiver });
        if (projected != null) return projected;
        var receiver = Kind(Encode(element.Receiver), SmtValueKind.Reference);
        return receiver != null && TryEncodeIndex(element.Index, out var index)
            ? new SmtVariable(ReferenceName(receiver) + "[" + index + "]", element.Kind)
            : null;
    }
    private static SmtFormula? EncodeMultiElement(SymbolicMultiElementTerm element) {
        var projected = EncodeProjection(element.Receiver, receiver => element with { Receiver = receiver });
        if (projected != null) return projected;
        var receiver = Kind(Encode(element.Receiver), SmtValueKind.Reference);
        if (receiver == null || element.Indices.IsDefaultOrEmpty) return null;
        var indices = new string[element.Indices.Length];
        for (var i = 0; i < indices.Length; i++)
            if (!TryEncodeIndex(element.Indices[i], out indices[i]))
                return null;
        return new SmtVariable(ReferenceName(receiver) + "[" + string.Join(",", indices) + "]", element.Kind);
    }
    private static SmtFormula? EncodeStringContent(SymbolicTerm reference) {
        var formula = Encode(reference);
        return formula != null &&
               SymbolicFactFactory.TryCreateReferenceStringContentFormula(formula, out var content)
            ? content
            : null;
    }
    private static SmtFormula? EncodeStringConcat(SymbolicStringConcatTerm concat) {
        var left = Kind(Encode(concat.Left), SmtValueKind.String);
        var right = Kind(Encode(concat.Right), SmtValueKind.String);
        return left != null && right != null ? new SmtStringConcatTerm(left, right) : null;
    }
    private static SmtFormula? EncodeStringSlice(SymbolicStringSliceTerm slice) {
        var value = Kind(Encode(slice.Value), SmtValueKind.String);
        var offset = Kind(Encode(slice.Offset), SmtValueKind.Int);
        var length = Kind(Encode(slice.Length), SmtValueKind.Int);
        return value != null && offset != null && length != null
            ? new SmtStringSubstringTerm(value, offset, length)
            : null;
    }
    private static SmtFormula? EncodeLength(SymbolicTerm value) {
        if (value is SymbolicStringConcatTerm concat) {
            var left = EncodeLength(concat.Left);
            var right = EncodeLength(concat.Right);
            if (left != null && right != null)
                return new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, left, right);
        }
        if (value.Kind == SmtValueKind.String)
            return Kind(Encode(value), SmtValueKind.String) is { } text ? new SmtStringLengthTerm(text) : null;
        var reference = Encode(value);
        return reference != null &&
               SymbolicFactFactory.TryCreateReferenceBuiltInLengthFormula(reference, out var length)
            ? length
            : null;
    }
    private static SmtFormula? EncodeArrayLength(SymbolicArrayDimensionLengthTerm length) {
        var value = Encode(length.Value);
        return value != null &&
               SymbolicFactFactory.TryCreateReferenceArrayDimensionLengthFormula(value, length.Dimension, out var formula)
            ? formula
            : null;
    }
    private static SmtFormula? EncodeIntegerBinary(SymbolicBinaryTerm binary) {
        var left = Kind(Encode(binary.Left), SmtValueKind.Int);
        var right = Kind(Encode(binary.Right), SmtValueKind.Int);
        if (left == null || right == null) return null;
        var op = SymbolicOperatorLowerer.GetSmtIntegerBinaryOperator(binary.Operator);
        return binary.MayOverflow
            ? new SmtOpaqueIntegerBinaryTerm(op, left, right)
            : new SmtIntegerBinaryTerm(op, left, right);
    }
    private static SmtFormula? EncodeConditional(SymbolicConditionalTerm conditional) {
        if (conditional.WhenTrue.Kind != conditional.WhenFalse.Kind) return null;
        var condition = Encode(conditional.Condition);
        var whenTrue = Encode(conditional.WhenTrue);
        var whenFalse = Encode(conditional.WhenFalse);
        return condition != null && whenTrue != null && whenFalse != null
            ? new SmtConditionalFormula(condition, whenTrue, whenFalse, whenTrue.Kind)
            : null;
    }
    private static SmtFormula? EncodeProjection(
        SymbolicTerm receiver,
        Func<SymbolicTerm, SymbolicTerm> project) {
        if (receiver is not SymbolicConditionalTerm conditional) return null;
        var condition = Encode(conditional.Condition);
        var whenTrue = Encode(project(conditional.WhenTrue));
        var whenFalse = Encode(project(conditional.WhenFalse));
        return condition != null && whenTrue != null && whenFalse != null && whenTrue.Kind == whenFalse.Kind
            ? new SmtConditionalFormula(condition, whenTrue, whenFalse, whenTrue.Kind)
            : null;
    }
    private static SmtFormula? EncodeReferenceProjection(SymbolicTerm receiver, string suffix, SmtValueKind kind) =>
        Kind(Encode(receiver), SmtValueKind.Reference) is { } reference
            ? new SmtVariable(ReferenceName(reference) + suffix, kind)
            : null;
    private static SmtFormula? EncodeRelation(SymbolicRelationAtom relation) {
        var left = Encode(relation.Left);
        var right = Encode(relation.Right);
        return left != null && right != null && CanCompare(left, right)
            ? new SmtBinaryFormula(ToSmtOperator(relation.Operator), left, right)
            : null;
    }
    private static SmtFormula? EncodeStringPredicate(SymbolicStringPredicateAtom predicate) {
        var value = Kind(Encode(predicate.Value), SmtValueKind.String);
        if (value == null) return null;
        if (predicate.Predicate == SymbolicStringPredicateKind.RegexMatch)
            return predicate.Argument is SymbolicStringConstantTerm pattern
                ? new SmtRegexMatchFormula(value, pattern.Value, predicate.RegexOptions)
                : null;
        var argument = Kind(Encode(predicate.Argument), SmtValueKind.String);
        return argument == null
            ? null
            : predicate.Predicate switch {
                SymbolicStringPredicateKind.Contains => new SmtStringContainsFormula(value, argument),
                SymbolicStringPredicateKind.StartsWith => new SmtStringStartsWithFormula(value, argument),
                SymbolicStringPredicateKind.EndsWith => new SmtStringEndsWithFormula(value, argument),
                _ => null
            };
    }
    private static SmtFormula? EncodeBounds(SymbolicBoundsAtom bounds) {
        var index = Kind(Encode(bounds.Index), SmtValueKind.Int);
        var length = Kind(Encode(bounds.Length), SmtValueKind.Int);
        if (index == null || length == null) return null;
        SmtFormula? lower = bounds.IncludeLowerBound
            ? new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, index, new SmtIntegerConstant(0))
            : null;
        SmtFormula? upper = bounds.IncludeUpperBound
            ? new SmtBinaryFormula(SmtBinaryOperator.LessThan, index, length)
            : null;
        return lower != null && upper != null
            ? new SmtBinaryFormula(SmtBinaryOperator.And, lower, upper)
            : lower ?? upper;
    }
    private static bool TryEncodeIndex(SymbolicTerm index, out string text) {
        var fromEnd = index is SymbolicFromEndIndexTerm;
        var value = fromEnd ? ((SymbolicFromEndIndexTerm)index).Value : index;
        var formula = Kind(Encode(value), SmtValueKind.Int);
        text = formula == null
            ? string.Empty
            : (fromEnd ? "^" : string.Empty) + (formula switch {
                SmtIntegerConstant constant => constant.Value.ToString(CultureInfo.InvariantCulture),
                SmtVariable variable => variable.Name,
                _ => formula.ToString()
            });
        return text.Length != 0;
    }
    private static SmtFormula? Unary(SmtUnaryOperator op, SmtFormula? value) =>
        value == null ? null : new SmtUnaryFormula(op, value);
    private static SmtFormula? Binary(SmtBinaryOperator op, SmtFormula? left, SmtFormula? right) =>
        left == null || right == null ? null : new SmtBinaryFormula(op, left, right);
    private static SmtFormula? Kind(SmtFormula? formula, SmtValueKind kind) =>
        formula?.Kind == kind ? formula : null;
    private static bool CanCompare(SmtFormula left, SmtFormula right) =>
        left.Kind == right.Kind ||
        left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
        right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
    private static SmtBinaryOperator ToSmtOperator(SymbolicRelationOperator op) => op switch {
        SymbolicRelationOperator.Equal => SmtBinaryOperator.Equal,
        SymbolicRelationOperator.NotEqual => SmtBinaryOperator.NotEqual,
        SymbolicRelationOperator.LessThan => SmtBinaryOperator.LessThan,
        SymbolicRelationOperator.LessThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
        SymbolicRelationOperator.GreaterThan => SmtBinaryOperator.GreaterThan,
        SymbolicRelationOperator.GreaterThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };
    private static string ReferenceName(SmtFormula formula) =>
        SymbolicFactFactory.GetReferenceFormulaName(formula);
    private static bool Try(SmtFormula? encoded, out SmtFormula formula) {
        formula = encoded!;
        return encoded != null;
    }
}
