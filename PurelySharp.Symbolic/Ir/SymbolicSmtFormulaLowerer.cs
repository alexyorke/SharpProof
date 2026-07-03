using Microsoft.CodeAnalysis;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static class SymbolicSmtFormulaLowerer
    {
        internal static bool TryLowerEqualityFact(
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey,
            out SymbolicFact fact)
        {
            fact = null!;
            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equality ||
                !TryLowerTerm(equality.Left, sourceNode, provenance, evidenceKey, out var left) ||
                !TryLowerTerm(equality.Right, sourceNode, provenance, evidenceKey, out var right) ||
                !CanCompareTerms(left, right))
            {
                return false;
            }

            fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    left,
                    right),
                sourceNode,
                provenance,
                evidenceKey: evidenceKey);
            return true;
        }

        internal static bool TryLowerCondition(
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey,
            out SymbolicCondition condition)
        {
            return TryLowerConditionCore(formula, sourceNode, provenance, evidenceKey, out condition);
        }

        internal static bool TryLowerTerm(SmtFormula formula, out SymbolicTerm term)
        {
            return TryLowerTerm(formula, sourceNode: null, provenance: null, evidenceKey: null, out term);
        }

        private static bool TryLowerTerm(
            SmtFormula formula,
            SyntaxNode? sourceNode,
            string? provenance,
            string? evidenceKey,
            out SymbolicTerm term)
        {
            switch (formula)
            {
                case SmtBooleanConstant boolean:
                    term = new SymbolicBooleanConstantTerm(boolean.Value);
                    return true;
                case SmtIntegerConstant integer:
                    term = new SymbolicIntegerConstantTerm(integer.Value);
                    return true;
                case SmtStringConstant text:
                    term = new SymbolicStringConstantTerm(text.Value);
                    return true;
                case SmtNullConstant:
                    term = new SymbolicNullTerm();
                    return true;
                case SmtVariable variable:
                    term = CreateSymbolicVariableTerm(variable);
                    return true;
                case SmtStringLengthTerm stringLength:
                    if (TryLowerTerm(stringLength.Value, sourceNode, provenance, evidenceKey, out var stringLengthValue))
                    {
                        term = new SymbolicLengthTerm(stringLengthValue);
                        return true;
                    }

                    break;
                case SmtStringConcatTerm concat:
                    if (TryLowerTerm(concat.Left, sourceNode, provenance, evidenceKey, out var leftString) &&
                        TryLowerTerm(concat.Right, sourceNode, provenance, evidenceKey, out var rightString) &&
                        leftString.Kind == SmtValueKind.String &&
                        rightString.Kind == SmtValueKind.String)
                    {
                        term = new SymbolicStringConcatTerm(leftString, rightString);
                        return true;
                    }

                    break;
                case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } negate:
                    if (TryLowerTerm(negate.Operand, sourceNode, provenance, evidenceKey, out var operand) &&
                        operand.Kind == SmtValueKind.Int)
                    {
                        term = new SymbolicBinaryTerm(
                            SymbolicBinaryTermOperator.Subtract,
                            new SymbolicIntegerConstantTerm(0),
                            operand);
                        return true;
                    }

                    break;
                case SmtIntegerBinaryTerm binary:
                    if (TryGetSymbolicBinaryTermOperator(binary.Operator, out var symbolicOperator) &&
                        TryLowerTerm(binary.Left, sourceNode, provenance, evidenceKey, out var left) &&
                        TryLowerTerm(binary.Right, sourceNode, provenance, evidenceKey, out var right) &&
                        left.Kind == SmtValueKind.Int &&
                        right.Kind == SmtValueKind.Int)
                    {
                        term = new SymbolicBinaryTerm(symbolicOperator, left, right);
                        return true;
                    }

                    break;
                case SmtConditionalFormula conditional:
                    if (sourceNode != null &&
                        provenance != null &&
                        TryLowerConditionCore(conditional.Condition, sourceNode, provenance, evidenceKey, out var condition) &&
                        TryLowerTerm(conditional.WhenTrue, sourceNode, provenance, evidenceKey, out var whenTrue) &&
                        TryLowerTerm(conditional.WhenFalse, sourceNode, provenance, evidenceKey, out var whenFalse) &&
                        whenTrue.Kind == whenFalse.Kind &&
                        whenTrue.Kind == conditional.ResultKind)
                    {
                        term = new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
                        return true;
                    }

                    break;
                default:
                    break;
            }

            term = null!;
            return false;
        }

        private static SymbolicTerm CreateSymbolicVariableTerm(SmtVariable variable)
        {
            const string stringSuffix = ".String";
            const string lengthSuffix = ".Length";
            const string countSuffix = ".Count";

            if (variable.Kind == SmtValueKind.String &&
                variable.Name.EndsWith(stringSuffix, StringComparison.Ordinal) &&
                variable.Name.Length > stringSuffix.Length)
            {
                return new SymbolicStringContentTerm(new SymbolicVariableTerm(
                    variable.Name.Substring(0, variable.Name.Length - stringSuffix.Length),
                    SmtValueKind.Reference));
            }

            if (variable.Kind == SmtValueKind.Int &&
                variable.Name.EndsWith(lengthSuffix, StringComparison.Ordinal) &&
                variable.Name.Length > lengthSuffix.Length)
            {
                return new SymbolicLengthTerm(new SymbolicVariableTerm(
                    variable.Name.Substring(0, variable.Name.Length - lengthSuffix.Length),
                    SmtValueKind.Reference));
            }

            if (variable.Kind == SmtValueKind.Int &&
                variable.Name.EndsWith(countSuffix, StringComparison.Ordinal) &&
                variable.Name.Length > countSuffix.Length)
            {
                return new SymbolicCountTerm(new SymbolicVariableTerm(
                    variable.Name.Substring(0, variable.Name.Length - countSuffix.Length),
                    SmtValueKind.Reference));
            }

            return new SymbolicVariableTerm(variable.Name, variable.Kind);
        }

        private static bool TryLowerConditionCore(
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string? evidenceKey,
            out SymbolicCondition condition)
        {
            switch (formula)
            {
                case SmtBooleanConstant boolean:
                    condition = new SymbolicConstantCondition(boolean.Value);
                    return true;
                case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } not:
                    if (TryLowerConditionCore(not.Operand, sourceNode, provenance, evidenceKey, out var operand))
                    {
                        condition = new SymbolicNotCondition(operand);
                        return true;
                    }

                    break;
                case SmtBinaryFormula { Operator: SmtBinaryOperator.And } and:
                    if (TryLowerConditionCore(and.Left, sourceNode, provenance, evidenceKey, out var leftAnd) &&
                        TryLowerConditionCore(and.Right, sourceNode, provenance, evidenceKey, out var rightAnd))
                    {
                        condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, leftAnd, rightAnd);
                        return true;
                    }

                    break;
                case SmtBinaryFormula { Operator: SmtBinaryOperator.Or } or:
                    if (TryLowerConditionCore(or.Left, sourceNode, provenance, evidenceKey, out var leftOr) &&
                        TryLowerConditionCore(or.Right, sourceNode, provenance, evidenceKey, out var rightOr))
                    {
                        condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, leftOr, rightOr);
                        return true;
                    }

                    break;
                case SmtBinaryFormula relation:
                    if (TryLowerBooleanRelationCondition(
                            relation,
                            sourceNode,
                            provenance,
                            evidenceKey,
                            out condition))
                    {
                        return true;
                    }

                    if (TryGetSymbolicRelationOperator(relation.Operator, out var relationOperator) &&
                        TryLowerTerm(relation.Left, sourceNode, provenance, evidenceKey, out var left) &&
                        TryLowerTerm(relation.Right, sourceNode, provenance, evidenceKey, out var right) &&
                        CanCompareTerms(left, right))
                    {
                        condition = new SymbolicFactCondition(SymbolicFact.Exact(
                            new SymbolicRelationAtom(relationOperator, left, right),
                            sourceNode,
                            provenance,
                            evidenceKey: evidenceKey));
                        return true;
                    }

                    break;
                case SmtStringContainsFormula contains:
                    if (TryLowerStringPredicate(
                            SymbolicStringPredicateKind.Contains,
                            contains.Value,
                            contains.Search,
                            sourceNode,
                            provenance,
                            evidenceKey,
                            out condition))
                    {
                        return true;
                    }

                    break;
                case SmtStringStartsWithFormula startsWith:
                    if (TryLowerStringPredicate(
                            SymbolicStringPredicateKind.StartsWith,
                            startsWith.Value,
                            startsWith.Prefix,
                            sourceNode,
                            provenance,
                            evidenceKey,
                            out condition))
                    {
                        return true;
                    }

                    break;
                case SmtStringEndsWithFormula endsWith:
                    if (TryLowerStringPredicate(
                            SymbolicStringPredicateKind.EndsWith,
                            endsWith.Value,
                            endsWith.Suffix,
                            sourceNode,
                            provenance,
                            evidenceKey,
                            out condition))
                    {
                        return true;
                    }

                    break;
                case SmtRegexMatchFormula regex:
                    if (TryLowerTerm(regex.Value, sourceNode, provenance, evidenceKey, out var regexValue) &&
                        regexValue.Kind == SmtValueKind.String)
                    {
                        condition = new SymbolicFactCondition(SymbolicFact.Exact(
                            new SymbolicStringPredicateAtom(
                                SymbolicStringPredicateKind.RegexMatch,
                                regexValue,
                                new SymbolicStringConstantTerm(regex.Pattern),
                                regex.Options),
                            sourceNode,
                            provenance,
                            evidenceKey: evidenceKey));
                        return true;
                    }

                    break;
                case SmtRuntimeTypeTestFormula typeTest:
                    if (TryLowerTerm(typeTest.Value, sourceNode, provenance, evidenceKey, out var value) &&
                        value.Kind == SmtValueKind.Reference)
                    {
                        condition = new SymbolicFactCondition(SymbolicFact.Exact(
                            new SymbolicTypeTestAtom(value, typeTest.TypeKey),
                            sourceNode,
                            provenance,
                            evidenceKey: evidenceKey));
                        return true;
                    }

                    break;
                default:
                    if (TryLowerTerm(formula, sourceNode, provenance, evidenceKey, out var truthTerm) &&
                        truthTerm.Kind == SmtValueKind.Bool)
                    {
                        condition = new SymbolicFactCondition(SymbolicFact.Exact(
                            new SymbolicTruthAtom(truthTerm),
                            sourceNode,
                            provenance,
                            evidenceKey: evidenceKey));
                        return true;
                    }

                    break;
            }

            condition = null!;
            return false;
        }

        private static bool TryLowerBooleanRelationCondition(
            SmtBinaryFormula relation,
            SyntaxNode sourceNode,
            string provenance,
            string? evidenceKey,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (relation.Operator is not SmtBinaryOperator.Equal and not SmtBinaryOperator.NotEqual ||
                relation.Left.Kind != SmtValueKind.Bool ||
                relation.Right.Kind != SmtValueKind.Bool ||
                !TryLowerConditionCore(relation.Left, sourceNode, provenance, evidenceKey, out var left) ||
                !TryLowerConditionCore(relation.Right, sourceNode, provenance, evidenceKey, out var right))
            {
                return false;
            }

            var leftAndRight = new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
            var notLeftAndNotRight = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(left),
                new SymbolicNotCondition(right));
            var equality = new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                leftAndRight,
                notLeftAndNotRight);

            if (relation.Operator == SmtBinaryOperator.Equal)
            {
                condition = equality;
                return true;
            }

            condition = new SymbolicNotCondition(equality);
            return true;
        }

        private static bool TryLowerStringPredicate(
            SymbolicStringPredicateKind predicate,
            SmtFormula valueFormula,
            SmtFormula argumentFormula,
            SyntaxNode sourceNode,
            string provenance,
            string? evidenceKey,
            out SymbolicCondition condition)
        {
            if (TryLowerTerm(valueFormula, sourceNode, provenance, evidenceKey, out var value) &&
                TryLowerTerm(argumentFormula, sourceNode, provenance, evidenceKey, out var argument) &&
                value.Kind == SmtValueKind.String &&
                argument.Kind == SmtValueKind.String)
            {
                condition = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicStringPredicateAtom(predicate, value, argument),
                    sourceNode,
                    provenance,
                    evidenceKey: evidenceKey));
                return true;
            }

            condition = null!;
            return false;
        }

        private static bool TryGetSymbolicBinaryTermOperator(
            SmtIntegerBinaryOperator smtOperator,
            out SymbolicBinaryTermOperator symbolicOperator)
        {
            switch (smtOperator)
            {
                case SmtIntegerBinaryOperator.Add:
                    symbolicOperator = SymbolicBinaryTermOperator.Add;
                    return true;
                case SmtIntegerBinaryOperator.Subtract:
                    symbolicOperator = SymbolicBinaryTermOperator.Subtract;
                    return true;
                case SmtIntegerBinaryOperator.Multiply:
                    symbolicOperator = SymbolicBinaryTermOperator.Multiply;
                    return true;
                case SmtIntegerBinaryOperator.Divide:
                    symbolicOperator = SymbolicBinaryTermOperator.Divide;
                    return true;
                case SmtIntegerBinaryOperator.Remainder:
                    symbolicOperator = SymbolicBinaryTermOperator.Remainder;
                    return true;
                default:
                    symbolicOperator = default;
                    return false;
            }
        }

        private static bool TryGetSymbolicRelationOperator(
            SmtBinaryOperator smtOperator,
            out SymbolicRelationOperator symbolicOperator)
        {
            switch (smtOperator)
            {
                case SmtBinaryOperator.Equal:
                    symbolicOperator = SymbolicRelationOperator.Equal;
                    return true;
                case SmtBinaryOperator.NotEqual:
                    symbolicOperator = SymbolicRelationOperator.NotEqual;
                    return true;
                case SmtBinaryOperator.LessThan:
                    symbolicOperator = SymbolicRelationOperator.LessThan;
                    return true;
                case SmtBinaryOperator.LessThanOrEqual:
                    symbolicOperator = SymbolicRelationOperator.LessThanOrEqual;
                    return true;
                case SmtBinaryOperator.GreaterThan:
                    symbolicOperator = SymbolicRelationOperator.GreaterThan;
                    return true;
                case SmtBinaryOperator.GreaterThanOrEqual:
                    symbolicOperator = SymbolicRelationOperator.GreaterThanOrEqual;
                    return true;
                default:
                    symbolicOperator = default;
                    return false;
            }
        }

        private static bool CanCompareTerms(SymbolicTerm left, SymbolicTerm right)
        {
            return left.Kind == right.Kind ||
                left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference ||
                right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference;
        }
    }
}
