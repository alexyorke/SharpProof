using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class TypePatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.TypePattern,
            OperationKind.IsType);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            return PatternPurityRuleHelpers.CheckChildOperationsArePure(operation, context, currentState);
        }
    }
}
