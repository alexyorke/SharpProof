using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    internal static bool TryLowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        expression = UnwrapExpression(expression);
        context.CancellationToken.ThrowIfCancellationRequested();

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value is bool booleanValue)
        {
            condition = new SymbolicConstantCondition(booleanValue);
            return true;
        }

        if (NullableFlowFacts.TryEvaluateNullTest(
                expression,
                context.SemanticModel,
                context.CancellationToken,
                out var nullableFlowValue))
        {
            condition = new SymbolicConstantCondition(nullableFlowValue);
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
            SymbolicRegexLowerer.TryLowerNegatedRegexInvocationPredicate(prefixUnary.Operand, context, out condition))
            return true;

        if (expression is PrefixUnaryExpressionSyntax prefixUnaryExpression &&
            prefixUnaryExpression.IsKind(SyntaxKind.LogicalNotExpression) &&
            TryLowerCondition(prefixUnaryExpression.Operand, context, out var operand))
        {
            condition = new SymbolicNotCondition(operand);
            return true;
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            SymbolicSourcePredicateLowerer.TryLowerBooleanConditional(
                conditionalExpression.Condition,
                conditionalExpression.WhenTrue,
                conditionalExpression.WhenFalse,
                context,
                out condition))
            return true;

        if (expression is BinaryExpressionSyntax binaryExpression)
        {
            if (binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                binaryExpression.Right is TypeSyntax typeSyntax &&
                SymbolicPatternLowerer.TryLowerTypeTestCondition(binaryExpression.Left, typeSyntax, binaryExpression, false, context,
                    out condition))
                return true;

            if (TryLowerLogicalBinaryCondition(binaryExpression, context, out condition)) return true;

            if (TryLowerBuiltInBooleanBitwiseCondition(binaryExpression, context, out condition)) return true;

            if (TryLowerTypeOfComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicConversionLowerer.TryLowerUnsignedCastBoundsComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicConversionLowerer.TryLowerCheckedIntegralConversionComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicConversionLowerer.TryLowerDecimalZeroComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicNullableLowerer.TryLowerNotNullIfNotNullNullComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicNullableLowerer.TryLowerNullableNullComparisonCondition(binaryExpression, context, out condition)) return true;

            if (SymbolicRegexLowerer.TryLowerRegexMatchesCountComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicStringLowerer.TryLowerStringSearchComparison(binaryExpression, context, out condition)) return true;

            if (SymbolicStringLowerer.TryLowerPrefixSubstringComparison(binaryExpression, context, out condition)) return true;

            if (IsEqualityExpression(binaryExpression) &&
                SymbolicStringLowerer.TryLowerStringEqualityCondition(binaryExpression, context, out condition))
                return true;

            if (IsEqualityExpression(binaryExpression) &&
                SymbolicTupleLowerer.TryLowerTupleEqualityCondition(binaryExpression, context, out condition))
                return true;

            if (IsEqualityExpression(binaryExpression) &&
                SymbolicStringLengthLowerer.TryLowerStringResultLengthIdentityCondition(binaryExpression, context, out condition))
                return true;

            if (TryGetRelationOperator(binaryExpression.Kind(), out var nullableRelationOperator) &&
                SymbolicNullableLowerer.TryLowerNullableValueAccessRelationCondition(
                    binaryExpression,
                    nullableRelationOperator,
                    context,
                    out condition))
                return true;

            if (TryGetRelationOperator(binaryExpression.Kind(), out nullableRelationOperator) &&
                SymbolicNullableLowerer.TryLowerNullableRelationCondition(
                    binaryExpression,
                    nullableRelationOperator,
                    context,
                    out condition))
                return true;

            if (TryGetRelationOperator(binaryExpression.Kind(), out var relationOperator) &&
                TryLowerTerm(binaryExpression.Left, context, out var left) &&
                TryLowerTerm(binaryExpression.Right, context, out var right) &&
                CanCompareTerms(left, right, relationOperator))
            {
                condition = CreateFactCondition(
                    new SymbolicRelationAtom(relationOperator, left, right),
                    binaryExpression,
                    "ir.relation");
                return true;
            }
        }

        if (expression is IsPatternExpressionSyntax isPatternExpression &&
            (SymbolicPatternLowerer.TryLowerNullablePatternCondition(isPatternExpression, context, out condition) ||
             (TryLowerTerm(isPatternExpression.Expression, context, out var patternValue) &&
              SymbolicPatternLowerer.TryLowerPatternCondition(
                  patternValue,
                  context.SemanticModel.GetTypeInfo(
                      isPatternExpression.Expression,
                      context.CancellationToken).ConvertedType ??
                  context.SemanticModel.GetTypeInfo(
                      isPatternExpression.Expression,
                      context.CancellationToken).Type,
                  isPatternExpression.Pattern,
                  isPatternExpression,
                  context,
                  out condition)) ||
             SymbolicPatternLowerer.TryLowerBinaryPatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerNullPatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerConstantPatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerRelationalPatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerEmptyRecursivePatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerTypePatternCondition(isPatternExpression, context, out condition) ||
             SymbolicPatternLowerer.TryLowerUnaryPatternCondition(isPatternExpression.Expression, isPatternExpression.Pattern, context,
                 out condition)))
            return true;

        if (SymbolicRegexLowerer.TryLowerRegexMatchSuccessCondition(expression, context, out condition)) return true;

        if (expression is InvocationExpressionSyntax sourceInvocation &&
            SymbolicSourcePredicateLowerer.TryLowerSourceBooleanInvocation(sourceInvocation, context, out condition))
            return true;

        if (expression is InvocationExpressionSyntax knownInvocation &&
            TryLowerKnownApiInvocation(knownInvocation, context, out condition))
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

        if (TryLowerTerm(expression, context, out var term) &&
            term.Kind == SmtValueKind.Bool)
        {
            condition = CreateFactCondition(new SymbolicTruthAtom(term), expression, "ir.truth");
            return true;
        }

        condition = null!;
        return false;
    }

    private static bool TryLowerLogicalBinaryCondition(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        var conditionOperator = expression.Kind() switch
        {
            SyntaxKind.LogicalAndExpression => SymbolicConditionOperator.And,
            SyntaxKind.LogicalOrExpression => SymbolicConditionOperator.Or,
            _ => (SymbolicConditionOperator?)null
        };
        if (conditionOperator != null &&
            TryLowerCondition(expression.Left, context, out var left) &&
            TryLowerCondition(expression.Right, context, out var right))
        {
            condition = new SymbolicBinaryCondition(conditionOperator.Value, left, right);
            return true;
        }

        condition = null!;
        return false;
    }

    internal static bool TryLowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = UnwrapExpression(expression);
        context.CancellationToken.ThrowIfCancellationRequested();

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue)
        {
            if (constantValue.Value is bool booleanValue)
            {
                term = new SymbolicBooleanConstantTerm(booleanValue);
                return true;
            }

            if (constantValue.Value == null)
            {
                term = new SymbolicNullTerm();
                return true;
            }

            if (constantValue.Value is string stringValue)
            {
                term = new SymbolicStringConstantTerm(stringValue);
                return true;
            }

            if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
            {
                term = new SymbolicIntegerConstantTerm(integralValue);
                return true;
            }
        }

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NullLiteralExpression))
        {
            term = new SymbolicNullTerm();
            return true;
        }

        if (SymbolicNumericLowerer.TryLowerDefaultValueTerm(expression, context, out term)) return true;

        if (expression is CheckedExpressionSyntax checkedExpression &&
            checkedExpression.IsKind(SyntaxKind.CheckedExpression) &&
            TryLowerTerm(checkedExpression.Expression, context, out term))
            return true;

        if (SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression(expression, context, out var completedResultExpression) &&
            TryLowerTerm(completedResultExpression, context, out term))
            return true;

        if (SymbolicConversionLowerer.TryLowerSupportedConversionTerm(expression, context, out term)) return true;

        if (expression is ThisExpressionSyntax)
        {
            term = context.ImplicitThis;
            return true;
        }

        if (SymbolicStringLowerer.TryLowerStringExpressionTerm(expression, context, out term)) return true;

        if (expression is InvocationExpressionSyntax customInvocation &&
            context.InvocationTermLowerer != null &&
            context.InvocationTermLowerer(customInvocation, context, out term))
            return true;

        if (expression is InvocationExpressionSyntax invocation &&
            TryLowerKnownApiInvocationTerm(invocation, context, out term))
            return true;

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            TryLowerElementAccessTerm(elementAccess, context, out term))
            return true;

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
            TryLowerReferenceConditionalAccessTerm(conditionalAccess, context, out term))
            return true;

        if (expression is BinaryExpressionSyntax nullableCoalesceExpression &&
            nullableCoalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            SymbolicNullableLowerer.TryLowerNullableCoalesceValueTerm(nullableCoalesceExpression, context, out term))
            return true;

        if (expression is AssignmentExpressionSyntax coalesceAssignment &&
            coalesceAssignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
            SymbolicNullableLowerer.TryLowerCoalesceAssignmentTerm(coalesceAssignment, context, out term))
            return true;

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerTerm(coalesceExpression.Left, context, out var coalesceLeft) &&
            TryLowerTerm(coalesceExpression.Right, context, out var coalesceRight) &&
            coalesceLeft.Kind == SmtValueKind.Reference &&
            coalesceRight.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicConditionalTerm(
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    coalesceLeft,
                    new SymbolicNullTerm(),
                    coalesceExpression.Left,
                    "ir.coalesce.left-not-null"),
                coalesceLeft,
                coalesceRight);
            return true;
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerCondition(conditionalExpression.Condition, context, out var condition) &&
            TryLowerTerm(conditionalExpression.WhenTrue, context, out var whenTrue) &&
            TryLowerTerm(conditionalExpression.WhenFalse, context, out var whenFalse) &&
            whenTrue.Kind == whenFalse.Kind)
        {
            term = new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
            context.SemanticModel.GetOperation(prefixUnary, context.CancellationToken) is
                Microsoft.CodeAnalysis.Operations.IUnaryOperation unaryOperation &&
            (unaryOperation.OperatorMethod == null ||
             unaryOperation.Type != null && SymbolicNumericLowerer.IsBigIntegerType(unaryOperation.Type)) &&
            TryLowerTerm(prefixUnary.Operand, context, out var unaryOperand) &&
            unaryOperand.Kind == SmtValueKind.Int)
        {
            var mathematicalTerm = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                new SymbolicIntegerConstantTerm(0),
                unaryOperand);
            term = TryGetBoundedIntegralRange(
                       prefixUnary,
                       unaryOperation.Type,
                       context,
                       out var minimum,
                       out var maximum) &&
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

        if (expression is BinaryExpressionSyntax binary &&
            TryGetBinaryTermOperator(binary.Kind(), out var binaryOperator) &&
            context.SemanticModel.GetOperation(binary, context.CancellationToken) is
                Microsoft.CodeAnalysis.Operations.IBinaryOperation binaryOperation &&
            (binaryOperation.OperatorMethod == null ||
             binaryOperation.Type != null && SymbolicNumericLowerer.IsBigIntegerType(binaryOperation.Type)) &&
            TryLowerTerm(binary.Left, context, out var left) &&
            TryLowerTerm(binary.Right, context, out var right) &&
            left.Kind == SmtValueKind.Int &&
            right.Kind == SmtValueKind.Int)
        {
            var mathematicalTerm = new SymbolicBinaryTerm(
                binaryOperator,
                left,
                right);
            term = IsOverflowSensitiveIntegralBinary(binary, binaryOperation) &&
                   TryGetBoundedIntegralRange(
                       binary,
                       binaryOperation.Type,
                       context,
                       out var minimum,
                       out var maximum)
                ? CreateOverflowAwareBinaryTerm(
                    mathematicalTerm,
                    minimum,
                    maximum,
                    binary,
                    "ir.numeric.binary",
                    binaryOperation.IsChecked)
                : mathematicalTerm;
            return true;
        }

        static bool TryGetBoundedIntegralRange(
            ExpressionSyntax expression,
            ITypeSymbol? operationType,
            SymbolicLoweringContext context,
            out long minimum,
            out long maximum)
        {
            if (SymbolicTypeFacts.TryGetBoundedIntegralRange(operationType, out minimum, out maximum))
                return true;

            if (context.InvocationTermTypeResolver != null)
                foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                {
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
            Microsoft.CodeAnalysis.Operations.IBinaryOperation operation)
        {
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
            SymbolicMemberLowerer.TryLowerMemberTerm(memberAccess, context, out term))
            return true;

        if (SymbolicMemberLowerer.TryLowerImplicitThisMemberTerm(expression, context, out term)) return true;

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol != null && context.TryGetSubstitution(symbol, out term)) return true;

        if ((symbol is ILocalSymbol || symbol is IParameterSymbol) &&
            TryGetSymbolType(symbol, out var symbolType) &&
            TryGetValueKind(symbolType, out var kind))
        {
            term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
            return true;
        }

        term = null!;
        return false;
    }

    internal static SymbolicTerm CreateOverflowAwareBinaryTerm(
        SymbolicBinaryTerm mathematicalTerm,
        long minimum,
        long maximum,
        SyntaxNode syntax,
        string provenance,
        bool isChecked)
    {
        // A checked expression has a value only on normal completion. On that path
        // its CLR result is the mathematical result; overflow exits by exception.
        if (isChecked) return mathematicalTerm;

        var leftInRange = CreateIntegerInRangeCondition(
            mathematicalTerm.Left,
            minimum,
            maximum,
            syntax,
            provenance + ".left");
        var rightInRange = CreateIntegerInRangeCondition(
            mathematicalTerm.Right,
            minimum,
            maximum,
            syntax,
            provenance + ".right");
        var resultInRange = CreateIntegerInRangeCondition(
            mathematicalTerm,
            minimum,
            maximum,
            syntax,
            provenance + ".result");

        // Values outside the CLR operand domain are impossible program inputs, so
        // define the extension mathematically there. Inside the domain, only the
        // true overflow branch is opaque. This preserves guarded exact proofs
        // without assigning unsound mathematical semantics to wrapped results.
        var operandsOutOfRange = new SymbolicNotCondition(
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                leftInRange,
                rightInRange));

        var modulus = unchecked((ulong)(maximum - minimum)) + 1UL;
        if ((mathematicalTerm.Operator is SymbolicBinaryTermOperator.Add or
                SymbolicBinaryTermOperator.Subtract) &&
            modulus != 0 &&
            modulus <= long.MaxValue)
        {
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
                    new SymbolicBinaryTerm(
                        SymbolicBinaryTermOperator.Add,
                        mathematicalTerm,
                        new SymbolicIntegerConstantTerm((long)modulus)),
                    mathematicalTerm));
            return new SymbolicConditionalTerm(
                operandsOutOfRange,
                mathematicalTerm,
                wrapped);
        }

        var exactBranch = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            operandsOutOfRange,
            resultInRange);
        return new SymbolicConditionalTerm(
            exactBranch,
            mathematicalTerm,
            mathematicalTerm with { MayOverflow = true });
    }
}
