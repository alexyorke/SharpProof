using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static class SymbolicIrFormulaEncoder
    {
        internal static bool TryEncode(SymbolicCondition condition, out SmtFormula formula)
        {
            switch (condition)
            {
                case SymbolicConstantCondition constant:
                    formula = new SmtBooleanConstant(constant.Value);
                    return true;
                case SymbolicFactCondition factCondition:
                    return TryEncode(factCondition.Fact, out formula);
                case SymbolicNotCondition notCondition:
                    if (TryEncode(notCondition.Operand, out var operand))
                    {
                        formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                        return true;
                    }

                    break;
                case SymbolicBinaryCondition binaryCondition:
                    if (TryEncode(binaryCondition.Left, out var left) &&
                        TryEncode(binaryCondition.Right, out var right))
                    {
                        formula = new SmtBinaryFormula(
                            binaryCondition.Operator == SymbolicConditionOperator.And
                                ? SmtBinaryOperator.And
                                : SmtBinaryOperator.Or,
                            left,
                            right);
                        return true;
                    }

                    break;
            }

            formula = null!;
            return false;
        }

        internal static bool TryEncode(SymbolicFact fact, out SmtFormula formula)
        {
            if (fact.Confidence == SymbolicFactConfidence.Unsupported ||
                !TryEncode(fact.Atom, out formula))
            {
                formula = null!;
                return false;
            }

            if (!fact.Polarity)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);
            }

            return true;
        }

        internal static bool TryEncode(SymbolicAtom atom, out SmtFormula formula)
        {
            switch (atom)
            {
                case SymbolicTruthAtom truth:
                    return TryEncodeBooleanTerm(truth.Condition, out formula);
                case SymbolicRelationAtom relation:
                    return TryEncodeRelation(relation, out formula);
                case SymbolicStringPredicateAtom stringPredicate:
                    return TryEncodeStringPredicate(stringPredicate, out formula);
                case SymbolicBoundsAtom bounds:
                    return TryEncodeBounds(bounds, out formula);
                case SymbolicTypeTestAtom typeTest:
                    if (TryEncodeTerm(typeTest.Value, out var value) &&
                        value.Kind == SmtValueKind.Reference)
                    {
                        formula = new SmtRuntimeTypeTestFormula(value, typeTest.TypeKey);
                        return true;
                    }

                    break;
                case SymbolicExceptionPreconditionAtom exceptionPrecondition:
                    return TryEncode(exceptionPrecondition.Trigger, out formula);
            }

            formula = null!;
            return false;
        }

        internal static bool TryEncodeTerm(SymbolicTerm term, out SmtFormula formula)
        {
            switch (term)
            {
                case SymbolicBooleanConstantTerm constant:
                    formula = new SmtBooleanConstant(constant.Value);
                    return true;
                case SymbolicIntegerConstantTerm constant:
                    formula = new SmtIntegerConstant(constant.Value);
                    return true;
                case SymbolicStringConstantTerm constant:
                    formula = new SmtStringConstant(constant.Value);
                    return true;
                case SymbolicNullTerm:
                    formula = new SmtNullConstant();
                    return true;
                case SymbolicVariableTerm variable:
                    formula = new SmtVariable(variable.Name, variable.Kind);
                    return true;
                case SymbolicMemberTerm member:
                    if (TryEncodeTerm(member.Receiver, out var receiver) &&
                        receiver.Kind == SmtValueKind.Reference)
                    {
                        formula = new SmtVariable(
                            GetReferenceFormulaName(receiver) + "." + member.MemberName,
                            member.Kind);
                        return true;
                    }

                    break;
                case SymbolicStringContentTerm stringContent:
                    if (TryEncodeTerm(stringContent.Reference, out var reference) &&
                        SymbolicFactFactory.TryCreateReferenceStringContentFormula(reference, out var stringFormula))
                    {
                        formula = stringFormula;
                        return true;
                    }

                    break;
                case SymbolicStringConcatTerm concat:
                    if (TryEncodeTerm(concat.Left, out var leftString) &&
                        TryEncodeTerm(concat.Right, out var rightString) &&
                        leftString.Kind == SmtValueKind.String &&
                        rightString.Kind == SmtValueKind.String)
                    {
                        formula = new SmtStringConcatTerm(leftString, rightString);
                        return true;
                    }

                    break;
                case SymbolicNullableHasValueTerm nullableHasValue:
                    formula = new SmtVariable(nullableHasValue.NullableName + ".HasValue", SmtValueKind.Bool);
                    return true;
                case SymbolicLengthTerm length:
                    if (TryEncodeTerm(length.Value, out var value))
                    {
                        if (value.Kind == SmtValueKind.String)
                        {
                            formula = new SmtStringLengthTerm(value);
                            return true;
                        }

                        if (SymbolicFactFactory.TryCreateReferenceBuiltInLengthFormula(value, out var lengthFormula))
                        {
                            formula = lengthFormula;
                            return true;
                        }
                    }

                    break;
                case SymbolicCountTerm count:
                    if (TryEncodeTerm(count.Value, out var countReference) &&
                        countReference.Kind == SmtValueKind.Reference)
                    {
                        formula = new SmtVariable(GetReferenceFormulaName(countReference) + ".Count", SmtValueKind.Int);
                        return true;
                    }

                    break;
                case SymbolicBinaryTerm binary:
                    if (TryEncodeTerm(binary.Left, out var left) &&
                        TryEncodeTerm(binary.Right, out var right) &&
                        left.Kind == SmtValueKind.Int &&
                        right.Kind == SmtValueKind.Int)
                    {
                        formula = new SmtIntegerBinaryTerm(ToSmtOperator(binary.Operator), left, right);
                        return true;
                    }

                    break;
                case SymbolicConditionalTerm conditional:
                    if (conditional.WhenTrue.Kind == conditional.WhenFalse.Kind &&
                        TryEncode(conditional.Condition, out var conditionFormula) &&
                        TryEncodeTerm(conditional.WhenTrue, out var whenTrue) &&
                        TryEncodeTerm(conditional.WhenFalse, out var whenFalse))
                    {
                        formula = new SmtConditionalFormula(conditionFormula, whenTrue, whenFalse, whenTrue.Kind);
                        return true;
                    }

                    break;
            }

            formula = null!;
            return false;
        }

        private static bool TryEncodeBooleanTerm(SymbolicTerm term, out SmtFormula formula)
        {
            if (term.Kind == SmtValueKind.Bool &&
                TryEncodeTerm(term, out formula))
            {
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryEncodeRelation(SymbolicRelationAtom relation, out SmtFormula formula)
        {
            if (!TryEncodeTerm(relation.Left, out var left) ||
                !TryEncodeTerm(relation.Right, out var right) ||
                !CanCompareSmtValues(left, right))
            {
                formula = null!;
                return false;
            }

            formula = new SmtBinaryFormula(ToSmtOperator(relation.Operator), left, right);
            return true;
        }

        private static bool TryEncodeStringPredicate(SymbolicStringPredicateAtom atom, out SmtFormula formula)
        {
            if (!TryEncodeTerm(atom.Value, out var value) ||
                value.Kind != SmtValueKind.String)
            {
                formula = null!;
                return false;
            }

            if (atom.Predicate == SymbolicStringPredicateKind.RegexMatch)
            {
                if (atom.Argument is not SymbolicStringConstantTerm pattern)
                {
                    formula = null!;
                    return false;
                }

                formula = new SmtRegexMatchFormula(value, pattern.Value, atom.RegexOptions);
                return true;
            }

            if (!TryEncodeTerm(atom.Argument, out var argument) ||
                argument.Kind != SmtValueKind.String)
            {
                formula = null!;
                return false;
            }

            formula = atom.Predicate switch
            {
                SymbolicStringPredicateKind.Contains => new SmtStringContainsFormula(value, argument),
                SymbolicStringPredicateKind.StartsWith => new SmtStringStartsWithFormula(value, argument),
                SymbolicStringPredicateKind.EndsWith => new SmtStringEndsWithFormula(value, argument),
                _ => null!,
            };

            return formula != null;
        }

        private static bool TryEncodeBounds(SymbolicBoundsAtom bounds, out SmtFormula formula)
        {
            if (!TryEncodeTerm(bounds.Index, out var index) ||
                !TryEncodeTerm(bounds.Length, out var length) ||
                index.Kind != SmtValueKind.Int ||
                length.Kind != SmtValueKind.Int)
            {
                formula = null!;
                return false;
            }

            SmtFormula? lower = bounds.IncludeLowerBound
                ? new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, index, new SmtIntegerConstant(0))
                : null;
            SmtFormula? upper = bounds.IncludeUpperBound
                ? new SmtBinaryFormula(SmtBinaryOperator.LessThan, index, length)
                : null;

            formula = lower != null && upper != null
                ? new SmtBinaryFormula(SmtBinaryOperator.And, lower, upper)
                : lower ?? upper!;
            return formula != null;
        }

        private static bool CanCompareSmtValues(SmtFormula left, SmtFormula right)
        {
            return left.Kind == right.Kind ||
                left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
                right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
        }

        private static SmtBinaryOperator ToSmtOperator(SymbolicRelationOperator op)
        {
            return op switch
            {
                SymbolicRelationOperator.Equal => SmtBinaryOperator.Equal,
                SymbolicRelationOperator.NotEqual => SmtBinaryOperator.NotEqual,
                SymbolicRelationOperator.LessThan => SmtBinaryOperator.LessThan,
                SymbolicRelationOperator.LessThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
                SymbolicRelationOperator.GreaterThan => SmtBinaryOperator.GreaterThan,
                SymbolicRelationOperator.GreaterThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
            };
        }

        private static SmtIntegerBinaryOperator ToSmtOperator(SymbolicBinaryTermOperator op)
        {
            return op switch
            {
                SymbolicBinaryTermOperator.Add => SmtIntegerBinaryOperator.Add,
                SymbolicBinaryTermOperator.Subtract => SmtIntegerBinaryOperator.Subtract,
                SymbolicBinaryTermOperator.Multiply => SmtIntegerBinaryOperator.Multiply,
                SymbolicBinaryTermOperator.Divide => SmtIntegerBinaryOperator.Divide,
                SymbolicBinaryTermOperator.Remainder => SmtIntegerBinaryOperator.Remainder,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
            };
        }

        private static string GetReferenceFormulaName(SmtFormula formula)
        {
            return formula is SmtVariable variable
                ? variable.Name
                : formula.ToString() ?? string.Empty;
        }
    }
}
