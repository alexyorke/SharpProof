using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
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

        var lowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessOutOfRangeCondition(
            elementAccess,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } outOfRangeCondition })
            return false;

        return IsDefinitelyTrueAtUse(
            elementAccess,
            outOfRangeCondition,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsDefinitelyOutOfRangeArrayGetValueCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            !SymbolicRuntimeHazardSyntaxFacts.IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
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

        var lowering = SymbolicSemanticPipeline.LowerArrayElementBoundsCondition(
            receiverExpression,
            indexExpressions,
            invocation,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        return lowering is { IsExact: true, Value: { } inRangeCondition } &&
               IsDefinitelyFalseAtUse(
                   invocation,
                   inRangeCondition,
                   semanticModel,
                   cancellationToken,
                   smtAnalysis);
    }

    private static bool IsDefinitelyOutOfRangeBuiltInSliceCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (!TryLowerBuiltInSliceCallInRangeForExceptionFlow(
                invocation,
                semanticModel,
                cancellationToken,
                out var inRangeCondition))
            return false;

        return IsDefinitelyFalseAtUse(invocation, inRangeCondition, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool IsDefinitelyFalseAtUse(
        SyntaxNode useNode,
        SymbolicCondition condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathState = ExceptionPathStateService.CollectPathStateForUse(
            useNode,
            semanticModel,
            cancellationToken);
        return SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis)
                   .Info.Status == SymbolicProofStatus.ProvenFalse;
    }

    private static bool IsDefinitelyTrueAtUse(
        SyntaxNode useNode,
        SymbolicCondition condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathState = ExceptionPathStateService.CollectPathStateForUse(
            useNode,
            semanticModel,
            cancellationToken);
        return SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis)
                   .Info.Status == SymbolicProofStatus.ProvenTrue;
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

    private static bool TryLowerBuiltInSliceCallInRangeForExceptionFlow(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition inRangeCondition)
    {
        inRangeCondition = null!;
        if (!TryGetBuiltInSliceCallParts(
                invocation,
                semanticModel,
                cancellationToken,
                out var receiverExpression,
                out var startExpression,
                out var lengthExpression))
            return false;

        var lowering = SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition(
            receiverExpression,
            startExpression,
            lengthExpression,
            invocation,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } condition }) return false;

        inRangeCondition = condition;
        return true;
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
        return SymbolMutationFacts.ContainsMutation(
            statement,
            symbol,
            semanticModel,
            cancellationToken);
    }
}
