using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class ObjectOrCollectionInitializerPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ObjectOrCollectionInitializer);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IObjectOrCollectionInitializerOperation initializer)
            {

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"[ObjInitRule] Checking Initializer: {initializer.Syntax}");





            foreach (var initOp in initializer.Initializers)
            {
                IOperation? valueToCheck = null;


                if (initOp is ISimpleAssignmentOperation assignment)
                {
                    var targetResult = CheckAssignmentTargetPurity(assignment, context, currentState);
                    if (!targetResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"[ObjInitRule]  -> Initializer assignment target IMPURE: {assignment.Target.Syntax}");
                        return targetResult;
                    }

                    valueToCheck = assignment.Value;
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  - Checking Assignment Value: {valueToCheck?.Syntax}");
                }

                else if (initOp is IInvocationOperation invocation && invocation.TargetMethod.MethodKind == MethodKind.Constructor)
                {



                    valueToCheck = initOp;
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  - Checking Collection Element Constructor: {valueToCheck?.Syntax}");
                }

                else if (initOp is IMemberInitializerOperation)
                {
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  -> Nested member initializer mutates an existing member object: {initOp.Syntax}");
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
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  - Checking Other Initializer Op: {valueToCheck?.Syntax}");
                }

                if (valueToCheck != null)
                {
                    var valueResult = PurityAnalysisEngine.CheckSingleOperation(valueToCheck, context, currentState);
                    if (!valueResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"[ObjInitRule]  -> Initializer value IMPURE: {valueToCheck.Syntax}");
                        return valueResult;
                    }
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  -> Initializer value PURE.");
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"[ObjInitRule]  - Could not determine value to check for initializer: {initOp.Syntax}. Assuming impure for safety.");
                    return PurityAnalysisEngine.ImpureResult(initOp.Syntax);
                }
            }

            PurityAnalysisEngine.LogDebug($"[ObjInitRule] All initializer values pure for: {initializer.Syntax}");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckAssignmentTargetPurity(
            ISimpleAssignmentOperation assignment,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (assignment.Target is IPropertyReferenceOperation propertyReference &&
                propertyReference.Property.SetMethod is { } setter)
            {
                var targetExpressionResult = CheckPropertyReferenceTargetPurity(propertyReference, context, currentState);
                if (!targetExpressionResult.IsPure)
                {
                    return targetExpressionResult;
                }

                // Synthesized record struct positional setters have no source body and are incorrectly
                // classified as impure. Inside a value-type 'with' expression (fresh copy), they are trivially pure.
                if (IsInsideValueTypeWithExpression(assignment) &&
                    IsSynthesizedFromPrimaryConstructorParameter(propertyReference.Property, context.CancellationToken))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                var setterPurity = PurityAnalysisEngine.GetCalleePurity(setter.OriginalDefinition, context);
                return setterPurity.IsPure
                    ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                    : setterPurity.WithCallee(setter.OriginalDefinition, assignment.Target.Syntax);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsInsideValueTypeWithExpression(ISimpleAssignmentOperation assignment)
        {
            return assignment.Parent is IObjectOrCollectionInitializerOperation initializer &&
                   initializer.Parent is IWithOperation withOp &&
                   withOp.Type?.IsValueType == true;
        }

        private static bool IsSynthesizedFromPrimaryConstructorParameter(IPropertySymbol property, CancellationToken cancellationToken)
        {
            return property.DeclaringSyntaxReferences.Any(r => r.GetSyntax(cancellationToken) is ParameterSyntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertyReferenceTargetPurity(
            IPropertyReferenceOperation propertyReference,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (propertyReference.Instance != null)
            {
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(propertyReference.Instance, context, currentState);
                if (!instanceResult.IsPure)
                {
                    return instanceResult;
                }
            }

            foreach (var argument in propertyReference.Arguments)
            {
                if (argument.Value == null)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(argument.Syntax);
                }

                var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentResult.IsPure)
                {
                    return argumentResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
