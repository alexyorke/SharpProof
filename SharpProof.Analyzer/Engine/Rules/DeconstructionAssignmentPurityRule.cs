using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal sealed class DeconstructionAssignmentPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds =>
            ImmutableArray.Create(OperationKind.DeconstructionAssignment);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation.Syntax is AssignmentExpressionSyntax assignmentSyntax)
            {
                var deconstructionInfo = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeconstructionInfo(context.SemanticModel, assignmentSyntax);
                var deconstructResult = CheckDeconstructionInfo(deconstructionInfo, operation, context);
                if (!deconstructResult.IsPure)
                {
                    return deconstructResult;
                }
            }

            foreach (var child in operation.ChildOperations)
            {
                if (IsPureDeconstructionTargetPlaceholder(child))
                {
                    continue;
                }

                var childResult = PurityAnalysisEngine.CheckSingleOperation(child, context, currentState);
                if (!childResult.IsPure)
                {
                    return childResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDeconstructionInfo(
            DeconstructionInfo deconstructionInfo,
            IOperation operation,
            PurityAnalysisContext context)
        {
            if (deconstructionInfo.Method is IMethodSymbol deconstructMethod)
            {
                var calleeResult = PurityAnalysisEngine.GetCalleePurity(deconstructMethod.OriginalDefinition, context);
                if (!calleeResult.IsPure)
                {
                    return calleeResult.WithCallee(deconstructMethod.OriginalDefinition, operation.Syntax);
                }
            }

            foreach (var nestedInfo in deconstructionInfo.Nested)
            {
                var nestedResult = CheckDeconstructionInfo(nestedInfo, operation, context);
                if (!nestedResult.IsPure)
                {
                    return nestedResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsPureDeconstructionTargetPlaceholder(IOperation operation)
        {
            operation = PurityAnalysisEngine.SkipImplicitConversions(operation) ?? operation;

            if (operation is IDeclarationExpressionOperation ||
                operation is IDiscardOperation ||
                operation is ILocalReferenceOperation)
            {
                return true;
            }

            if (operation is ITupleOperation tupleOperation)
            {
                foreach (var element in tupleOperation.Elements)
                {
                    if (!IsPureDeconstructionTargetPlaceholder(element))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }
    }
}
