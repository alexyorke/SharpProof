using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    public static bool TryLowerCondition(
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

        if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
            TryLowerCondition(prefixUnary.Operand, context, out var operand))
        {
            condition = new SymbolicNotCondition(operand);
            return true;
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerBooleanConditional(
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
                TryLowerTypeTestCondition(binaryExpression.Left, typeSyntax, binaryExpression, false, context,
                    out condition))
                return true;

            if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                TryLowerCondition(binaryExpression.Left, context, out var leftAnd) &&
                TryLowerCondition(binaryExpression.Right, context, out var rightAnd))
            {
                condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, leftAnd, rightAnd);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                TryLowerCondition(binaryExpression.Left, context, out var leftOr) &&
                TryLowerCondition(binaryExpression.Right, context, out var rightOr))
            {
                condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, leftOr, rightOr);
                return true;
            }

            if (TryLowerBuiltInBooleanBitwiseCondition(binaryExpression, context, out condition)) return true;

            if (TryLowerTypeOfComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerUnsignedCastBoundsComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerCheckedIntegralConversionComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerDecimalZeroComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerNotNullIfNotNullNullComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerNullableNullComparisonCondition(binaryExpression, context, out condition)) return true;

            if (TryLowerRegexMatchesCountComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerStringSearchComparison(binaryExpression, context, out condition)) return true;

            if (TryLowerPrefixSubstringComparison(binaryExpression, context, out condition)) return true;

            if (IsEqualityExpression(binaryExpression) &&
                TryLowerStringEqualityCondition(binaryExpression, context, out condition))
                return true;

            if (IsEqualityExpression(binaryExpression) &&
                TryLowerTupleEqualityCondition(binaryExpression, context, out condition))
                return true;

            if (TryGetRelationOperator(binaryExpression.Kind(), out var nullableRelationOperator) &&
                TryLowerNullableValueAccessRelationCondition(
                    binaryExpression,
                    nullableRelationOperator,
                    context,
                    out condition))
                return true;

            if (TryGetRelationOperator(binaryExpression.Kind(), out nullableRelationOperator) &&
                TryLowerNullableRelationCondition(
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
            (TryLowerNullablePatternCondition(isPatternExpression, context, out condition) ||
             (TryLowerTerm(isPatternExpression.Expression, context, out var patternValue) &&
              TryLowerPatternCondition(
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
             TryLowerBinaryPatternCondition(isPatternExpression, context, out condition) ||
             TryLowerNullPatternCondition(isPatternExpression, context, out condition) ||
             TryLowerConstantPatternCondition(isPatternExpression, context, out condition) ||
             TryLowerRelationalPatternCondition(isPatternExpression, context, out condition) ||
             TryLowerEmptyRecursivePatternCondition(isPatternExpression, context, out condition) ||
             TryLowerTypePatternCondition(isPatternExpression, context, out condition) ||
             TryLowerUnaryPatternCondition(isPatternExpression.Expression, isPatternExpression.Pattern, context,
                 out condition)))
            return true;

        if (TryLowerRegexMatchSuccessCondition(expression, context, out condition)) return true;

        if (expression is InvocationExpressionSyntax sourceInvocation &&
            TryLowerSourceBooleanInvocation(sourceInvocation, context, out condition))
            return true;

        if (expression is InvocationExpressionSyntax knownInvocation &&
            TryLowerKnownApiInvocation(knownInvocation, context, out condition))
            return true;

        if (expression is MemberAccessExpressionSyntax sourceBooleanProperty &&
            context.SemanticModel.GetSymbolInfo(sourceBooleanProperty, context.CancellationToken).Symbol is
                IPropertySymbol sourceBooleanPropertySymbol &&
            TryLowerSourceBooleanPropertyCondition(
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

    public static bool TryLowerTerm(
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

        if (TryLowerDefaultValueTerm(expression, context, out term)) return true;

        if (expression is CheckedExpressionSyntax checkedExpression &&
            checkedExpression.IsKind(SyntaxKind.CheckedExpression) &&
            TryLowerTerm(checkedExpression.Expression, context, out term))
            return true;

        if (TryGetKnownCompletedAsyncResultExpression(expression, context, out var completedResultExpression) &&
            TryLowerTerm(completedResultExpression, context, out term))
            return true;

        if (TryLowerSupportedConversionTerm(expression, context, out term)) return true;

        if (expression is ThisExpressionSyntax)
        {
            term = context.ImplicitThis;
            return true;
        }

        if (TryLowerStringExpressionTerm(expression, context, out term)) return true;

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
            TryLowerNullableCoalesceValueTerm(nullableCoalesceExpression, context, out term))
            return true;

        if (expression is AssignmentExpressionSyntax coalesceAssignment &&
            coalesceAssignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
            TryLowerCoalesceAssignmentTerm(coalesceAssignment, context, out term))
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
            TryLowerTerm(prefixUnary.Operand, context, out var unaryOperand) &&
            unaryOperand.Kind == SmtValueKind.Int)
        {
            term = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                new SymbolicIntegerConstantTerm(0),
                unaryOperand);
            return true;
        }

        if (expression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression) &&
            TryLowerReferenceAsTerm(asExpression, context, out term))
            return true;

        if (expression is BinaryExpressionSyntax binary &&
            TryGetBinaryTermOperator(binary.Kind(), out var binaryOperator) &&
            TryLowerTerm(binary.Left, context, out var left) &&
            TryLowerTerm(binary.Right, context, out var right) &&
            left.Kind == SmtValueKind.Int &&
            right.Kind == SmtValueKind.Int)
        {
            term = new SymbolicBinaryTerm(binaryOperator, left, right);
            return true;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            TryLowerMemberTerm(memberAccess, context, out term))
            return true;

        if (TryLowerImplicitThisMemberTerm(expression, context, out term)) return true;

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
}
