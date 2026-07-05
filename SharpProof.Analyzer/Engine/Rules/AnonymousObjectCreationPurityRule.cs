using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class AnonymousObjectCreationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.AnonymousObjectCreation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IAnonymousObjectCreationOperation anonymousObjectCreationOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var initializer in anonymousObjectCreationOperation.Initializers)
            {
                if (initializer is ISimpleAssignmentOperation assignment)
                {
                    if (assignment.Value == null)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(assignment.Syntax);
                    }

                    var valueResult = PurityAnalysisEngine.CheckSingleOperation(assignment.Value, context, currentState);
                    if (!valueResult.IsPure)
                    {
                        return valueResult;
                    }

                    continue;
                }

                var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initializer, context, currentState);
                if (!initializerResult.IsPure)
                {
                    return initializerResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
