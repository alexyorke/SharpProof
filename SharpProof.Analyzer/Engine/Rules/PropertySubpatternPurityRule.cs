using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class PropertySubpatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.PropertySubpattern);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IPropertySubpatternOperation propertySubpattern)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (propertySubpattern.Member != null)
            {
                var memberResult = PurityAnalysisEngine.CheckSingleOperation(propertySubpattern.Member, context, currentState);
                if (!memberResult.IsPure)
                {
                    return memberResult;
                }
            }

            return PurityAnalysisEngine.CheckSingleOperation(propertySubpattern.Pattern, context, currentState);
        }
    }
}
