using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class BinaryOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Binary);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IBinaryOperation binaryOperation))
            {
                PurityAnalysisEngine.LogDebug($"  [BinaryOpRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [BinaryOpRule] Checking Binary Operation: {binaryOperation.Syntax} (Operator: {binaryOperation.OperatorKind})");


            var leftResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.LeftOperand, context, currentState);
            if (!leftResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Left Operand is Impure: {binaryOperation.LeftOperand.Syntax}");
                return leftResult;
            }

            PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Left Operand is Pure.");

            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalAnd &&
                binaryOperation.LeftOperand.ConstantValue.HasValue &&
                binaryOperation.LeftOperand.ConstantValue.Value is bool leftAnd &&
                !leftAnd)
            {
                PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Constant false && skips right operand. Binary operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
                binaryOperation.LeftOperand.ConstantValue.HasValue &&
                binaryOperation.LeftOperand.ConstantValue.Value is bool leftOr &&
                leftOr)
            {
                PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Constant true || skips right operand. Binary operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            var rightResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.RightOperand, context, currentState);
            if (!rightResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Right Operand is Impure: {binaryOperation.RightOperand.Syntax}");
                return rightResult;
            }

            PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Right Operand is Pure.");

            if (binaryOperation.LeftOperand.Type?.TypeKind == TypeKind.Dynamic ||
                binaryOperation.RightOperand.Type?.TypeKind == TypeKind.Dynamic ||
                binaryOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
                PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Dynamic binary operation detected. Conservatively treating as Impure.");
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
                if (IsStaticAbstractInterfaceOperator(operatorMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Static abstract interface operator '{operatorMethod.Name}' has unresolved dispatch targets. Binary operation is Impure.");
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
                    PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] User-defined operator method '{operatorMethod.Name}' is IMPURE. Binary operation is Impure.");
                    return operatorPurity.WithCallee(operatorMethod, binaryOperation.Syntax);
                }

                PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] User-defined operator method '{operatorMethod.Name}' is Pure.");
            }


            PurityAnalysisEngine.LogDebug($"    [BinaryOpRule] Binary operation is Pure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsStaticAbstractInterfaceOperator(IMethodSymbol methodSymbol)
        {
            return methodSymbol.IsStatic &&
                methodSymbol.IsAbstract &&
                methodSymbol.MethodKind == MethodKind.UserDefinedOperator &&
                methodSymbol.ContainingType?.TypeKind == TypeKind.Interface;
        }
    }
}
