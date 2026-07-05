using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class TuplePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Tuple);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ITupleOperation tupleOperation))
            {
                PurityAnalysisEngine.LogDebug($"    [TupleRule] WARNING: Not an ITupleOperation ({operation.Kind}). Assuming pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"    [TupleRule] Checking Tuple operation ({operation.Syntax})...");


            foreach (var element in tupleOperation.Elements)
            {
                PurityAnalysisEngine.LogDebug($"    [TupleRule] Checking element: {element.Syntax} ({element.Kind})");
                var elementResult = PurityAnalysisEngine.CheckSingleOperation(element, context, currentState);
                if (!elementResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [TupleRule] Element is IMPURE. Tuple creation is Impure.");
                    return elementResult;
                }
            }

            PurityAnalysisEngine.LogDebug($"    [TupleRule] Tuple operation ({operation.Syntax}) - All elements Pure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}