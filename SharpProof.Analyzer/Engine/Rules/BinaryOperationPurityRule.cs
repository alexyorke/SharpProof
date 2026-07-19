namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class BinaryOperationPurityRule : PurityRuleBase<IBinaryOperation>
{
    protected override OperationKind Kind => OperationKind.Binary;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IBinaryOperation binaryOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var leftResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.LeftOperand, context, currentState);
        if (!leftResult.IsPure) return leftResult;


        if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalAnd &&
            PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                currentState,
                binaryOperation.LeftOperand,
                context.SemanticModel,
                context.SmtAnalysis,
                context.CancellationToken,
                out var leftAnd) &&
            !leftAnd)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
            PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                currentState,
                binaryOperation.LeftOperand,
                context.SemanticModel,
                context.SmtAnalysis,
                context.CancellationToken,
                out var leftOr) &&
            leftOr)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        var rightState = currentState;
        if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalAnd)
        {
            if (!PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    true,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out rightState))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
        else if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr)
        {
            if (!PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    false,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out rightState))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        var rightResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.RightOperand, context, rightState);
        if (!rightResult.IsPure) return rightResult;


        if (binaryOperation.LeftOperand.Type?.TypeKind == TypeKind.Dynamic ||
            binaryOperation.RightOperand.Type?.TypeKind == TypeKind.Dynamic ||
            binaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            return PurityAnalysisEngine.ImpureResult(
                binaryOperation,
                "dynamic_dispatch",
                nameof(BinaryOperationPurityRule));


        if (binaryOperation.OperatorMethod != null)
        {
            var operatorMethod = binaryOperation.OperatorMethod;
            if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.UserDefinedOperator))
                return PurityAnalysisEngine.ImpureResult(
                    binaryOperation,
                    "unknown_external_call",
                    nameof(BinaryOperationPurityRule),
                    operatorMethod);

            var operatorPurity = PurityCalleeResolver.GetCalleePurityAtUse(operatorMethod, binaryOperation.Syntax, context);
            if (!operatorPurity.IsPure) return operatorPurity;
        }


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
