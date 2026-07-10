using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class UnaryOperationPurityRule : IPurityRule
    {

        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Unary);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IUnaryOperation unaryOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }



            var operandResult = PurityAnalysisEngine.CheckSingleOperation(unaryOperation.Operand, context, currentState);
            if (!operandResult.IsPure)
            {
                return operandResult;
            }


            if (unaryOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
                unaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    unaryOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(UnaryOperationPurityRule),
                        unaryOperation,
                        syntaxNode: unaryOperation.Syntax));
            }


            if (unaryOperation.OperatorMethod != null)
            {
                var operatorMethod = unaryOperation.OperatorMethod;
                if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.UserDefinedOperator))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        unaryOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(UnaryOperationPurityRule),
                            unaryOperation,
                            symbol: operatorMethod));
                }

                var operatorPurity = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);

                if (!operatorPurity.IsPure)
                {
                    return operatorPurity.WithCallee(operatorMethod, unaryOperation.Syntax);
                }

            }


            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
