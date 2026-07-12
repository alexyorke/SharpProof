namespace SearchLib.Smt;

internal static class SmtFormulaTraversal
{
    internal static IEnumerable<SmtFormula> Enumerate(SmtFormula root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var stack = new Stack<SmtFormula>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            PushChildrenInReverse(current, stack);
        }
    }

    internal static SmtFormula RewriteBottomUp(
        SmtFormula root,
        Func<SmtFormula, SmtFormula> rewrite,
        out bool changed)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (rewrite == null) throw new ArgumentNullException(nameof(rewrite));

        changed = false;
        var frames = new Stack<TraversalFrame>();
        var results = new Stack<SmtFormula>();
        frames.Push(new TraversalFrame(root, false));

        while (frames.Count > 0)
        {
            var frame = frames.Pop();
            if (!frame.Visited)
            {
                frames.Push(new TraversalFrame(frame.Formula, true));
                PushChildrenInReverse(frame.Formula, frames);
                continue;
            }

            var childCount = GetChildCount(frame.Formula);
            var children = childCount == 0 ? Array.Empty<SmtFormula>() : new SmtFormula[childCount];
            for (var index = childCount - 1; index >= 0; index--) children[index] = results.Pop();

            var rebuilt = Rebuild(frame.Formula, children);
            var rewritten = rewrite(rebuilt) ?? throw new InvalidOperationException("Formula rewrite returned null.");
            if (!ReferenceEquals(frame.Formula, rewritten)) changed = true;
            results.Push(rewritten);
        }

        return results.Pop();
    }

    internal static bool AreStructurallyEqual(SmtFormula left, SmtFormula right)
    {
        var pairs = new Stack<FormulaPair>();
        pairs.Push(new FormulaPair(left, right));
        while (pairs.Count > 0)
        {
            var pair = pairs.Pop();
            if (ReferenceEquals(pair.Left, pair.Right)) continue;
            if (pair.Left.GetType() != pair.Right.GetType() || pair.Left.Kind != pair.Right.Kind) return false;

            switch (pair.Left)
            {
                case SmtBooleanConstant leftValue when pair.Right is SmtBooleanConstant rightValue:
                    if (leftValue.Value != rightValue.Value) return false;
                    break;
                case SmtIntegerConstant leftValue when pair.Right is SmtIntegerConstant rightValue:
                    if (leftValue.Value != rightValue.Value) return false;
                    break;
                case SmtStringConstant leftValue when pair.Right is SmtStringConstant rightValue:
                    if (!string.Equals(leftValue.Value, rightValue.Value, StringComparison.Ordinal)) return false;
                    break;
                case SmtNullConstant:
                    break;
                case SmtVariable leftValue when pair.Right is SmtVariable rightValue:
                    if (!string.Equals(leftValue.Name, rightValue.Name, StringComparison.Ordinal)) return false;
                    break;
                case SmtUnaryFormula leftValue when pair.Right is SmtUnaryFormula rightValue:
                    if (leftValue.Operator != rightValue.Operator) return false;
                    pairs.Push(new FormulaPair(leftValue.Operand, rightValue.Operand));
                    break;
                case SmtBinaryFormula leftValue when pair.Right is SmtBinaryFormula rightValue:
                    if (leftValue.Operator != rightValue.Operator) return false;
                    pairs.Push(new FormulaPair(leftValue.Left, rightValue.Left));
                    pairs.Push(new FormulaPair(leftValue.Right, rightValue.Right));
                    break;
                case SmtIntegerUnaryTerm leftValue when pair.Right is SmtIntegerUnaryTerm rightValue:
                    if (leftValue.Operator != rightValue.Operator) return false;
                    pairs.Push(new FormulaPair(leftValue.Operand, rightValue.Operand));
                    break;
                case SmtIntegerBinaryTerm leftValue when pair.Right is SmtIntegerBinaryTerm rightValue:
                    if (leftValue.Operator != rightValue.Operator) return false;
                    pairs.Push(new FormulaPair(leftValue.Left, rightValue.Left));
                    pairs.Push(new FormulaPair(leftValue.Right, rightValue.Right));
                    break;
                case SmtStringLengthTerm leftValue when pair.Right is SmtStringLengthTerm rightValue:
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    break;
                case SmtStringConcatTerm leftValue when pair.Right is SmtStringConcatTerm rightValue:
                    pairs.Push(new FormulaPair(leftValue.Left, rightValue.Left));
                    pairs.Push(new FormulaPair(leftValue.Right, rightValue.Right));
                    break;
                case SmtStringContainsFormula leftValue when pair.Right is SmtStringContainsFormula rightValue:
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    pairs.Push(new FormulaPair(leftValue.Search, rightValue.Search));
                    break;
                case SmtStringStartsWithFormula leftValue when pair.Right is SmtStringStartsWithFormula rightValue:
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    pairs.Push(new FormulaPair(leftValue.Prefix, rightValue.Prefix));
                    break;
                case SmtStringEndsWithFormula leftValue when pair.Right is SmtStringEndsWithFormula rightValue:
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    pairs.Push(new FormulaPair(leftValue.Suffix, rightValue.Suffix));
                    break;
                case SmtRegexMatchFormula leftValue when pair.Right is SmtRegexMatchFormula rightValue:
                    if (!string.Equals(leftValue.Pattern, rightValue.Pattern, StringComparison.Ordinal) ||
                        leftValue.Options != rightValue.Options)
                        return false;
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    break;
                case SmtRuntimeTypeTestFormula leftValue when pair.Right is SmtRuntimeTypeTestFormula rightValue:
                    if (!string.Equals(leftValue.TypeKey, rightValue.TypeKey, StringComparison.Ordinal)) return false;
                    pairs.Push(new FormulaPair(leftValue.Value, rightValue.Value));
                    break;
                case SmtConditionalFormula leftValue when pair.Right is SmtConditionalFormula rightValue:
                    if (leftValue.ResultKind != rightValue.ResultKind) return false;
                    pairs.Push(new FormulaPair(leftValue.Condition, rightValue.Condition));
                    pairs.Push(new FormulaPair(leftValue.WhenTrue, rightValue.WhenTrue));
                    pairs.Push(new FormulaPair(leftValue.WhenFalse, rightValue.WhenFalse));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static int GetChildCount(SmtFormula formula)
    {
        return formula switch
        {
            SmtUnaryFormula or SmtIntegerUnaryTerm or SmtStringLengthTerm or SmtRegexMatchFormula or
                SmtRuntimeTypeTestFormula => 1,
            SmtBinaryFormula or SmtIntegerBinaryTerm or SmtStringConcatTerm or SmtStringContainsFormula or
                SmtStringStartsWithFormula or SmtStringEndsWithFormula => 2,
            SmtConditionalFormula => 3,
            _ => 0
        };
    }

    private static void PushChildrenInReverse(SmtFormula formula, Stack<SmtFormula> stack)
    {
        switch (formula)
        {
            case SmtUnaryFormula unary:
                stack.Push(unary.Operand);
                break;
            case SmtBinaryFormula binary:
                stack.Push(binary.Right);
                stack.Push(binary.Left);
                break;
            case SmtIntegerUnaryTerm unary:
                stack.Push(unary.Operand);
                break;
            case SmtIntegerBinaryTerm binary:
                stack.Push(binary.Right);
                stack.Push(binary.Left);
                break;
            case SmtStringLengthTerm length:
                stack.Push(length.Value);
                break;
            case SmtStringConcatTerm concat:
                stack.Push(concat.Right);
                stack.Push(concat.Left);
                break;
            case SmtStringContainsFormula contains:
                stack.Push(contains.Search);
                stack.Push(contains.Value);
                break;
            case SmtStringStartsWithFormula startsWith:
                stack.Push(startsWith.Prefix);
                stack.Push(startsWith.Value);
                break;
            case SmtStringEndsWithFormula endsWith:
                stack.Push(endsWith.Suffix);
                stack.Push(endsWith.Value);
                break;
            case SmtRegexMatchFormula regex:
                stack.Push(regex.Value);
                break;
            case SmtRuntimeTypeTestFormula runtimeType:
                stack.Push(runtimeType.Value);
                break;
            case SmtConditionalFormula conditional:
                stack.Push(conditional.WhenFalse);
                stack.Push(conditional.WhenTrue);
                stack.Push(conditional.Condition);
                break;
        }
    }

    private static void PushChildrenInReverse(SmtFormula formula, Stack<TraversalFrame> stack)
    {
        void Push(SmtFormula child) => stack.Push(new TraversalFrame(child, false));

        switch (formula)
        {
            case SmtUnaryFormula unary:
                Push(unary.Operand);
                break;
            case SmtBinaryFormula binary:
                Push(binary.Right);
                Push(binary.Left);
                break;
            case SmtIntegerUnaryTerm unary:
                Push(unary.Operand);
                break;
            case SmtIntegerBinaryTerm binary:
                Push(binary.Right);
                Push(binary.Left);
                break;
            case SmtStringLengthTerm length:
                Push(length.Value);
                break;
            case SmtStringConcatTerm concat:
                Push(concat.Right);
                Push(concat.Left);
                break;
            case SmtStringContainsFormula contains:
                Push(contains.Search);
                Push(contains.Value);
                break;
            case SmtStringStartsWithFormula startsWith:
                Push(startsWith.Prefix);
                Push(startsWith.Value);
                break;
            case SmtStringEndsWithFormula endsWith:
                Push(endsWith.Suffix);
                Push(endsWith.Value);
                break;
            case SmtRegexMatchFormula regex:
                Push(regex.Value);
                break;
            case SmtRuntimeTypeTestFormula runtimeType:
                Push(runtimeType.Value);
                break;
            case SmtConditionalFormula conditional:
                Push(conditional.WhenFalse);
                Push(conditional.WhenTrue);
                Push(conditional.Condition);
                break;
        }
    }

    private static SmtFormula Rebuild(SmtFormula formula, IReadOnlyList<SmtFormula> children)
    {
        bool Same(int index, SmtFormula child) => ReferenceEquals(children[index], child);

        return formula switch
        {
            SmtUnaryFormula value => Same(0, value.Operand)
                ? formula
                : new SmtUnaryFormula(value.Operator, children[0]),
            SmtBinaryFormula value => Same(0, value.Left) && Same(1, value.Right)
                ? formula
                : new SmtBinaryFormula(value.Operator, children[0], children[1]),
            SmtIntegerUnaryTerm value => Same(0, value.Operand)
                ? formula
                : new SmtIntegerUnaryTerm(value.Operator, children[0]),
            SmtIntegerBinaryTerm value => Same(0, value.Left) && Same(1, value.Right)
                ? formula
                : new SmtIntegerBinaryTerm(value.Operator, children[0], children[1]),
            SmtStringLengthTerm value => Same(0, value.Value)
                ? formula
                : new SmtStringLengthTerm(children[0]),
            SmtStringConcatTerm value => Same(0, value.Left) && Same(1, value.Right)
                ? formula
                : new SmtStringConcatTerm(children[0], children[1]),
            SmtStringContainsFormula value => Same(0, value.Value) && Same(1, value.Search)
                ? formula
                : new SmtStringContainsFormula(children[0], children[1]),
            SmtStringStartsWithFormula value => Same(0, value.Value) && Same(1, value.Prefix)
                ? formula
                : new SmtStringStartsWithFormula(children[0], children[1]),
            SmtStringEndsWithFormula value => Same(0, value.Value) && Same(1, value.Suffix)
                ? formula
                : new SmtStringEndsWithFormula(children[0], children[1]),
            SmtRegexMatchFormula value => Same(0, value.Value)
                ? formula
                : new SmtRegexMatchFormula(children[0], value.Pattern, value.Options),
            SmtRuntimeTypeTestFormula value => Same(0, value.Value)
                ? formula
                : new SmtRuntimeTypeTestFormula(children[0], value.TypeKey),
            SmtConditionalFormula value =>
                Same(0, value.Condition) && Same(1, value.WhenTrue) && Same(2, value.WhenFalse)
                    ? formula
                    : new SmtConditionalFormula(children[0], children[1], children[2], value.ResultKind),
            _ => formula
        };
    }

    private readonly record struct TraversalFrame(SmtFormula Formula, bool Visited);

    private readonly record struct FormulaPair(SmtFormula Left, SmtFormula Right);
}
