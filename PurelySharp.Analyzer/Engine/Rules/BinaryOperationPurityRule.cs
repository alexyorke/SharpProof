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
                PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    context.SmtAnalysis,
                    out var leftAnd) &&
                !leftAnd)
            {
                PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Proven false && skips right operand. Binary operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (binaryOperation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
                PurityAnalysisEngine.TryGetKnownConditionValueFromPathFacts(
                    currentState,
                    binaryOperation.LeftOperand,
                    context.SemanticModel,
                    context.SmtAnalysis,
                    out var leftOr) &&
                leftOr)
            {
                PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Proven true || skips right operand. Binary operation is Pure.");
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
                    out rightState))
                {
                    PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Proven unreachable && right operand. Binary operation is Pure.");
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
                    out rightState))
                {
                    PurityAnalysisEngine.LogDebug("    [BinaryOpRule] Proven unreachable || right operand. Binary operation is Pure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }

            var rightResult = PurityAnalysisEngine.CheckSingleOperation(binaryOperation.RightOperand, context, rightState);
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
