using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class ThrowOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Throw);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IThrowOperation throwOperation))
            {

                PurityAnalysisEngine.LogDebug($"  [ThrowRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }




            if (throwOperation.Exception != null)
            {
                PurityAnalysisEngine.LogDebug($"    [ThrowRule] Checking exception expression: {throwOperation.Exception.Syntax} ({throwOperation.Exception.Kind})");
                var exceptionResult = PurityAnalysisEngine.CheckSingleOperation(throwOperation.Exception, context, currentState);
                if (!exceptionResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ThrowRule] Exception expression is IMPURE. Throw is Impure.");
                    return exceptionResult;
                }
                PurityAnalysisEngine.LogDebug($"    [ThrowRule] Exception expression is PURE.");
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"  [ThrowRule] Found re-throw operation (exception is null). Treating throw as impure.");
            }

            PurityAnalysisEngine.LogDebug($"  [ThrowRule] Throw operation is IMPURE.");
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                operation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "throw",
                    nameof(ThrowOperationPurityRule),
                    operation));
        }
    }
}
