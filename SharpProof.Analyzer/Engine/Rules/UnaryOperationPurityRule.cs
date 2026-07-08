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
                PurityAnalysisEngine.LogDebug($"  [UnaryOpRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [UnaryOpRule] Checking Unary Operation: {unaryOperation.Syntax} (Operator: {unaryOperation.OperatorKind})");


            var operandResult = PurityAnalysisEngine.CheckSingleOperation(unaryOperation.Operand, context, currentState);
            if (!operandResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] Operand is Impure: {unaryOperation.Operand.Syntax}");
                return operandResult;
            }

            PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] Operand is Pure.");

            if (unaryOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
                unaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
                PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] Dynamic unary operation detected. Conservatively treating as Impure.");
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
                    PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] Static abstract interface operator '{operatorMethod.Name}' has unresolved dispatch targets. Unary operation is Impure.");
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
                    PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] User-defined operator method '{operatorMethod.Name}' is IMPURE. Unary operation is Impure.");
                    return operatorPurity.WithCallee(operatorMethod, unaryOperation.Syntax);
                }

                PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] User-defined operator method '{operatorMethod.Name}' is Pure.");
            }


            PurityAnalysisEngine.LogDebug($"    [UnaryOpRule] Unary operation is Pure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
