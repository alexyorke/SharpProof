namespace SharpProof.ProofCore.Smt;

internal static class SmtFormulaTraversal {
    internal static IEnumerable<SmtFormula> EnumerateConjuncts(SmtFormula formula) {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } conjunction) {
            foreach (var item in EnumerateConjuncts(conjunction.Left)) yield return item;
            foreach (var item in EnumerateConjuncts(conjunction.Right)) yield return item;
        }
        else {
            yield return formula;
        }
    }

    internal static IEnumerable<SmtFormula> Enumerate(SmtFormula root) {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var stack = new Stack<SmtFormula>();
        stack.Push(root);
        while (stack.Count > 0) {
            var current = stack.Pop();
            yield return current;
            PushChildrenInReverse(current, stack);
        }
    }

    internal static bool Contains(SmtFormula root, Func<SmtFormula, bool> predicate) {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return Enumerate(root).Any(predicate);
    }

    internal static SmtFormula MapChildren(
        SmtFormula formula,
        Func<SmtFormula, SmtFormula> map) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        if (map == null) throw new ArgumentNullException(nameof(map));

        var children = GetChildren(formula);
        if (children.Count == 0) return formula;

        var mapped = new SmtFormula[children.Count];
        for (var index = 0; index < children.Count; index++)
            mapped[index] = map(children[index]) ??
                            throw new InvalidOperationException("Formula child mapping returned null.");

        return Rebuild(formula, mapped);
    }

    internal static SmtFormula RewriteBottomUp(
        SmtFormula root,
        Func<SmtFormula, SmtFormula> rewrite,
        out bool changed) {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (rewrite == null) throw new ArgumentNullException(nameof(rewrite));

        changed = false;
        var frames = new Stack<TraversalFrame>();
        var results = new Stack<SmtFormula>();
        frames.Push(new TraversalFrame(root, false));

        while (frames.Count > 0) {
            var frame = frames.Pop();
            if (!frame.Visited) {
                frames.Push(new TraversalFrame(frame.Formula, true));
                PushChildrenInReverse(frame.Formula, frames);
                continue;
            }

            var childCount = GetChildCount(frame.Formula);
            var children = childCount == 0 ? Array.Empty<SmtFormula>() : new SmtFormula[childCount];
            for (var index = childCount - 1; index >= 0; index--) children[index] = results.Pop();

            var rebuilt = Rebuild(frame.Formula, children);
            var rewritten = rewrite(rebuilt) ?? throw new InvalidOperationException("Formula rewrite returned null.");
            if (!AreStructurallyEqual(frame.Formula, rewritten)) changed = true;
            results.Push(rewritten);
        }

        return results.Pop();
    }

    internal static bool IsWithinDepth(SmtFormula root, int maxDepth) {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var stack = new Stack<(SmtFormula Formula, int Depth)>();
        stack.Push((root, 1));
        while (stack.Count > 0) {
            var (formula, depth) = stack.Pop();
            if (depth > maxDepth) return false;

            var children = GetChildren(formula);
            for (var index = children.Count - 1; index >= 0; index--)
                stack.Push((children[index], depth + 1));
        }

        return true;
    }

    internal static bool AreStructurallyEqual(SmtFormula left, SmtFormula right) {
        var pairs = new Stack<FormulaPair>();
        pairs.Push(new FormulaPair(left, right));
        while (pairs.Count > 0) {
            var pair = pairs.Pop();
            if (ReferenceEquals(pair.Left, pair.Right)) continue;
            if (pair.Left.GetType() != pair.Right.GetType() || pair.Left.Kind != pair.Right.Kind) return false;

            switch (pair.Left) {
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
                case SmtOpaqueIntegerBinaryTerm leftValue
                    when pair.Right is SmtOpaqueIntegerBinaryTerm rightValue:
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

    private static int GetChildCount(SmtFormula formula) =>
        GetChildren(formula).Count;

    private static void PushChildrenInReverse(SmtFormula formula, Stack<SmtFormula> stack) {
        var children = GetChildren(formula);
        for (var index = children.Count - 1; index >= 0; index--) stack.Push(children[index]);
    }

    private static void PushChildrenInReverse(SmtFormula formula, Stack<TraversalFrame> stack) {
        var children = GetChildren(formula);
        for (var index = children.Count - 1; index >= 0; index--)
            stack.Push(new TraversalFrame(children[index], false));
    }

    private static FormulaChildren GetChildren(SmtFormula formula) {
        return formula switch {
            SmtUnaryFormula unary => new FormulaChildren(unary.Operand),
            SmtBinaryFormula binary => new FormulaChildren(binary.Left, binary.Right),
            SmtIntegerUnaryTerm unary => new FormulaChildren(unary.Operand),
            SmtIntegerBinaryTerm binary => new FormulaChildren(binary.Left, binary.Right),
            SmtOpaqueIntegerBinaryTerm binary => new FormulaChildren(binary.Left, binary.Right),
            SmtStringLengthTerm length => new FormulaChildren(length.Value),
            SmtStringConcatTerm concat => new FormulaChildren(concat.Left, concat.Right),
            SmtStringContainsFormula contains => new FormulaChildren(contains.Value, contains.Search),
            SmtStringStartsWithFormula startsWith => new FormulaChildren(startsWith.Value, startsWith.Prefix),
            SmtStringEndsWithFormula endsWith => new FormulaChildren(endsWith.Value, endsWith.Suffix),
            SmtRegexMatchFormula regex => new FormulaChildren(regex.Value),
            SmtRuntimeTypeTestFormula runtimeType => new FormulaChildren(runtimeType.Value),
            SmtConditionalFormula conditional =>
                new FormulaChildren(conditional.Condition, conditional.WhenTrue, conditional.WhenFalse),
            _ => default
        };
    }

    private static SmtFormula Rebuild(SmtFormula formula, IReadOnlyList<SmtFormula> children) {
        bool Same(int index, SmtFormula child) => ReferenceEquals(children[index], child);

        return formula switch {
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
            SmtOpaqueIntegerBinaryTerm value => Same(0, value.Left) && Same(1, value.Right)
                ? formula
                : new SmtOpaqueIntegerBinaryTerm(value.Operator, children[0], children[1]),
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

    private readonly record struct FormulaChildren(
        SmtFormula? First,
        SmtFormula? Second = null,
        SmtFormula? Third = null) {
        internal int Count => Third != null ? 3 : Second != null ? 2 : First != null ? 1 : 0;

        internal SmtFormula this[int index] => index switch {
            0 when First != null => First,
            1 when Second != null => Second,
            2 when Third != null => Third,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
