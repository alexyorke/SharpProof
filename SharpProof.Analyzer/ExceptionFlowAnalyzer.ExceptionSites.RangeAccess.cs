using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsDefinitelyOutOfRangeBuiltInElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        bool requireRangeArgument)
    {
        if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken)) return false;

        var hasRangeArgument = IsBuiltInRangeAccessArgument(
            elementAccess.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken);
        if (hasRangeArgument != requireRangeArgument) return false;

        if (!SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess,
                semanticModel,
                cancellationToken,
                out var inRangeFormula))
            return false;

        return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsDefinitelyOutOfRangeArrayGetValueCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            !IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
            !TryGetArrayGetValueRuntimeArrayType(
                receiverExpression,
                invocation,
                semanticModel,
                cancellationToken,
                out var arrayType) ||
            invocationOperation.Arguments.Length != arrayType.Rank)
            return false;

        var indexExpressions = new List<ExpressionSyntax>(arrayType.Rank);
        for (var dimension = 0; dimension < arrayType.Rank; dimension++)
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, dimension,
                    out var indexExpression)) return false;

            indexExpressions.Add(indexExpression);
        }

        return SymbolicReachabilityService.TryCreateArrayGetValueIndexesInRangeFormula(
                   receiverExpression,
                   arrayType,
                   indexExpressions,
                   semanticModel,
                   cancellationToken,
                   out var inRangeFormula) &&
               IsDefinitelyFalseAtUse(invocation, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsDefinitelyOutOfRangeBuiltInSliceCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!TryTranslateBuiltInSliceCallInRangeForExceptionFlow(
                invocation,
                semanticModel,
                cancellationToken,
                out var inRangeFormula))
            return false;

        return IsDefinitelyFalseAtUse(invocation, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsDefinitelyFalseAtUse(
        SyntaxNode useNode,
        SmtFormula formula,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var outOfRangeFormula = SmtFormulaFactory.CreateNot(formula);

        var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

        return SymbolicReachabilityService.PathConditionsAllowAndImplyWithIrFirst(
            pathConditions,
            outOfRangeFormula,
            useNode,
            smtAnalysis,
            "exception.path.query",
            "exception.path.query");
    }

    private static bool IsDefinitelyTrueAtUse(
        SyntaxNode useNode,
        SmtFormula formula,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

        return SymbolicReachabilityService.PathConditionsAllowAndImplyWithIrFirst(
            pathConditions,
            formula,
            useNode,
            smtAnalysis,
            "exception.path.query",
            "exception.path.query");
    }

    private static bool IsBuiltInSequenceElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0) return false;

        var receiverTypeInfo = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken);
        var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
        if (receiverType is IArrayTypeSymbol arrayType) return arrayType.Rank == argumentCount;

        return argumentCount == 1 &&
               (receiverType?.SpecialType == SpecialType.System_String ||
                IsBuiltInSpanType(receiverType));
    }

    private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanType(typeSymbol);
    }

    private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(typeSymbol);
    }

    private static bool IsArrayGetValueInvocation(IMethodSymbol method)
    {
        return method.Name == "GetValue" &&
               !method.IsStatic &&
               method.ContainingType?.SpecialType == SpecialType.System_Array &&
               method.ReturnType.SpecialType == SpecialType.System_Object &&
               method.Parameters.Length > 0 &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    private static bool TryGetArrayGetValueRuntimeArrayType(
        ExpressionSyntax receiverExpression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        var receiverType = GetExpressionType(receiverExpression, semanticModel, cancellationToken);
        if (receiverType is IArrayTypeSymbol staticArrayType)
        {
            arrayType = staticArrayType;
            return true;
        }

        if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                receiverExpression,
                useNode,
                semanticModel,
                cancellationToken,
                out var exactRuntimeType) &&
            exactRuntimeType is IArrayTypeSymbol exactArrayType)
        {
            arrayType = exactArrayType;
            return true;
        }

        arrayType = null!;
        return false;
    }

    private static bool TryTranslateBuiltInSliceCallInRangeForExceptionFlow(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula inRangeFormula)
    {
        inRangeFormula = null!;
        if (!TryGetBuiltInSliceCallParts(
                invocation,
                semanticModel,
                cancellationToken,
                out var receiverExpression,
                out var startExpression,
                out var lengthExpression))
            return false;

        return SymbolicReachabilityService.TryCreateSubsequenceInRangeCondition(
            receiverExpression,
            startExpression,
            lengthExpression,
            semanticModel,
            cancellationToken,
            out inRangeFormula,
            true);
    }

    private static bool TryGetBuiltInSliceCallParts(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax receiverExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? lengthExpression)
    {
        receiverExpression = null!;
        startExpression = null!;
        lengthExpression = null;

        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol
            {
                Name: "Slice",
                IsStatic: false,
                Parameters.Length: >= 1 and <= 2
            } method ||
            !IsBuiltInSpanOrMemoryType(method.ContainingType) ||
            !method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TryMapInvocationArguments(invocation, method, out var arguments) ||
            arguments[0] == null)
            return false;

        receiverExpression = memberAccess.Expression;
        startExpression = arguments[0]!;
        lengthExpression = arguments.Length == 2
            ? arguments[1]
            : null;
        return lengthExpression != null || method.Parameters.Length == 1;
    }

    private static bool TryMapInvocationArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        out ExpressionSyntax?[] arguments)
    {
        arguments = new ExpressionSyntax?[method.Parameters.Length];
        var nextOrdinal = 0;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var targetOrdinal = -1;
            if (argument.NameColon != null)
            {
                var name = argument.NameColon.Name.Identifier.ValueText;
                for (var parameterIndex = 0; parameterIndex < method.Parameters.Length; parameterIndex++)
                    if (string.Equals(method.Parameters[parameterIndex].Name, name, StringComparison.Ordinal))
                    {
                        targetOrdinal = parameterIndex;
                        break;
                    }
            }
            else
            {
                while (nextOrdinal < arguments.Length && arguments[nextOrdinal] != null) nextOrdinal++;

                targetOrdinal = nextOrdinal++;
            }

            if (targetOrdinal < 0 || targetOrdinal >= arguments.Length || arguments[targetOrdinal] != null)
            {
                arguments = Array.Empty<ExpressionSyntax?>();
                return false;
            }

            arguments[targetOrdinal] = argument.Expression;
        }

        return arguments.All(static argument => argument != null);
    }

    private static bool IsBuiltInRangeAccessArgument(
        ExpressionSyntax argumentExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        argumentExpression = UnwrapFactExpression(argumentExpression);
        if (argumentExpression is RangeExpressionSyntax) return true;

        var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
        return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
    }

    private static bool StatementMutatesSymbolExceptLinearAssignment(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }
}