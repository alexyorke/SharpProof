using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class ChildOperationsPurityRule : IPurityRule
    {
        private readonly ImmutableArray<OperationKind> _applicableOperationKinds;

        public ChildOperationsPurityRule(params OperationKind[] applicableOperationKinds)
        {
            _applicableOperationKinds = ImmutableArray.Create(applicableOperationKinds);
        }

        public IEnumerable<OperationKind> ApplicableOperationKinds => _applicableOperationKinds;

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            return CheckChildOperationsArePure(operation, context, currentState);
        }

        internal static PurityAnalysisEngine.PurityAnalysisResult CheckChildOperationsArePure(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            foreach (var child in operation.ChildOperations)
            {
                var childResult = PurityAnalysisEngine.CheckSingleOperation(child, context, currentState);
                if (!childResult.IsPure)
                {
                    return childResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
