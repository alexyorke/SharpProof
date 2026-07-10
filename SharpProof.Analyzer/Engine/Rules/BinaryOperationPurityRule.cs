using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class BinaryOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Binary);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IBinaryOperation binaryOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }



            var leftResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.LeftOperand, context, currentState);
            if (!leftResult.IsPure)
            {
                return leftResult;
            }


            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalAnd &&
                PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out var leftAnd) &&
                !leftAnd)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
                PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out var leftOr) &&
                leftOr)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            var rightState = currentState;
            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalAnd)
            {
                if (!PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    branchWhenTrue: true,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out rightState))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }
            else if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr)
            {
                if (!PurityAnalysisEngine.TryCreateBranchAssumptionState(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    branchWhenTrue: false,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    out rightState))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }

            var rightResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.RightOperand, context, rightState);
            if (!rightResult.IsPure)
            {
                return rightResult;
            }


            if (binaryOperation.LeftOperand.Type?.TypeKind == TypeKind.Dynamic ||
                binaryOperation.RightOperand.Type?.TypeKind == TypeKind.Dynamic ||
                binaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    binaryOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(BinaryOperationPurityRule),
                        binaryOperation,
                        syntaxNode: binaryOperation.Syntax));
            }


            if (binaryOperation.OperatorMethod != null)
            {
                var operatorMethod = binaryOperation.OperatorMethod;
                if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.UserDefinedOperator))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        binaryOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(BinaryOperationPurityRule),
                            binaryOperation,
                            symbol: operatorMethod));
                }

                var operatorPurity = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);

                if (!operatorPurity.IsPure)
                {
                    return operatorPurity.WithCallee(operatorMethod, binaryOperation.Syntax);
                }

            }


            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
