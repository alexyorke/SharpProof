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
    internal static IEnumerable<SmtFormula> EnumerateChildren(SmtFormula formula) {
        var children = GetChildren(formula);
        for (var index = 0; index < children.Count; index++) yield return children[index];
    }
    internal static SmtFormula RewriteBottomUp(SmtFormula root, Func<SmtFormula, SmtFormula> rewrite, out bool changed) {
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
            var childCount = GetChildren(frame.Formula).Count;
            var children = childCount == 0 ? [] : new SmtFormula[childCount];
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
    internal static bool AreStructurallyEqual(SmtFormula left, SmtFormula right) => Equals(left, right);
    private static void PushChildrenInReverse(SmtFormula formula, Stack<SmtFormula> stack) {
        var children = GetChildren(formula);
        for (var index = children.Count - 1; index >= 0; index--) stack.Push(children[index]);
    }
    private static void PushChildrenInReverse(SmtFormula formula, Stack<TraversalFrame> stack) {
        var children = GetChildren(formula);
        for (var index = children.Count - 1; index >= 0; index--)
            stack.Push(new TraversalFrame(children[index], false));
    }
    private static FormulaChildren GetChildren(SmtFormula formula) => formula switch {
        SmtUnaryFormula unary => new FormulaChildren(unary.Operand),
        SmtBinaryFormula binary => new FormulaChildren(binary.Left, binary.Right),
        SmtIntegerUnaryTerm unary => new FormulaChildren(unary.Operand),
        SmtIntegerBinaryTerm binary => new FormulaChildren(binary.Left, binary.Right),
        SmtOpaqueIntegerBinaryTerm binary => new FormulaChildren(binary.Left, binary.Right),
        SmtStringLengthTerm length => new FormulaChildren(length.Value),
        SmtStringConcatTerm concat => new FormulaChildren(concat.Left, concat.Right),
        SmtStringSubstringTerm substring => new FormulaChildren(substring.Value, substring.Offset, substring.Length),
        SmtStringContainsFormula contains => new FormulaChildren(contains.Value, contains.Search),
        SmtStringStartsWithFormula startsWith => new FormulaChildren(startsWith.Value, startsWith.Prefix),
        SmtStringEndsWithFormula endsWith => new FormulaChildren(endsWith.Value, endsWith.Suffix),
        SmtRegexMatchFormula regex => new FormulaChildren(regex.Value),
        SmtRuntimeTypeTestFormula runtimeType => new FormulaChildren(runtimeType.Value),
        SmtConditionalFormula conditional =>
            new FormulaChildren(conditional.Condition, conditional.WhenTrue, conditional.WhenFalse),
        _ => default
    };
    private static SmtFormula Rebuild(SmtFormula formula, IReadOnlyList<SmtFormula> children) {
        var originalChildren = GetChildren(formula);
        var unchanged = true;
        for (var index = 0; index < children.Count; index++)
            unchanged &= ReferenceEquals(children[index], originalChildren[index]);
        if (unchanged) return formula;
        return formula switch {
            SmtUnaryFormula value => value with { Operand = children[0] },
            SmtBinaryFormula value => value with { Left = children[0], Right = children[1] },
            SmtIntegerUnaryTerm value => value with { Operand = children[0] },
            SmtIntegerBinaryTerm value => value with { Left = children[0], Right = children[1] },
            SmtOpaqueIntegerBinaryTerm value => value with { Left = children[0], Right = children[1] },
            SmtStringLengthTerm value => value with { Value = children[0] },
            SmtStringConcatTerm value => value with { Left = children[0], Right = children[1] },
            SmtStringSubstringTerm value =>
                value with { Value = children[0], Offset = children[1], Length = children[2] },
            SmtStringContainsFormula value => value with { Value = children[0], Search = children[1] },
            SmtStringStartsWithFormula value => value with { Value = children[0], Prefix = children[1] },
            SmtStringEndsWithFormula value => value with { Value = children[0], Suffix = children[1] },
            SmtRegexMatchFormula value => value with { Value = children[0] },
            SmtRuntimeTypeTestFormula value => value with { Value = children[0] },
            SmtConditionalFormula value =>
                value with { Condition = children[0], WhenTrue = children[1], WhenFalse = children[2] },
            _ => formula
        };
    }
    readonly record struct TraversalFrame(SmtFormula Formula, bool Visited);
    readonly record struct FormulaChildren(SmtFormula? First, SmtFormula? Second = null, SmtFormula? Third = null) {
        internal int Count => Third != null ? 3 : Second != null ? 2 : First != null ? 1 : 0;
        internal SmtFormula this[int index] => index switch {
            0 when First != null => First,
            1 when Second != null => Second,
            2 when Third != null => Third,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
