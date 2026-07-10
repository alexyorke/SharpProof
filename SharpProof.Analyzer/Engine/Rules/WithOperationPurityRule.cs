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

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }




            ITypeSymbol? targetType = withOperation.Type;

            if (targetType == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);
            }


            var operandResult = PurityAnalysisEngine.CheckSingleOperation(withOperation.Operand, context, currentState);
            if (!operandResult.IsPure)
            {
                return operandResult;
            }


            if (withOperation.Initializer != null)
            {
                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(withOperation.Initializer, context, currentState);
                if (!initializerResult.IsPure)
                {
                    return initializerResult;
                }
            }


            if (targetType.IsValueType)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }
            else
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);
            }
        }
    }
}