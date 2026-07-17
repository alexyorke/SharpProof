using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ObjectOrCollectionInitializerPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds =>
        ImmutableArray.Create(OperationKind.ObjectOrCollectionInitializer);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not IObjectOrCollectionInitializerOperation initializer)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        foreach (var initOp in initializer.Initializers)
        {
            IOperation? valueToCheck = null;


            if (initOp is ISimpleAssignmentOperation assignment)
            {
                var targetResult = CheckAssignmentTargetPurity(assignment, context, currentState);
                if (!targetResult.IsPure) return targetResult;

                valueToCheck = assignment.Value;
            }

            else if (initOp is IInvocationOperation invocation &&
                     invocation.TargetMethod.MethodKind == MethodKind.Constructor)
            {
                valueToCheck = initOp;
            }

            else if (initOp is IMemberInitializerOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    initOp.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "mutable_state_write",
                        nameof(ObjectOrCollectionInitializerPurityRule),
                        initOp));
            }

            else
            {
                valueToCheck = initOp;
            }

            if (valueToCheck != null)
            {
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(valueToCheck, context, currentState);
                if (!valueResult.IsPure) return valueResult;
            }
            else
            {
                return PurityAnalysisEngine.ImpureResult(initOp.Syntax);
            }
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckAssignmentTargetPurity(
        ISimpleAssignmentOperation assignment,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (assignment.Target is IPropertyReferenceOperation propertyReference)
        {
            // Synthesized record struct positional setters have no source body and are incorrectly
            // classified as impure. Inside a value-type 'with' expression (fresh copy), they are trivially pure.
            if (IsInsideValueTypeWithExpression(assignment) &&
                IsSynthesizedFromPrimaryConstructorParameter(propertyReference.Property, context.CancellationToken))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        return AssignmentPurityRule.CheckWriteTargetPurity(
            assignment,
            assignment.Target,
            context,
            currentState);
    }

    private static bool IsInsideValueTypeWithExpression(ISimpleAssignmentOperation assignment)
    {
        return assignment.Parent is IObjectOrCollectionInitializerOperation initializer &&
               initializer.Parent is IWithOperation withOp &&
               withOp.Type?.IsValueType == true;
    }

    private static bool IsSynthesizedFromPrimaryConstructorParameter(IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        return property.DeclaringSyntaxReferences.Any(r => r.GetSyntax(cancellationToken) is ParameterSyntax);
    }

}
