using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class AssignmentPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.SimpleAssignment, OperationKind.CompoundAssignment, OperationKind.CoalesceAssignment, OperationKind.Increment, OperationKind.Decrement);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            IOperation targetOperation;
            IOperation? valueOperation = null;
            IMethodSymbol? compoundOperatorMethod = null;
            SyntaxNode diagnosticNode = operation.Syntax;

            if (operation is IAssignmentOperation assignmentOperation)
            {
                targetOperation = assignmentOperation.Target;
                valueOperation = assignmentOperation.Value;

            }
            else if (operation is ICompoundAssignmentOperation compoundAssignmentOperation)
            {
                targetOperation = compoundAssignmentOperation.Target;
                valueOperation = compoundAssignmentOperation.Value;
                compoundOperatorMethod = compoundAssignmentOperation.OperatorMethod?.OriginalDefinition;

            }
            else if (operation is IIncrementOrDecrementOperation incrementDecrementOperation)
            {
                targetOperation = incrementDecrementOperation.Target;
                compoundOperatorMethod = incrementDecrementOperation.OperatorMethod?.OriginalDefinition;

            }
            else
            {
                PurityAnalysisEngine.LogDebug($"AssignmentPurityRule: Unexpected operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"AssignmentPurityRule: Analyzing Target {targetOperation?.Kind} in operation {operation.Kind}");

            if (targetOperation == null)
            {
                PurityAnalysisEngine.LogDebug("AssignmentPurityRule: Target operation is null. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            targetOperation = NormalizeAssignmentTargetOperation(targetOperation, context);

            if (valueOperation != null)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule] Checking assignment value (RHS): {valueOperation.Syntax} ({valueOperation.Kind})");
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(valueOperation, context, currentState);
                if (!valueResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [AssignRule] Assignment value (RHS) itself is IMPURE. Assignment is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        valueResult.ImpureSyntaxNode ?? valueOperation.Syntax,
                        valueResult.Evidence);
                }



                ITypeSymbol? targetType = (targetOperation as ILocalReferenceOperation)?.Type ??
                                          (targetOperation as IParameterReferenceOperation)?.Type ??
                                          (targetOperation as IFieldReferenceOperation)?.Type ??
                                          (targetOperation as IPropertyReferenceOperation)?.Type;

                ITypeSymbol? valueType = valueOperation.Type;

                if (targetType != null && valueType != null && !SymbolEqualityComparer.Default.Equals(targetType, valueType))
                {
                    IConversionOperation? conversionOp = null;


                    if (valueOperation is IConversionOperation topLevelConv &&
                        topLevelConv.Conversion.IsImplicit &&
                        SymbolEqualityComparer.Default.Equals(topLevelConv.Type, targetType))
                    {
                        conversionOp = topLevelConv;
                        PurityAnalysisEngine.LogDebug("    [AssignRule] Found implicit conversion as top-level value operation.");
                    }
                    else
                    {

                        conversionOp = valueOperation.DescendantsAndSelf()
                                        .OfType<IConversionOperation>()
                                        .FirstOrDefault(conv => conv.Conversion.IsImplicit &&
                                                               SymbolEqualityComparer.Default.Equals(conv.Type, targetType) &&
                                                               conv.Operand != null &&
                                                               SymbolEqualityComparer.Default.Equals(conv.Operand.Type, valueType));
                        if (conversionOp != null)
                        {
                            PurityAnalysisEngine.LogDebug("    [AssignRule] Found implicit conversion in descendants of value operation.");
                        }
                    }


                    if (conversionOp != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [AssignRule] Checking implicit conversion operation: {conversionOp.Syntax}");
                        var conversionResult = PurityAnalysisEngine.CheckSingleOperation(conversionOp, context, currentState);
                        if (!conversionResult.IsPure)
                        {

                            PurityAnalysisEngine.LogDebug("    [AssignRule] Implicit conversion operation reported IMPURE.");
                            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                                conversionResult.ImpureSyntaxNode ?? conversionOp.Operand?.Syntax ?? valueOperation.Syntax,
                                conversionResult.Evidence);
                        }
                    }
                }

            }

            if (compoundOperatorMethod != null)
            {
                var operatorResult = CheckCompoundAssignmentOperatorPurity(compoundOperatorMethod, operation, context);
                if (!operatorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [AssignRule] Compound assignment operator '{compoundOperatorMethod.Name}' is IMPURE.");
                    return operatorResult;
                }
            }


            PurityAnalysisEngine.LogDebug($"    [AssignRule] Checking assignment target (LHS): {targetOperation.Syntax} ({targetOperation.Kind})");
            var targetResult = PurityAnalysisEngine.CheckSingleOperation(targetOperation, context, currentState);
            if (!targetResult.IsPure)
            {

                PurityAnalysisEngine.LogDebug($"AssignmentPurityRule: Target check failed (Kind: {targetOperation.Kind}, RefKind: {(targetOperation as IParameterReferenceOperation)?.Parameter.RefKind}). Reporting impurity on the whole operation: {operation.Syntax}");
                if (TryCreateMutableBorrowConflictEvidence(
                        operation,
                        TryResolveSymbol(targetOperation),
                        currentState,
                        context,
                        out var borrowConflictEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        borrowConflictEvidence);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax, targetResult.Evidence);
            }


            var setterResult = CheckPropertySetterPurity(targetOperation, context, currentState);
            if (!setterResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule] Property/indexer setter is IMPURE for assignment target {targetOperation.Syntax}.");
                return setterResult;
            }

            var targetSymbol = TryResolveSymbol(targetOperation);
            if (TryCreateMutableBorrowConflictEvidence(
                    operation,
                    targetSymbol,
                    currentState,
                    context,
                    out var earlyBorrowConflictEvidence))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    earlyBorrowConflictEvidence);
            }

            bool isPureAssignment = IsAssignmentTargetPure(targetOperation, context, targetSymbol, currentState);

            if (!isPureAssignment)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule] Assignment target itself is considered impure for assignment. Assignment is Impure.");
                if (TryCreateMutableBorrowConflictEvidence(
                        operation,
                        targetSymbol,
                        currentState,
                        context,
                        out var borrowConflictEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        borrowConflictEvidence);
                }

                if (PurityAnalysisEngine.TryCreateCallerVisibleMutationEvidence(
                        operation,
                        targetOperation,
                        currentState,
                        nameof(AssignmentPurityRule),
                        out var mutationEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        mutationEvidence);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "mutable_state_write",
                        ruleName: nameof(AssignmentPurityRule),
                        operation: operation,
                        syntaxNode: operation.Syntax,
                        symbol: targetSymbol));
            }



            PurityAnalysisEngine.LogDebug("AssignmentPurityRule: Both target and value (if applicable) are pure. Result: Pure");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

    }
}
