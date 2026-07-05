using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

using System.Collections.Generic;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class IsPatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => new[] { OperationKind.IsPattern };



        public PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisState currentState)
        {







            var isPatternOperation = (IIsPatternOperation)operation;


            PurityAnalysisResult inputPurity = CheckSingleOperation(isPatternOperation.Value, context, currentState);
            if (!inputPurity.IsPure)
            {

                LogDebug($"    [IsPatternRule] Input expression '{isPatternOperation.Value.Syntax?.ToString() ?? "N/A"}' is impure.");
                return inputPurity;
            }














            PurityAnalysisResult patternPurity = CheckSingleOperation(isPatternOperation.Pattern, context, currentState);
            if (!patternPurity.IsPure)
            {
                LogDebug($"    [IsPatternRule] Pattern expression '{isPatternOperation.Pattern.Syntax?.ToString() ?? "N/A"}' is impure.");
                return patternPurity;
            }



            LogDebug($"    [IsPatternRule] Assuming pattern itself is pure, input was pure. Syntax: '{operation.Syntax?.ToString() ?? "N/A"}'");

            return PurityAnalysisResult.Pure;
        }
    }
}