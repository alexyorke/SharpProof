namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class UnaryOperationPurityRule : PurityRuleBase<IUnaryOperation>
{
    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IUnaryOperation unaryOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var operandResult = PurityAnalysisEngine.CheckSingleOperation(unaryOperation.Operand, context, currentState);
        if (!operandResult.IsPure) return operandResult;


        if (unaryOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
            unaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            return PurityAnalysisEngine.ImpureResult(
                unaryOperation,
                "dynamic_dispatch",
                nameof(UnaryOperationPurityRule));


        if (unaryOperation.OperatorMethod != null)
        {
            var operatorMethod = unaryOperation.OperatorMethod;
            if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.UserDefinedOperator))
                return PurityAnalysisEngine.ImpureResult(
                    unaryOperation,
                    "unknown_external_call",
                    nameof(UnaryOperationPurityRule),
                    operatorMethod);

            var operatorPurity = PurityCalleeResolver.GetCalleePurityAtUse(operatorMethod, unaryOperation.Syntax, context);
            if (!operatorPurity.IsPure) return operatorPurity;
        }


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
