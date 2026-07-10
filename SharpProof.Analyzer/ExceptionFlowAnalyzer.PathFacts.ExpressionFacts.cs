using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static ISymbol? GetLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
            UnwrapFactExpression(expression),
            semanticModel,
            cancellationToken,
            out var symbol)
            ? symbol
            : null;
    }

    private static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private static bool ExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
        return expressionSymbol != null && SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
    }

    private static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            if (node is ExpressionSyntax expression &&
                ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool ExpressionReferencesAnySymbol(
        SyntaxNode root,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in symbols)
            if (ExpressionReferencesSymbol(root, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool ExpressionMatchesFact(
        ExpressionSyntax expression,
        PathFactKind factKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapFactExpression(expression);
        if (factKind == PathFactKind.Null)
            return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                   IsDefaultReferenceExpression(expression, semanticModel, cancellationToken);

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        return (constantValue.HasValue && SymbolicValueFacts.IsIntegralOrDecimalZero(constantValue.Value)) ||
               IsDefaultIntegralExpression(expression, semanticModel, cancellationToken);
    }

    private static bool IsDefaultReferenceExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsDefaultExpressionSyntax(expression) &&
               IsReferenceType(GetExpressionType(expression, semanticModel, cancellationToken));
    }

    private static bool IsDefaultIntegralExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var type = GetExpressionType(expression, semanticModel, cancellationToken);
        return IsDefaultExpressionSyntax(expression) &&
               type != null &&
               SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(type);
    }

    private static bool IsDefaultExpressionSyntax(ExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression is DefaultExpressionSyntax;
    }

    private static ITypeSymbol? GetExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.ConvertedType ?? typeInfo.Type;
    }

    private static bool TryCreateFactFormula(
        ISymbol symbol,
        PathFactKind factKind,
        out SmtFormula? factFormula)
    {
        if (factKind == PathFactKind.Null)
            return SymbolicReachabilityService.TryCreateSymbolReferenceNullComparison(
                symbol,
                true,
                out factFormula);

        return SymbolicReachabilityService.TryCreateSymbolNumericZeroComparison(
            symbol,
            out factFormula);
    }

    private static bool TryCreateFactFormula(
        ExpressionSyntax expression,
        PathFactKind factKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? factFormula)
    {
        factFormula = null;
        if (factKind == PathFactKind.Null)
        {
            if (SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    true,
                    out var nullFormula))
            {
                factFormula = nullFormula;
                return true;
            }

            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            return symbol != null && TryCreateFactFormula(symbol, factKind, out factFormula);
        }

        if (SymbolicReachabilityService.TryCreateExpressionNumericZeroComparison(
                expression,
                semanticModel,
                cancellationToken,
                out var zeroFormula))
        {
            factFormula = zeroFormula;
            return true;
        }

        var fallbackSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
        return fallbackSymbol != null && TryCreateFactFormula(fallbackSymbol, factKind, out factFormula);
    }

    private static void TryAddPathCondition(
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        SymbolicReachabilityService.TryAddBranchConditionFacts(
            condition,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            pathConditions);
    }

    private static void TryAddReferenceNullCondition(
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        if (TryGetSyntacticReferenceNullState(expression, semanticModel, cancellationToken, out var isDefinitelyNull))
        {
            if (isNull != isDefinitelyNull) SymbolicReachabilityService.AddUnsatisfiablePathCondition(pathConditions);

            return;
        }

        if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                expression,
                semanticModel,
                cancellationToken,
                isNull,
                out var formula))
            return;

        pathConditions.Add(formula);
    }

    private static bool TryGetSyntacticReferenceNullState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool isNull)
    {
        expression = UnwrapFactExpression(expression);
        if (expression is CastExpressionSyntax castExpression)
        {
            if (!IsReferenceType(GetExpressionType(castExpression, semanticModel, cancellationToken)))
            {
                isNull = false;
                return false;
            }

            return TryGetSyntacticReferenceNullState(castExpression.Expression, semanticModel, cancellationToken,
                out isNull);
        }

        if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            IsDefaultReferenceExpression(expression, semanticModel, cancellationToken))
        {
            isNull = true;
            return true;
        }

        var expressionType = GetExpressionType(expression, semanticModel, cancellationToken);
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
        {
            if (constantValue.Value == null && IsReferenceType(expressionType))
            {
                isNull = true;
                return true;
            }

            if (constantValue.Value is string)
            {
                isNull = false;
                return true;
            }
        }

        if (IsSyntacticallyNonNullReferenceExpression(expression, expressionType))
        {
            isNull = false;
            return true;
        }

        isNull = false;
        return false;
    }

    private static bool IsSyntacticallyNonNullReferenceExpression(
        ExpressionSyntax expression,
        ITypeSymbol? expressionType)
    {
        if (!IsReferenceType(expressionType)) return false;

        return expression switch
        {
            ThisExpressionSyntax => true,
            BaseExpressionSyntax => true,
            ObjectCreationExpressionSyntax => true,
            ImplicitObjectCreationExpressionSyntax => true,
            AnonymousObjectCreationExpressionSyntax => true,
            ArrayCreationExpressionSyntax => true,
            ImplicitArrayCreationExpressionSyntax => true,
            InterpolatedStringExpressionSyntax => true,
            TypeOfExpressionSyntax => true,
            CollectionExpressionSyntax when expressionType is IArrayTypeSymbol => true,
            _ => false
        };
    }

    private static void TryAddCoalesceRightPathCondition(
        ExpressionSyntax leftExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> pathConditions)
    {
        var originalCount = pathConditions.Count;
        TryAddReferenceNullCondition(leftExpression, true, semanticModel, cancellationToken, pathConditions);
        if (pathConditions.Count != originalCount) return;

        leftExpression = UnwrapFactExpression(leftExpression);
        if (leftExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
            ConditionalAccessFallbackRequiresNullReceiver(conditionalAccess, semanticModel, cancellationToken))
            TryAddReferenceNullCondition(conditionalAccess.Expression, true, semanticModel, cancellationToken,
                pathConditions);
    }

    private static bool ConditionalAccessFallbackRequiresNullReceiver(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var whenNotNullType = GetConditionalAccessWhenNotNullType(
            conditionalAccess.WhenNotNull,
            semanticModel,
            cancellationToken);
        return IsKnownNonNullableValueType(whenNotNullType);
    }

    private static ITypeSymbol? GetConditionalAccessWhenNotNullType(
        ExpressionSyntax whenNotNullExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(whenNotNullExpression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type != null) return type;

        var symbol = semanticModel.GetSymbolInfo(whenNotNullExpression, cancellationToken).Symbol;
        return symbol switch
        {
            IFieldSymbol fieldSymbol => fieldSymbol.Type,
            IPropertySymbol propertySymbol => propertySymbol.Type,
            IEventSymbol eventSymbol => eventSymbol.Type,
            IMethodSymbol methodSymbol => methodSymbol.ReturnType,
            _ => null
        };
    }

    private static bool IsKnownNonNullableValueType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.IsValueType == true &&
               typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }
}