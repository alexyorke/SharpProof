using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class WithOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.With);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IWithOperation withOperation))
            {

                PurityAnalysisEngine.LogDebug($"[WithRule] Warning: Incorrect operation type {operation.Kind}.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }




            ITypeSymbol? targetType = withOperation.Type;

            if (targetType == null)
            {
                PurityAnalysisEngine.LogDebug($"[WithRule] Could not determine type for 'with' expression. Assuming impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);
            }


            PurityAnalysisEngine.LogDebug($"    [WithRule] Checking operand: {withOperation.Operand.Syntax} ({withOperation.Operand.Kind})");
            var operandResult = PurityAnalysisEngine.CheckSingleOperation(withOperation.Operand, context, currentState);
            if (!operandResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [WithRule] Operand is IMPURE. 'with' expression is Impure.");
                return operandResult;
            }


            if (withOperation.Initializer != null)
            {
                PurityAnalysisEngine.LogDebug($"    [WithRule] Checking initializer: {withOperation.Initializer.Syntax} ({withOperation.Initializer.Kind})");
                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(withOperation.Initializer, context, currentState);
                if (!initializerResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [WithRule] Initializer is IMPURE. 'with' expression is Impure.");
                    return initializerResult;
                }
            }


            if (targetType.IsValueType)
            {
                PurityAnalysisEngine.LogDebug($"[WithRule] 'with' expression on value type '{targetType.ToDisplayString()}' with pure children. Result: Pure");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"[WithRule] 'with' expression on reference type '{targetType.ToDisplayString()}' with pure children. Result: Impure (Object Creation)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);
            }
        }
    }
}