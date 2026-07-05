using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class BinaryPatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.BinaryPattern);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IBinaryPatternOperation binaryPatternOperation))
            {

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [BinaryPatternRule] Checking Binary Pattern: {binaryPatternOperation.Syntax}");


            var leftResult = PurityAnalysisEngine.CheckSingleOperation(binaryPatternOperation.LeftPattern, context, currentState);
            if (!leftResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [BinaryPatternRule] Left pattern is Impure: {binaryPatternOperation.LeftPattern.Syntax}");
                return leftResult;
            }

            PurityAnalysisEngine.LogDebug($"    [BinaryPatternRule] Left pattern is Pure.");


            var rightResult = PurityAnalysisEngine.CheckSingleOperation(binaryPatternOperation.RightPattern, context, currentState);
            if (!rightResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [BinaryPatternRule] Right pattern is Impure: {binaryPatternOperation.RightPattern.Syntax}");
                return rightResult;
            }


            PurityAnalysisEngine.LogDebug($"  [BinaryPatternRule] Binary Pattern is PURE.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}