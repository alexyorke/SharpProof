using static SharpProof.Symbolic.Ir.SymbolicIndexingLowerer;
using static SharpProof.Symbolic.Ir.SymbolicLoweringValueFacts;
namespace SharpProof.Symbolic.Ir;
internal static partial class SymbolicIrLowerer {
    internal static SymbolicTerm? LowerTerm(ExpressionSyntax expression, SymbolicLoweringContext context) =>
        TryLowerTerm(BoundNode.Bind(expression, context), out var term) ? term : null;
    internal static SymbolicCondition? LowerCondition(ExpressionSyntax expression, SymbolicLoweringContext context) =>
        TryLowerCondition(BoundNode.Bind(expression, context), out var condition) ? condition : null;
    private static bool TryLowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) =>
        TryLowerCondition(BoundNode.Bind(expression, context), out condition);
    private static bool TryLowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) =>
        TryLowerTerm(BoundNode.Bind(expression, context), out term);
    internal static bool TryLowerReferenceTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) =>
        TryLowerTerm(BoundNode.Bind(expression, context), true, out term);
    private static bool TryLowerCondition(BoundNode node, out SymbolicCondition condition) {
        var expression = node.Syntax;
        var context = node.Context;
        var constantValue = node.Constant;
        if (constantValue.HasValue && constantValue.Value is bool booleanValue) {
            condition = new SymbolicConstantCondition(booleanValue);
            return true;
        }
        if (NullableFlowFacts.TryEvaluateNullTest(expression, context.SemanticModel, context.CancellationToken,
            out var nullableFlowValue)) {
            condition = new SymbolicConstantCondition(nullableFlowValue);
            return true;
        }
        switch (node.Kind) {
            case SyntaxKind.LogicalNotExpression:
                var prefix = (PrefixUnaryExpressionSyntax)expression;
                if (SymbolicRegexLowerer.TryLowerNegatedRegexInvocationPredicate(prefix.Operand, context, out condition))
                    return true;
                if (TryLowerCondition(prefix.Operand, context, out var operand)) {
                    condition = new SymbolicNotCondition(operand);
                    return true;
                }
                break;
            case SyntaxKind.ConditionalExpression:
                var conditional = (ConditionalExpressionSyntax)expression;
                if (SymbolicSourcePredicateLowerer.TryLowerBooleanConditional(
                        conditional.Condition, conditional.WhenTrue, conditional.WhenFalse, context, out condition))
                    return true;
                break;
            case SyntaxKind.SwitchExpression:
                if (SymbolicSourcePredicateLowerer.TryLowerBooleanSwitchExpression(
                        (SwitchExpressionSyntax)expression,
                        context,
                        out condition))
                    return true;
                break;
            case SyntaxKind.IsPatternExpression:
                var isPattern = (IsPatternExpressionSyntax)expression;
                if (SymbolicPatternLowerer.TryLowerNullablePatternCondition(isPattern, context, out condition) ||
                    TryLowerTerm(isPattern.Expression, context, out var patternValue) &&
                    SymbolicPatternLowerer.TryLowerPatternCondition(
                        patternValue,
                        context.SemanticModel.GetTypeInfo(isPattern.Expression, context.CancellationToken).ConvertedType ??
                        context.SemanticModel.GetTypeInfo(isPattern.Expression, context.CancellationToken).Type,
                        isPattern.Pattern,
                        isPattern,
                        context,
                        out condition))
                    return true;
                break;
            default:
                if (expression is BinaryExpressionSyntax binary &&
                    TryLowerBinaryCondition(binary, node, out condition))
                    return true;
                break;
        }
        if (SymbolicRegexLowerer.TryLowerRegexMatchSuccessCondition(expression, context, out condition)) return true;
        if (expression is InvocationExpressionSyntax sourceInvocation &&
            SymbolicSourcePredicateLowerer.TryLowerSourceBooleanInvocation(sourceInvocation, context, out condition))
            return true;
        if (expression is InvocationExpressionSyntax knownInvocation &&
            SymbolicKnownApiLowerer.TryLowerKnownApiInvocation(knownInvocation, context, out condition))
            return true;
        if (expression is MemberAccessExpressionSyntax sourceBooleanProperty &&
            context.SemanticModel.GetSymbolInfo(sourceBooleanProperty, context.CancellationToken).Symbol is
                IPropertySymbol sourceBooleanPropertySymbol &&
            SymbolicMemberLowerer.TryLowerSourceBooleanPropertyCondition(
                sourceBooleanProperty,
                sourceBooleanPropertySymbol,
                context,
                out condition))
            return true;
        if (TryLowerTerm(node, out var term) &&
            term.Kind == SmtValueKind.Bool) {
            condition = CreateFactCondition(new SymbolicTruthAtom(term), expression, "ir.truth");
            return true;
        }
        condition = null!;
        return false;
    }
    private static bool TryLowerBinaryCondition(
        BinaryExpressionSyntax expression,
        BoundNode node,
        out SymbolicCondition condition) {
        var context = node.Context;
        if (expression.IsKind(SyntaxKind.IsExpression) &&
            expression.Right is TypeSyntax typeSyntax &&
            TryLowerTerm(expression.Left, context, out var typeTestValue) &&
            SymbolicPatternLowerer.TryLowerTypeTestCondition(
                typeTestValue, typeSyntax, expression, false, context, out condition))
            return true;
        if (TryLowerLogicalBinaryCondition(expression, context, out condition) ||
            SymbolicOperatorLowerer.TryLowerBuiltInBooleanBitwiseCondition(expression, context, out condition) ||
            SymbolicTypeLowerer.TryLowerTypeOfComparison(expression, context, out condition) ||
            SymbolicConversionLowerer.TryLowerUnsignedCastBoundsComparison(expression, context, out condition) ||
            SymbolicConversionLowerer.TryLowerCheckedIntegralConversionComparison(expression, context, out condition) ||
            SymbolicConversionLowerer.TryLowerDecimalZeroComparison(expression, context, out condition) ||
            SymbolicNullableLowerer.TryLowerNotNullIfNotNullNullComparison(expression, context, out condition) ||
            SymbolicNullableLowerer.TryLowerNullableNullComparisonCondition(expression, context, out condition) ||
            SymbolicRegexLowerer.TryLowerRegexMatchesCountComparison(expression, context, out condition) ||
            SymbolicStringLowerer.TryLowerStringSearchComparison(expression, context, out condition) ||
            SymbolicStringLowerer.TryLowerPrefixSubstringComparison(expression, context, out condition))
            return true;
        var isEquality = SymbolicOperatorLowerer.IsEqualityExpression(expression);
        if (isEquality &&
            (SymbolicStringLowerer.TryLowerStringEqualityCondition(expression, context, out condition) ||
             SymbolicTupleLowerer.TryLowerTupleEqualityCondition(expression, context, out condition) ||
             SymbolicStringLengthLowerer.TryLowerStringResultLengthIdentityCondition(expression, context, out condition)))
            return true;
        if (!SymbolicOperatorLowerer.TryGetRelationOperator(expression.Kind(), out var relation))
            return NoCondition(out condition);
        if (SymbolicNullableLowerer.TryLowerNullableValueAccessRelationCondition(
                expression, relation, context, out condition) ||
            SymbolicNullableLowerer.TryLowerNullableRelationCondition(expression, relation, context, out condition))
            return true;
        if (node.Operation is IBinaryOperation operation &&
            SymbolicOperatorLowerer.HasBuiltInNullSemantics(operation) &&
            TryLowerTerm(expression.Left, context, out var left) &&
            TryLowerTerm(expression.Right, context, out var right) &&
            SymbolicOperatorLowerer.CanCompareTerms(left, right, relation)) {
            condition = CreateFactCondition(new SymbolicRelationAtom(relation, left, right), expression, "ir.relation");
            return true;
        }
        return NoCondition(out condition);
    }
    private static bool TryLowerLogicalBinaryCondition(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        var conditionOperator = expression.Kind() switch {
            SyntaxKind.LogicalAndExpression => SymbolicConditionOperator.And,
            SyntaxKind.LogicalOrExpression => SymbolicConditionOperator.Or,
            _ => (SymbolicConditionOperator?)null
        };
        if (conditionOperator != null &&
            TryLowerCondition(expression.Left, context, out var left) &&
            TryLowerCondition(expression.Right, context, out var right)) {
            condition = new SymbolicBinaryCondition(conditionOperator.Value, left, right);
            return true;
        }
        condition = null!;
        return false;
    }
    private static bool NoCondition(out SymbolicCondition condition) {
        condition = null!;
        return false;
    }
    private static bool TryLowerTerm(BoundNode node, out SymbolicTerm term) =>
        TryLowerTerm(node, false, out term);
    private static bool TryLowerTerm(BoundNode node, bool referenceMode, out SymbolicTerm term) {
        var expression = node.Syntax;
        var context = node.Context;
        var constantValue = node.Constant;
        if (referenceMode && node.Type is not { IsReferenceType: true })
            return NoTerm(out term);
        if (constantValue.HasValue &&
            TryCreateConstantTerm(constantValue.Value, out term) &&
            (!referenceMode || term.Kind == SmtValueKind.Reference))
            return true;
        if (!referenceMode &&
            TryLowerDefaultValueTerm(node, out term))
            return true;
        if (!referenceMode)
            switch (node.Kind) {
                case SyntaxKind.CheckedExpression:
                    if (TryLowerTerm(((CheckedExpressionSyntax)expression).Expression, context, out term))
                        return true;
                    break;
                case SyntaxKind.UncheckedExpression:
                case SyntaxKind.CastExpression:
                    if (SymbolicConversionLowerer.TryLowerSupportedConversionTerm(node, out term))
                        return true;
                    break;
                case SyntaxKind.AwaitExpression:
                case SyntaxKind.SimpleMemberAccessExpression:
                    if (TryLowerCompletedAsyncTerm(expression, context, out term))
                        return true;
                    break;
                case SyntaxKind.InvocationExpression:
                    if (TryLowerCompletedAsyncTerm(expression, context, out term) ||
                        SymbolicStringLowerer.TryLowerStringExpressionTerm(expression, context, out term))
                        return true;
                    var invocation = (InvocationExpressionSyntax)expression;
                    if (context.InvocationTermLowerer != null &&
                        context.InvocationTermLowerer(invocation, context, out var customTerm)) {
                        term = customTerm;
                        return true;
                    }
                    if (SymbolicKnownApiLowerer.TryLowerKnownApiInvocationTerm(invocation, context, out term))
                        return true;
                    break;
                case SyntaxKind.AddExpression:
                case SyntaxKind.InterpolatedStringExpression:
                    if (SymbolicStringLowerer.TryLowerStringExpressionTerm(expression, context, out term))
                        return true;
                    break;
            }
        if (expression is ThisExpressionSyntax) {
            term = context.ImplicitThis;
            return true;
        }
        if (expression is ElementAccessExpressionSyntax elementAccess &&
            TryLowerElementAccessTerm(elementAccess, context, out term) &&
            (!referenceMode || term.Kind == SmtValueKind.Reference))
            return true;
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
            SymbolicReferenceLowerer.TryLowerReferenceConditionalAccessTerm(conditionalAccess, context, out term))
            return true;
        if (!referenceMode &&
            expression is BinaryExpressionSyntax nullableCoalesceExpression &&
            nullableCoalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            SymbolicNullableLowerer.TryLowerNullableCoalesceValueTerm(nullableCoalesceExpression, context, out term))
            return true;
        if (!referenceMode &&
            expression is AssignmentExpressionSyntax coalesceAssignment &&
            coalesceAssignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
            SymbolicNullableLowerer.TryLowerCoalesceAssignmentTerm(coalesceAssignment, context, out term))
            return true;
        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerTerm(BoundNode.Bind(coalesceExpression.Left, context), referenceMode, out var coalesceLeft) &&
            TryLowerTerm(BoundNode.Bind(coalesceExpression.Right, context), referenceMode, out var coalesceRight) &&
            coalesceLeft.Kind == SmtValueKind.Reference &&
            coalesceRight.Kind == SmtValueKind.Reference) {
            term = new SymbolicConditionalTerm(
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    coalesceLeft,
                    new SymbolicNullTerm(),
                    coalesceExpression.Left,
                    referenceMode ? "ir.reference.coalesce.left-not-null" : "ir.coalesce.left-not-null"),
                coalesceLeft,
                coalesceRight);
            return true;
        }
        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerCondition(conditionalExpression.Condition, context, out var condition) &&
            TryLowerTerm(BoundNode.Bind(conditionalExpression.WhenTrue, context), referenceMode, out var whenTrue) &&
            TryLowerTerm(BoundNode.Bind(conditionalExpression.WhenFalse, context), referenceMode, out var whenFalse) &&
            whenTrue.Kind == whenFalse.Kind) {
            term = new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
            return true;
        }
        if (!referenceMode &&
            expression is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
            node.Operation is
                Microsoft.CodeAnalysis.Operations.IUnaryOperation unaryOperation &&
            (unaryOperation.OperatorMethod == null ||
             unaryOperation.Type != null && SymbolicNumericLowerer.IsBigIntegerType(unaryOperation.Type)) &&
            TryLowerTerm(prefixUnary.Operand, context, out var unaryOperand) &&
            unaryOperand.Kind == SmtValueKind.Int) {
            var mathematicalTerm = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                new SymbolicIntegerConstantTerm(0),
                unaryOperand);
            term = TryGetBoundedIntegralRange(prefixUnary, unaryOperation.Type, context, out var minimum, out var maximum) &&
                   minimum < 0
                ? CreateOverflowAwareBinaryTerm(
                    mathematicalTerm,
                    minimum,
                    maximum,
                    prefixUnary,
                    "ir.numeric.unary-negation",
                    unaryOperation.IsChecked)
                : mathematicalTerm;
            return true;
        }
        if (expression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression) &&
            SymbolicConversionLowerer.TryLowerReferenceAsTerm(asExpression, context, out term))
            return true;
        if (!referenceMode &&
            expression is BinaryExpressionSyntax binary &&
            SymbolicOperatorLowerer.TryGetBinaryTermOperator(binary.Kind(), out var binaryOperator) &&
            node.Operation is
                Microsoft.CodeAnalysis.Operations.IBinaryOperation binaryOperation &&
            (binaryOperation.OperatorMethod == null ||
             binaryOperation.Type != null && SymbolicNumericLowerer.IsBigIntegerType(binaryOperation.Type)) &&
            TryLowerTerm(binary.Left, context, out var left) &&
            TryLowerTerm(binary.Right, context, out var right) &&
            left.Kind == SmtValueKind.Int &&
            right.Kind == SmtValueKind.Int) {
            var mathematicalTerm = new SymbolicBinaryTerm(binaryOperator, left, right);
            term = IsOverflowSensitiveIntegralBinary(binary, binaryOperation) &&
                   TryGetBoundedIntegralRange(binary, binaryOperation.Type, context, out var minimum, out var maximum)
                ? CreateOverflowAwareBinaryTerm(mathematicalTerm, minimum, maximum, binary, "ir.numeric.binary", binaryOperation.IsChecked)
                : mathematicalTerm;
            return true;
        }
        static bool TryGetBoundedIntegralRange(
            ExpressionSyntax expression,
            ITypeSymbol? operationType,
            SymbolicLoweringContext context,
            out long minimum,
            out long maximum) {
            if (SymbolicTypeFacts.TryGetBoundedIntegralRange(operationType, out minimum, out maximum))
                return true;
            if (context.InvocationTermTypeResolver != null)
                foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()) {
                    var invocationType = context.InvocationTermTypeResolver(invocation);
                    if (SymbolicTypeFacts.TryGetBoundedIntegralRange(invocationType, out minimum, out maximum))
                        return true;
                }
            minimum = default;
            maximum = default;
            return false;
        }
        static bool IsOverflowSensitiveIntegralBinary(
            BinaryExpressionSyntax candidate,
            Microsoft.CodeAnalysis.Operations.IBinaryOperation operation) {
            var type = operation?.Type;
            if (candidate.Kind() is not (SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or
                    SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or
                    SyntaxKind.ModuloExpression))
                return false;
            // Speculative contract expressions such as old(value) can leave the
            // enclosing Roslyn operation error-typed even though both lowered
            // operands are integral. Preserve the conservative overflow marker.
            if (type == null || type.TypeKind == TypeKind.Error) return true;
            if (!SymbolicTypeFacts.TryGetBoundedIntegralRange(type, out _, out _)) return false;
            // Z3 integers are unbounded. Wrap-capable arithmetic must remain opaque.
            // Division and remainder retain their mathematical normal-completion
            // semantics; their zero and MinValue / -1 exceptional paths are modeled
            // separately as runtime hazards.
            return candidate.Kind() is not (SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression);
        }
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            SymbolicMemberLowerer.TryLowerMemberTerm(node, out term) &&
            (!referenceMode || term.Kind == SmtValueKind.Reference))
            return true;
        if (SymbolicMemberLowerer.TryLowerImplicitThisMemberTerm(node, out term) &&
            (!referenceMode || term.Kind == SmtValueKind.Reference))
            return true;
        var symbol = node.Symbol;
        if (symbol != null &&
            context.TryGetSubstitution(symbol, out term) &&
            (!referenceMode || term.Kind == SmtValueKind.Reference))
            return true;
        if ((symbol is ILocalSymbol || symbol is IParameterSymbol) &&
            SymbolicTypeLowerer.TryGetSymbolType(symbol, out var symbolType)) {
            if (referenceMode && symbolType.IsReferenceType) {
                term = new SymbolicVariableTerm(context.GetVariableName(symbol), SmtValueKind.Reference);
                return true;
            }
            if (!referenceMode && SymbolicTypeLowerer.TryGetValueKind(symbolType, out var kind)) {
                term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
                return true;
            }
        }
        term = null!;
        return false;
    }
    private static bool NoTerm(out SymbolicTerm term) {
        term = null!;
        return false;
    }
    private static bool TryLowerCompletedAsyncTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) =>
        SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression(expression, context, out var result) &&
        TryLowerTerm(result, context, out term) ||
        NoTerm(out term);
    private static bool TryCreateConstantTerm(object? value, out SymbolicTerm term) {
        term = value switch {
            bool boolean => new SymbolicBooleanConstantTerm(boolean),
            null => new SymbolicNullTerm(),
            string text => new SymbolicStringConstantTerm(text),
            _ when TryGetIntegralConstant(value, out var integer) => new SymbolicIntegerConstantTerm(integer),
            _ => null!
        };
        return term != null;
    }
    private static bool TryLowerDefaultValueTerm(BoundNode node, out SymbolicTerm term) {
        if (node.Kind is not SyntaxKind.DefaultLiteralExpression and
            not SyntaxKind.DefaultExpression)
            return NoTerm(out term);
        term = node.Type switch {
            { SpecialType: SpecialType.System_Boolean } => new SymbolicBooleanConstantTerm(false),
            { IsReferenceType: true } => new SymbolicNullTerm(),
            { } type when SymbolicTypeLowerer.IsIntegerSmtType(type) => new SymbolicIntegerConstantTerm(0),
            _ => null!
        };
        return term != null;
    }
    internal static SymbolicTerm CreateOverflowAwareBinaryTerm(
        SymbolicBinaryTerm mathematicalTerm,
        long minimum,
        long maximum,
        SyntaxNode syntax,
        string provenance,
        bool isChecked) {
        // A checked expression has a value only on normal completion. On that path
        // its CLR result is the mathematical result; overflow exits by exception.
        if (isChecked) return mathematicalTerm;
        var leftInRange = CreateIntegerInRangeCondition(mathematicalTerm.Left, minimum, maximum, syntax, provenance + ".left");
        var rightInRange = CreateIntegerInRangeCondition(mathematicalTerm.Right, minimum, maximum, syntax, provenance + ".right");
        var resultInRange = CreateIntegerInRangeCondition(mathematicalTerm, minimum, maximum, syntax, provenance + ".result");
        // Values outside the CLR operand domain are impossible program inputs, so
        // define the extension mathematically there. Inside the domain, only the
        // true overflow branch is opaque. This preserves guarded exact proofs
        // without assigning unsound mathematical semantics to wrapped results.
        var operandsOutOfRange = new SymbolicNotCondition(
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, leftInRange, rightInRange));
        var modulus = unchecked((ulong)(maximum - minimum)) + 1UL;
        if ((mathematicalTerm.Operator is SymbolicBinaryTermOperator.Add or
                SymbolicBinaryTermOperator.Subtract) &&
            modulus != 0 &&
            modulus <= long.MaxValue) {
            var aboveMaximum = CreateRelationCondition(
                SymbolicRelationOperator.GreaterThan,
                mathematicalTerm,
                new SymbolicIntegerConstantTerm(maximum),
                syntax,
                provenance + ".above-maximum");
            var belowMinimum = CreateRelationCondition(
                SymbolicRelationOperator.LessThan,
                mathematicalTerm,
                new SymbolicIntegerConstantTerm(minimum),
                syntax,
                provenance + ".below-minimum");
            var wrapped = new SymbolicConditionalTerm(
                aboveMaximum,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    mathematicalTerm,
                    new SymbolicIntegerConstantTerm((long)modulus)),
                new SymbolicConditionalTerm(
                    belowMinimum,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, mathematicalTerm,
                        new SymbolicIntegerConstantTerm((long)modulus)),
                    mathematicalTerm));
            return new SymbolicConditionalTerm(operandsOutOfRange, mathematicalTerm, wrapped);
        }
        var exactBranch = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, operandsOutOfRange, resultInRange);
        return new SymbolicConditionalTerm(
            exactBranch,
            mathematicalTerm,
            mathematicalTerm with { MayOverflow = true });
    }
}
