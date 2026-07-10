using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class RecursivePatternPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.RecursivePattern);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is IRecursivePatternOperation recursivePatternOperation &&
            recursivePatternOperation.DeconstructSymbol is IMethodSymbol deconstructMethod)
        {
            var deconstructResult = PurityAnalysisEngine.GetCalleePurity(deconstructMethod.OriginalDefinition, context);
            if (!deconstructResult.IsPure)
                return deconstructResult.WithCallee(deconstructMethod.OriginalDefinition, operation.Syntax);
        }

        return ChildOperationsPurityRule.CheckChildOperationsArePure(operation, context, currentState);
    }
}