using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class SwitchCasePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.CaseClause);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {

            foreach (var child in operation.ChildOperations)
            {
                var childResult = PurityAnalysisEngine.CheckSingleOperation(child, context, currentState);
                if (!childResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [SwitchCaseRule] CaseClause child operation is impure: {child.Syntax}");
                    return childResult;
                }
            }

            PurityAnalysisEngine.LogDebug($"    [SwitchCaseRule] CaseClause operation ({operation.Syntax}) - Pure");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
