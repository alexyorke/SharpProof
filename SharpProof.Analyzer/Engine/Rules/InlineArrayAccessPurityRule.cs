using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class InlineArrayAccessPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.InlineArrayAccess);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IInlineArrayAccessOperation inlineArrayAccessOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (inlineArrayAccessOperation.Instance == null ||
                inlineArrayAccessOperation.Argument == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(inlineArrayAccessOperation.Syntax);
            }

            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                inlineArrayAccessOperation.Instance,
                context,
                currentState);
            if (!instanceResult.IsPure)
            {
                return instanceResult;
            }

            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
                inlineArrayAccessOperation.Argument,
                context,
                currentState);
            if (!argumentResult.IsPure)
            {
                return argumentResult;
            }

            if (IsPartOfAssignmentTarget(inlineArrayAccessOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsPartOfAssignmentTarget(IOperation operation)
        {
            IOperation? current = operation;
            while (current != null)
            {
                if (current.Parent is IAssignmentOperation assignment && assignment.Target == current)
                {
                    return true;
                }

                if (current.Parent is ICompoundAssignmentOperation compoundAssignment && compoundAssignment.Target == current)
                {
                    return true;
                }

                if (!(current.Parent is IMemberReferenceOperation ||
                      current.Parent is IPropertyReferenceOperation ||
                      current.Parent is IArrayElementReferenceOperation ||
                      current.Parent is IInlineArrayAccessOperation))
                {
                    break;
                }

                current = current.Parent;
            }

            return false;
        }
    }
}
