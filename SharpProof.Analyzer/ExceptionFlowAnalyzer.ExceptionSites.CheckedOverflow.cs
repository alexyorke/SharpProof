using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsDefinitelyCheckedIntegralOverflow(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return TryGetCheckedIntegralBinaryOperator(binaryExpression, semanticModel, cancellationToken,
                   out var smtOperator, out var minValue, out var maxValue) &&
               TryClassifyOutsideIntegralRangeAtUse(
                   binaryExpression,
                   SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
                       binaryExpression.Left,
                       binaryExpression.Right,
                       smtOperator,
                       minValue,
                       maxValue,
                       binaryExpression,
                       new SymbolicLoweringContext(semanticModel, cancellationToken)),
                   semanticModel,
                   cancellationToken,
                   smtAnalysis,
                   out var isOverflow) &&
               isOverflow;
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (TryGetCheckedIntegralUnaryOperator(unaryExpression, semanticModel, cancellationToken, out var minValue,
                out var maxValue) &&
            TryClassifyOutsideIntegralRangeAtUse(
                unaryExpression,
                SymbolicSemanticPipeline.LowerNegatedIntegerInRangeCondition(
                    unaryExpression.Operand,
                    minValue,
                    maxValue,
                    new SymbolicLoweringContext(semanticModel, cancellationToken)),
                semanticModel,
                cancellationToken,
                smtAnalysis,
                out var isOverflow))
            return isOverflow;

        return IsDefinitelyCheckedIncrementOrDecrementOverflow(
            unaryExpression,
            unaryExpression.Operand,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        PostfixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return IsDefinitelyCheckedIncrementOrDecrementOverflow(
            unaryExpression,
            unaryExpression.Operand,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsDefinitelyCheckedIncrementOrDecrementOverflow(
        ExpressionSyntax updateExpression,
        ExpressionSyntax operand,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return TryGetCheckedIntegralIncrementOrDecrementOperator(
                   updateExpression,
                   operand,
                   semanticModel,
                   cancellationToken,
                   out var smtOperator,
                   out var minValue,
                   out var maxValue) &&
               TryClassifyOutsideIntegralRangeAtUse(
                   updateExpression,
                   SymbolicSemanticPipeline.LowerIntegerUpdateInRangeCondition(
                       operand,
                       smtOperator,
                       minValue,
                       maxValue,
                       new SymbolicLoweringContext(semanticModel, cancellationToken)),
                   semanticModel,
                   cancellationToken,
                   smtAnalysis,
                   out var isOverflow) &&
               isOverflow;
    }

    private static bool IsDefinitelyCheckedIntegralOverflow(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return TryGetCheckedExplicitNumericConversionRange(castExpression, semanticModel, cancellationToken,
                   out var minValue, out var maxValue) &&
               TryClassifyOutsideIntegralRangeAtUse(
                   castExpression,
                   SymbolicSemanticPipeline.LowerIntegerInRangeCondition(
                       castExpression.Expression,
                       minValue,
                       maxValue,
                       new SymbolicLoweringContext(semanticModel, cancellationToken)),
                   semanticModel,
                   cancellationToken,
                   smtAnalysis,
                   out var isOverflow) &&
               isOverflow;
    }

    private static bool TryClassifyOutsideIntegralRangeAtUse(
        SyntaxNode useNode,
        SymbolicLoweringResult<SymbolicCondition> lowering,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        out bool isOutsideRange)
    {
        if (lowering is not { IsExact: true, Value: { } inRangeCondition })
        {
            isOutsideRange = false;
            return false;
        }

        isOutsideRange = IsDefinitelyFalseAtUse(
            useNode,
            inRangeCondition,
            semanticModel,
            cancellationToken,
            smtAnalysis);
        return true;
    }

    private static bool IsDefinitelyNegativeArrayLength(
        ArrayCreationExpressionSyntax arrayCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var lengthExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            var lowering = SymbolicSemanticPipeline.LowerNegativeIntegerCondition(
                    lengthExpression,
                    new SymbolicLoweringContext(semanticModel, cancellationToken));
            if (lowering is not { IsExact: true, Value: { } negativeLength })
                continue;

            if (IsDefinitelyTrueAtUse(arrayCreation, negativeLength, semanticModel, cancellationToken, smtAnalysis))
                return true;
        }

        return false;
    }

    private static bool TryGetCheckedIntegralBinaryOperator(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (!TryGetCheckedIntegralRange(binaryExpression, semanticModel, cancellationToken, out minValue,
                out maxValue) ||
            semanticModel.GetOperation(binaryExpression, cancellationToken) is not IBinaryOperation
            {
                IsChecked: true,
                OperatorMethod: null
            })
            return false;

        switch (binaryExpression.Kind())
        {
            case SyntaxKind.AddExpression:
                smtOperator = SmtIntegerBinaryOperator.Add;
                return true;
            case SyntaxKind.SubtractExpression:
                smtOperator = SmtIntegerBinaryOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyExpression:
                smtOperator = SmtIntegerBinaryOperator.Multiply;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCheckedIntegralUnaryOperator(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;
        return unaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) &&
               TryGetCheckedIntegralRange(unaryExpression, semanticModel, cancellationToken, out minValue,
                   out maxValue) &&
               semanticModel.GetOperation(unaryExpression, cancellationToken) is IUnaryOperation
               {
                   IsChecked: true,
                   OperatorMethod: null
               };
    }

    private static bool TryGetCheckedIntegralIncrementOrDecrementOperator(
        ExpressionSyntax updateExpression,
        ExpressionSyntax operand,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        var operandType = semanticModel.GetTypeInfo(operand, cancellationToken).Type;
        if (!TryGetBoundedIntegralRange(operandType, out minValue, out maxValue) ||
            semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
            {
                IsChecked: true,
                OperatorMethod: null
            })
            return false;

        switch (updateExpression.Kind())
        {
            case SyntaxKind.PreIncrementExpression:
            case SyntaxKind.PostIncrementExpression:
                smtOperator = SmtIntegerBinaryOperator.Add;
                return true;
            case SyntaxKind.PreDecrementExpression:
            case SyntaxKind.PostDecrementExpression:
                smtOperator = SmtIntegerBinaryOperator.Subtract;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCheckedExplicitNumericConversionRange(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;

        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            !conversionOperation.IsChecked ||
            !conversionOperation.Conversion.Exists ||
            conversionOperation.Conversion.IsIdentity ||
            conversionOperation.Conversion.IsImplicit ||
            !conversionOperation.Conversion.IsNumeric ||
            conversionOperation.Conversion.IsUserDefined ||
            conversionOperation.Conversion.MethodSymbol != null ||
            !TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression, semanticModel, cancellationToken),
                out minValue,
                out maxValue))
            return false;

        if (TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel,
                    cancellationToken),
                out var sourceMinValue,
                out var sourceMaxValue) &&
            sourceMinValue >= minValue &&
            sourceMaxValue <= maxValue)
            return false;

        return true;
    }

    private static bool TryGetCheckedIntegralRange(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
    }

    private static bool TryGetCheckedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    private static bool TryGetBoundedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetBoundedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    private static bool TryGetCheckedNumericConversionRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
    }
}
