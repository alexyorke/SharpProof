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
        private bool IsAssignmentTargetPure(IOperation targetOperation, PurityAnalysisContext context, ISymbol? targetSymbol, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            switch (targetOperation.Kind)
            {
                case OperationKind.Discard:
                    PurityAnalysisEngine.LogDebug(" Assignment Target: Discard - Pure Target");
                    return true;

                case OperationKind.LocalReference:
                    if (targetOperation is ILocalReferenceOperation localRef &&
                        IsRefLocalAliasToExternallyVisibleStorage(localRef.Local, context, currentState))
                    {
                        PurityAnalysisEngine.LogDebug($"    [AssignRule-Target] Target: Ref LocalReference '{targetSymbol?.Name ?? "Unknown"}' aliases caller-visible storage - Impure Target");
                        return false;
                    }

                    PurityAnalysisEngine.LogDebug($"    [AssignRule-Target] Target: LocalReference '{targetSymbol?.Name ?? "Unknown"}' - Pure Target Location");
                    return true;

                case OperationKind.ParameterReference:
                    if (targetOperation is IParameterReferenceOperation paramRef)
                    {
                        if (paramRef.Parameter.RefKind == RefKind.Ref || paramRef.Parameter.RefKind == RefKind.Out ||
                            paramRef.Parameter.RefKind == RefKind.In || paramRef.Parameter.RefKind == RefKind.RefReadOnly)
                        {
                            PurityAnalysisEngine.LogDebug($" Assignment Target: ParameterReference ({paramRef.Parameter.RefKind}) modification attempt - Impure Target");
                            return false;
                        }
                        else
                        {
                            PurityAnalysisEngine.LogDebug(" Assignment Target: ParameterReference (value) - Pure Target");
                            return true;
                        }
                    }
                    return true;

                case OperationKind.FieldReference:
                    var fieldRefOp = (IFieldReferenceOperation)targetOperation;
                    if (fieldRefOp.Field.IsStatic)
                    {
                        PurityAnalysisEngine.LogDebug($" Assignment Target: Static FieldReference '{fieldRefOp.Field.Name}' - Impure Target");
                        return false;
                    }
                    if (IsFreshObjectInitializerFieldAssignment(fieldRefOp, context))
                    {
                        PurityAnalysisEngine.LogDebug($" Assignment Target: FieldReference '{fieldRefOp.Field.Name}' within fresh object initializer - Allowed (Target is Pure)");
                        return true;
                    }
                    if (IsValueTypeWithInitializerAssignment(fieldRefOp, context))
                    {
                        PurityAnalysisEngine.LogDebug($" Assignment Target: FieldReference '{fieldRefOp.Field.Name}' within value-type 'with' initializer - Allowed (Target is Pure)");
                        return true;
                    }
                    if (fieldRefOp.Instance is IInstanceReferenceOperation instanceRef &&
                        instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
                        context.ContainingMethodSymbol.MethodKind == MethodKind.Constructor)
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: Instance FieldReference 'this.Field' within Constructor - Allowed (Target is Pure)");
                        return true;
                    }
                    if (IsPureLocalValueTypeFieldRefTarget(fieldRefOp))
                    {
                        PurityAnalysisEngine.LogDebug($" Assignment Target: FieldReference '{fieldRefOp.Field.Name}' on by-value local value-type receiver - Allowed (Target is Pure)");
                        return true;
                    }
                    if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(fieldRefOp.Instance, fieldRefOp.Syntax, context, currentState))
                    {
                        PurityAnalysisEngine.LogDebug($" Assignment Target: FieldReference '{fieldRefOp.Field.Name}' on fresh local object receiver - Allowed (Target is Pure)");
                        return true;
                    }
                    PurityAnalysisEngine.LogDebug($" Assignment Target: FieldReference '{fieldRefOp.Field.Name}' (Non-Static, Non-Constructor 'this.Field') - Impure Target");
                    return false;

                case OperationKind.PropertyReference:
                    var propRefOp = (IPropertyReferenceOperation)targetOperation;
                    if (propRefOp.Property.IsStatic)
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: Static PropertyReference - Impure Target");
                        return false;
                    }


                    if (propRefOp.Property.SetMethod != null && propRefOp.Property.SetMethod.IsInitOnly)
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: Init-only PropertyReference - Allowed (Target is Pure by IsAssignmentTargetPure)");
                        return true;
                    }
                    if (IsValueTypeWithInitializerAssignment(propRefOp, context))
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: PropertyReference within value-type 'with' initializer - Allowed (Target is Pure)");
                        return true;
                    }


                    if (propRefOp.Instance is IInstanceReferenceOperation instanceRefKind &&
                        instanceRefKind.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                    {
                        if (context.ContainingMethodSymbol.MethodKind == MethodKind.Constructor)
                        {
                            PurityAnalysisEngine.LogDebug(" Assignment Target: Instance PropertyReference 'this.Prop' (non-init) within Constructor - Allowed (Target is Pure)");
                            return true;
                        }

                        if (context.ContainingMethodSymbol.ContainingType.IsRecord &&
                            context.ContainingMethodSymbol.ContainingType.IsValueType &&
                            PurityAnalysisEngine.IsPureEnforced(
                                context.ContainingMethodSymbol,
                                context.EnforcePureAttributeSymbol,
                                context.PureAttributeSymbol))
                        {
                            PurityAnalysisEngine.LogDebug(" Assignment Target: Instance PropertyReference 'this.Prop' (non-init) within [EnforcePure] Record Struct Method - Target is Impure for this method context");
                            return false;
                        }

                        PurityAnalysisEngine.LogDebug(" Assignment Target: Instance PropertyReference 'this.Prop' (non-init, Non-Constructor/Special Record) - Impure Target due to 'this' modification");
                        return false;
                    }



                    if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(propRefOp.Instance, propRefOp.Syntax, context, currentState))
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: PropertyReference on fresh local object receiver - Allowed (Target is Pure)");
                        return true;
                    }

                    PurityAnalysisEngine.LogDebug($" Assignment Target: PropertyReference on local/param for non-init prop ('{propRefOp.Instance?.Syntax}') - Impure Target by IsAssignmentTargetPure rule.");
                    return false;

                case OperationKind.ArrayElementReference:
                    if (targetOperation is IArrayElementReferenceOperation arrayElementReference &&
                        IsOwnedLocalArrayReference(arrayElementReference.ArrayReference, currentState))
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: ArrayElementReference on fresh local array - Pure Target");
                        return true;
                    }

                    PurityAnalysisEngine.LogDebug(" Assignment Target: ArrayElementReference - Impure Target");
                    return false;

                case OperationKind.InlineArrayAccess:
                    if (targetOperation is IInlineArrayAccessOperation inlineArrayAccess &&
                        IsPureInlineArrayTarget(inlineArrayAccess, context))
                    {
                        PurityAnalysisEngine.LogDebug(" Assignment Target: InlineArrayAccess on local/by-value storage - Pure Target");
                        return true;
                    }

                    PurityAnalysisEngine.LogDebug(" Assignment Target: InlineArrayAccess - Impure Target");
                    return false;

                default:
                    PurityAnalysisEngine.LogDebug($" Assignment Target: Unhandled Kind {targetOperation.Kind} - Assuming Impure Target");
                    return false;
            }
        }

        private static bool IsPureInlineArrayTarget(
            IInlineArrayAccessOperation inlineArrayAccessOperation,
            PurityAnalysisContext context)
        {
            var instance = inlineArrayAccessOperation.Instance;
            if (instance == null)
            {
                return false;
            }

            if (instance is ILocalReferenceOperation)
            {
                return true;
            }

            if (instance is IParameterReferenceOperation parameterReference)
            {
                return parameterReference.Parameter.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnly);
            }

            return instance is IFieldReferenceOperation fieldReference &&
                   IsPureLocalValueTypeFieldRefTarget(fieldReference);
        }

        private static bool IsOwnedLocalArrayReference(IOperation operation, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is IConversionOperation conversionOperation && conversionOperation.Operand != null)
            {
                return IsOwnedLocalArrayReference(conversionOperation.Operand, currentState);
            }

            return PurityAnalysisEngine.IsTrackedOwnedArrayValue(operation, currentState);
        }

        private static bool IsRefLocalAliasToExternallyVisibleStorage(
            ILocalSymbol local,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (local.RefKind != RefKind.Ref && local.RefKind != RefKind.Out)
            {
                return false;
            }

            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            if (PurityAnalysisEngine.HasSymbolicBorrowFactForLocal(local, currentState, SymbolicBorrowKind.Mutable) &&
                IsRefLocalAliasToExternallyVisibleStorage(local, context, currentState, visited))
            {
                return true;
            }

            return IsRefLocalAliasToExternallyVisibleStorage(local, context, currentState, visited);
        }

        private static bool IsRefLocalAliasToExternallyVisibleStorage(
            ILocalSymbol local,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            HashSet<ISymbol> visited)
        {
            if ((local.RefKind != RefKind.Ref && local.RefKind != RefKind.Out) || !visited.Add(local))
            {
                return false;
            }

            foreach (var syntaxReference in local.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax declarator ||
                    declarator.Initializer?.Value == null)
                {
                    continue;
                }

                ExpressionSyntax initializerSyntax = declarator.Initializer.Value;
                if (initializerSyntax is RefExpressionSyntax refExpression)
                {
                    initializerSyntax = refExpression.Expression;
                }

                var initializerOperation = context.SemanticModel.GetOperation(initializerSyntax, context.CancellationToken);
                if (IsExternallyVisibleRefTarget(initializerOperation, context, currentState, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExternallyVisibleRefTarget(
            IOperation? operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            HashSet<ISymbol> visited)
        {
            operation = PurityAnalysisEngine.SkipImplicitConversions(operation);

            return operation switch
            {
                IParameterReferenceOperation parameterReference =>
                    parameterReference.Parameter.RefKind == RefKind.Ref ||
                    parameterReference.Parameter.RefKind == RefKind.Out ||
                    parameterReference.Parameter.RefKind == RefKind.In ||
                    parameterReference.Parameter.RefKind == RefKind.RefReadOnly,

                ILocalReferenceOperation localReference =>
                    IsRefLocalAliasToExternallyVisibleStorage(localReference.Local, context, currentState, visited),

                IArrayElementReferenceOperation arrayElementReference =>
                    !IsOwnedLocalArrayReference(arrayElementReference.ArrayReference, currentState),

                IFieldReferenceOperation fieldReference =>
                    !IsPureLocalValueTypeFieldRefTarget(fieldReference),

                IPropertyReferenceOperation => true,

                _ => false
            };
        }

        private static bool IsPureLocalValueTypeFieldRefTarget(IFieldReferenceOperation fieldReference)
        {
            var instance = PurityAnalysisEngine.SkipImplicitConversions(fieldReference.Instance);
            return instance switch
            {
                ILocalReferenceOperation localReference =>
                    localReference.Local.RefKind == RefKind.None &&
                    localReference.Local.Type.IsValueType,

                IParameterReferenceOperation parameterReference =>
                    parameterReference.Parameter.RefKind == RefKind.None &&
                    parameterReference.Parameter.Type.IsValueType,

                _ => false
            };
        }

        private static bool IsFreshObjectInitializerFieldAssignment(
            IFieldReferenceOperation fieldReferenceOperation,
            PurityAnalysisContext context)
        {
            if (fieldReferenceOperation.Parent is not ISimpleAssignmentOperation assignment ||
                assignment.Target != fieldReferenceOperation)
            {
                return false;
            }

            if (assignment.Parent is IObjectOrCollectionInitializerOperation initializer &&
                initializer.Parent is IObjectCreationOperation)
            {
                return true;
            }

            if (fieldReferenceOperation.Instance is not Microsoft.CodeAnalysis.FlowAnalysis.IFlowCaptureReferenceOperation flowCaptureReference)
            {
                return false;
            }

            var capturedOperation = context.SemanticModel.GetOperation(flowCaptureReference.Syntax, context.CancellationToken);
            return capturedOperation is IObjectCreationOperation;
        }

        private static bool IsValueTypeWithInitializerAssignment(
            IOperation targetOperation,
            PurityAnalysisContext context)
        {
            if (targetOperation.Parent is not ISimpleAssignmentOperation assignment ||
                assignment.Target != targetOperation)
            {
                return false;
            }

            var withSyntax = assignment.Syntax.AncestorsAndSelf().OfType<WithExpressionSyntax>().FirstOrDefault();
            if (withSyntax == null)
            {
                return false;
            }

            return context.SemanticModel.GetOperation(withSyntax, context.CancellationToken) is IWithOperation withOperation &&
                   withOperation.Type?.IsValueType == true;
        }


        private static ISymbol? TryResolveSymbol(IOperation? operation)
        {
            return operation switch
            {
                ILocalReferenceOperation localRef => localRef.Local,
                IParameterReferenceOperation paramRef => paramRef.Parameter,
                IFieldReferenceOperation fieldRef => fieldRef.Field,
                IPropertyReferenceOperation propRef => propRef.Property,

                _ => null
            };
        }

        private static IOperation NormalizeAssignmentTargetOperation(
            IOperation targetOperation,
            PurityAnalysisContext context)
        {
            if (targetOperation is not Microsoft.CodeAnalysis.FlowAnalysis.IFlowCaptureReferenceOperation ||
                targetOperation.Syntax == null)
            {
                return targetOperation;
            }

            var reboundOperation = context.SemanticModel.GetOperation(targetOperation.Syntax, context.CancellationToken);
            return reboundOperation is not null and not Microsoft.CodeAnalysis.FlowAnalysis.IFlowCaptureReferenceOperation
                ? reboundOperation
                : targetOperation;
        }
    }
}
