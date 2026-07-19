namespace SharpProof.Analyzer.Engine.Rules;

internal partial class AssignmentPurityRule
{
    private static bool IsAssignmentTargetPure(IOperation targetOperation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        switch (targetOperation.Kind)
        {
            case OperationKind.Discard:
                return true;

            case OperationKind.LocalReference:
                if (targetOperation is ILocalReferenceOperation localRef &&
                    IsRefLocalAliasToExternallyVisibleStorage(localRef.Local, context, currentState))
                    return false;

                return true;

            case OperationKind.ParameterReference:
                if (targetOperation is IParameterReferenceOperation paramRef)
                {
                    if (paramRef.Parameter.RefKind == RefKind.Ref || paramRef.Parameter.RefKind == RefKind.Out ||
                        paramRef.Parameter.RefKind == RefKind.In || paramRef.Parameter.RefKind == RefKind.RefReadOnly)
                        return false;

                    return true;
                }

                return true;

            case OperationKind.FieldReference:
                var fieldRefOp = (IFieldReferenceOperation)targetOperation;
                if (fieldRefOp.Field.IsStatic) return false;
                if (IsFreshObjectInitializerFieldAssignment(fieldRefOp, context)) return true;
                if (IsValueTypeWithInitializerAssignment(fieldRefOp, context)) return true;
                if (fieldRefOp.Instance is IInstanceReferenceOperation instanceRef &&
                    instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
                    context.ContainingMethodSymbol.MethodKind == MethodKind.Constructor)
                    return true;
                if (IsPureLocalValueTypeFieldRefTarget(fieldRefOp)) return true;
                if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(fieldRefOp.Instance,
                        fieldRefOp.Syntax, context, currentState)) return true;
                return false;

            case OperationKind.PropertyReference:
                var propRefOp = (IPropertyReferenceOperation)targetOperation;
                if (propRefOp.Property.IsStatic) return false;


                if (propRefOp.Property.SetMethod != null && propRefOp.Property.SetMethod.IsInitOnly) return true;
                if (IsValueTypeWithInitializerAssignment(propRefOp, context)) return true;


                if (propRefOp.Instance is IInstanceReferenceOperation instanceRefKind &&
                    instanceRefKind.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                {
                    if (context.ContainingMethodSymbol.MethodKind == MethodKind.Constructor) return true;

                    return false;
                }


                if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(propRefOp.Instance,
                        propRefOp.Syntax, context, currentState)) return true;

                return false;

            case OperationKind.ArrayElementReference:
                if (targetOperation is IArrayElementReferenceOperation arrayElementReference &&
                    IsOwnedLocalArrayReference(arrayElementReference.ArrayReference, currentState))
                    return true;

                return false;

            case OperationKind.InlineArrayAccess:
                if (targetOperation is IInlineArrayAccessOperation inlineArrayAccess &&
                    IsPureInlineArrayTarget(inlineArrayAccess))
                    return true;

                return false;

            default:
                return false;
        }
    }

    private static bool IsPureInlineArrayTarget(IInlineArrayAccessOperation inlineArrayAccessOperation)
    {
        var instance = inlineArrayAccessOperation.Instance;
        if (instance == null) return false;

        if (instance is ILocalReferenceOperation) return true;

        if (instance is IParameterReferenceOperation parameterReference)
            return parameterReference.Parameter.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In
                or RefKind.RefReadOnly);

        return instance is IFieldReferenceOperation fieldReference &&
               IsPureLocalValueTypeFieldRefTarget(fieldReference);
    }

    private static bool IsOwnedLocalArrayReference(IOperation operation,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is IConversionOperation conversionOperation && conversionOperation.Operand != null)
            return IsOwnedLocalArrayReference(conversionOperation.Operand, currentState);

        return PurityKnownBclSemantics.IsTrackedOwnedArrayValue(operation, currentState);
    }

    private static bool IsRefLocalAliasToExternallyVisibleStorage(
        ILocalSymbol local,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (local.RefKind != RefKind.Ref && local.RefKind != RefKind.Out) return false;

        var visited = new HashSet<ISymbol>(SymbolEq.Default);
        if (PuritySymbolicStateFacts.HasSymbolicBorrowFactForLocal(local, currentState, SymbolicBorrowKind.Mutable) &&
            IsRefLocalAliasToExternallyVisibleStorage(local, context, currentState, visited))
            return true;

        return IsRefLocalAliasToExternallyVisibleStorage(local, context, currentState, visited);
    }

    private static bool IsRefLocalAliasToExternallyVisibleStorage(
        ILocalSymbol local,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        HashSet<ISymbol> visited)
    {
        if ((local.RefKind != RefKind.Ref && local.RefKind != RefKind.Out) || !visited.Add(local)) return false;

        foreach (var initializerOperation in RuleAnalysisHelper.EnumerateRefLocalInitializerOperations(
                     local,
                     context.SemanticModel,
                     context.CancellationToken))
        {
            if (IsExternallyVisibleRefTarget(initializerOperation, context, currentState, visited)) return true;
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
            return false;

        if (assignment.Parent is IObjectOrCollectionInitializerOperation initializer &&
            initializer.Parent is IObjectCreationOperation)
            return true;

        if (fieldReferenceOperation.Instance is not IFlowCaptureReferenceOperation flowCaptureReference) return false;

        var capturedOperation =
            context.SemanticModel.GetOperation(flowCaptureReference.Syntax, context.CancellationToken);
        return capturedOperation is IObjectCreationOperation;
    }

    private static bool IsValueTypeWithInitializerAssignment(
        IOperation targetOperation,
        PurityAnalysisContext context)
    {
        if (targetOperation.Parent is not ISimpleAssignmentOperation assignment ||
            assignment.Target != targetOperation)
            return false;

        var withSyntax = assignment.Syntax.AncestorsAndSelf().OfType<WithExpressionSyntax>().FirstOrDefault();
        if (withSyntax == null) return false;

        return context.SemanticModel.GetOperation(withSyntax,
                   context.CancellationToken) is IWithOperation withOperation &&
               withOperation.Type?.IsValueType == true;
    }

    private static IOperation NormalizeAssignmentTargetOperation(
        IOperation targetOperation,
        PurityAnalysisContext context)
    {
        if (targetOperation is not IFlowCaptureReferenceOperation ||
            targetOperation.Syntax == null)
            return targetOperation;

        var reboundOperation = context.SemanticModel.GetOperation(targetOperation.Syntax, context.CancellationToken);
        return reboundOperation is not null and not IFlowCaptureReferenceOperation
            ? reboundOperation
            : targetOperation;
    }
}
