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

                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }




            if (throwOperation.Exception != null)
            {
                var exceptionResult = PurityAnalysisEngine.CheckSingleOperation(throwOperation.Exception, context, currentState);
                if (!exceptionResult.IsPure)
                {
                    return exceptionResult;
                }
            }
            else
            {
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                operation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "throw",
                    nameof(ThrowOperationPurityRule),
                    operation));
        }
    }
}
