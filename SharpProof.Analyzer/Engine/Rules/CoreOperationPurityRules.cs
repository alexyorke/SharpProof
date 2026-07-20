using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class CoreOperationPurityRules {
    internal static PurityAnalysisResult CheckArrayCreation(
        IArrayCreationOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        foreach (var dimension in operation.DimensionSizes) {
            var result = CheckSingleOperation(dimension, context, state);
            if (!result.IsPure) return result;
        }

        if (operation.Initializer != null)
            foreach (var element in operation.Initializer.ElementValues) {
                var result = CheckSingleOperation(element, context, state);
                if (!result.IsPure) return result;
            }

        if (operation.Parent is IArgumentOperation { Parameter.IsParams: true } ||
            RuleAnalysisHelper.IsFreshLocalArrayInitialization(operation) ||
            IsTransientImmutableArrayFactoryArgument(operation))
            return PurityAnalysisResult.Pure;

        return PurityAnalysisResult.Impure(
            operation.Syntax,
            PurityEvidence.Create(
                "mutable_state_write",
                "ArrayCreationPurityRule",
                operation,
                operation.Syntax,
                operation.Type,
                "array_creation"));
    }

    internal static PurityAnalysisResult CheckCollectionExpression(
        ICollectionExpressionOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var targetType = operation.Type;
        if (targetType != null &&
            !IsPureCollectionExpressionTargetType(targetType) &&
            !(targetType is IArrayTypeSymbol && RuleAnalysisHelper.IsFreshLocalArrayInitialization(operation)))
            return ImpureResult(
                operation,
                targetType is IArrayTypeSymbol ? "mutable_state_write" : "unsupported_operation",
                "CollectionExpressionPurityRule",
                targetType,
                "collection_expression_target");

        foreach (var element in operation.Elements) {
            var result = CheckSingleOperation(element, context, state);
            if (!result.IsPure)
                return PurityAnalysisResult.Impure(result.ImpureSyntaxNode ?? operation.Syntax, result.Evidence);
        }

        return PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckObjectOrCollectionInitializer(
        IObjectOrCollectionInitializerOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        foreach (var initializer in operation.Initializers) {
            IOperation? value;
            if (initializer is ISimpleAssignmentOperation assignment) {
                var targetResult = CheckInitializerAssignmentTarget(assignment, context, state);
                if (!targetResult.IsPure) return targetResult;
                value = assignment.Value;
            }
            else if (initializer is IInvocationOperation { TargetMethod.MethodKind: MethodKind.Constructor }) {
                value = initializer;
            }
            else if (initializer is IMemberInitializerOperation) {
                return PurityAnalysisResult.Impure(
                    initializer.Syntax,
                    PurityEvidence.Create(
                        "mutable_state_write",
                        "ObjectOrCollectionInitializerPurityRule",
                        initializer));
            }
            else {
                value = initializer;
            }

            var valueResult = CheckSingleOperation(value!, context, state);
            if (!valueResult.IsPure) return valueResult;
        }

        return PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckEventReference(
        IEventReferenceOperation operation,
        PurityAnalysisContext _,
        PurityAnalysisState __) =>
        ImpureResult(operation, "mutable_state_read", "EventReferencePurityRule", operation.Event);

    internal static PurityAnalysisResult CheckEventAssignment(
        IEventAssignmentOperation operation,
        PurityAnalysisContext _,
        PurityAnalysisState __) =>
        ImpureResult(
            operation,
            "mutable_state_write",
            "EventAssignmentPurityRule",
            (operation.EventReference as IEventReferenceOperation)?.Event,
            "event_subscription");

    internal static PurityAnalysisResult CheckSwitchStatement(
        ISwitchOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) =>
        CheckSingleOperation(operation.Value, context, state);

    internal static PurityAnalysisResult CheckRecursivePattern(
        IRecursivePatternOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        if (operation.DeconstructSymbol is IMethodSymbol deconstructMethod) {
            var result = PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                deconstructMethod,
                operation.Syntax,
                context);
            if (!result.IsPure) return result;
        }

        return ChildOperationsPurityRule.CheckChildOperationsArePure(operation, context, state);
    }

    internal static PurityAnalysisResult CheckSpread(
        ISpreadOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        if (operation.Operand == null) return PurityAnalysisResult.Pure;

        var result = CheckSingleOperation(operation.Operand, context, state);
        if (!result.IsPure)
            return PurityAnalysisResult.Impure(result.ImpureSyntaxNode ?? operation.Syntax, result.Evidence);

        result = LoopPurityRule.CheckForEachEnumeratorPurity(operation.Operand, context);
        return result.IsPure
            ? PurityAnalysisResult.Pure
            : PurityAnalysisResult.Impure(result.ImpureSyntaxNode ?? operation.Syntax, result.Evidence);
    }

    internal static PurityAnalysisResult CheckCoalesce(
        ICoalesceOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var leftResult = CheckSingleOperation(operation.Value, context, state);
        if (!leftResult.IsPure ||
            operation.Value.ConstantValue.HasValue && operation.Value.ConstantValue.Value != null)
            return leftResult;

        return TryCreateReferenceNullAssumptionState(
            state,
            operation.Value,
            true,
            context.SmtAnalysis,
            out var whenNullState)
            ? CheckSingleOperation(operation.WhenNull, context, whenNullState)
            : PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckWith(
        IWithOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        if (operation.Type == null) return PurityAnalysisResult.Impure(operation.Syntax);

        var result = CheckSingleOperation(operation.Operand, context, state);
        if (!result.IsPure) return result;

        if (operation.Initializer != null) {
            result = CheckSingleOperation(operation.Initializer, context, state);
            if (!result.IsPure) return result;
        }

        return operation.Type.IsValueType
            ? PurityAnalysisResult.Pure
            : PurityAnalysisResult.Impure(operation.Syntax);
    }

    internal static PurityAnalysisResult CheckConditionalAccess(
        IConditionalAccessOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var result = CheckSingleOperation(operation.Operation, context, state);
        if (!result.IsPure) return result;

        var receiver = SkipImplicitConversions(operation.Operation) ?? operation.Operation;
        if (receiver.ConstantValue.HasValue && receiver.ConstantValue.Value == null)
            return PurityAnalysisResult.Pure;

        return TryCreateReferenceNullAssumptionState(
            state,
            receiver,
            false,
            context.SmtAnalysis,
            out var whenNotNullState)
            ? CheckSingleOperation(operation.WhenNotNull, context, whenNotNullState)
            : PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckSwitchExpression(
        ISwitchExpressionOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var result = CheckSingleOperation(operation.Value, context, state);
        if (!result.IsPure) return result;

        foreach (var arm in operation.Arms) {
            if (arm.Pattern != null) {
                result = CheckSingleOperation(arm.Pattern, context, state);
                if (!result.IsPure) return result;
            }

            if (arm.Guard != null) {
                result = CheckSingleOperation(arm.Guard, context, state);
                if (!result.IsPure) return result;
            }

            result = CheckSingleOperation(arm.Value, context, state);
            if (!result.IsPure) return result;
        }

        return PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckUnary(
        IUnaryOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var result = CheckSingleOperation(operation.Operand, context, state);
        if (!result.IsPure) return result;

        if (operation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
            operation.Type?.TypeKind == TypeKind.Dynamic)
            return ImpureResult(operation, "dynamic_dispatch", "UnaryOperationPurityRule");

        if (operation.OperatorMethod == null) return PurityAnalysisResult.Pure;

        var operatorMethod = operation.OperatorMethod;
        if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.UserDefinedOperator))
            return ImpureResult(operation, "unknown_external_call", "UnaryOperationPurityRule", operatorMethod);

        return PurityCalleeResolver.GetCalleePurityAtUse(operatorMethod, operation.Syntax, context);
    }

    internal static PurityAnalysisResult CheckLock(
        ILockOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        var synchronizationAllowed = context.ContainingMethodSymbol != null &&
                                     context.AttributePolicy.HasAttribute(
                                         context.ContainingMethodSymbol,
                                         "AllowSynchronizationAttribute");
        if (!synchronizationAllowed)
            return ImpureResult(operation, "synchronization", "LockStatementPurityRule");

        var allowableTarget = operation.LockedValue is ITypeOfOperation ||
                              operation.LockedValue is IFieldReferenceOperation {
                                  Field: { IsReadOnly: true, Type.SpecialType: SpecialType.System_Object }
                              };
        if (!allowableTarget)
            return ImpureResult(operation, "synchronization", "LockStatementPurityRule");

        var targetPurity = CheckSingleOperation(operation.LockedValue, context, state);
        return targetPurity.IsPure
            ? CheckSingleOperation(operation.Body, context, state)
            : targetPurity;
    }

    private static PurityAnalysisResult CheckInitializerAssignmentTarget(
        ISimpleAssignmentOperation assignment,
        PurityAnalysisContext context,
        PurityAnalysisState state) {
        if (assignment.Target is IPropertyReferenceOperation propertyReference &&
            assignment.Parent is IObjectOrCollectionInitializerOperation {
                Parent: IWithOperation { Type.IsValueType: true }
            } &&
            propertyReference.Property.DeclaringSyntaxReferences.Any(
                reference => reference.GetSyntax(context.CancellationToken) is ParameterSyntax))
            return PurityAnalysisResult.Pure;

        return AssignmentPurityRule.CheckWriteTargetPurity(assignment, assignment.Target, context, state);
    }

    private static bool IsPureCollectionExpressionTargetType(ITypeSymbol type) {
        var definition = type.OriginalDefinition;
        if (definition.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Collections.Immutable")
            return true;

        return definition is INamedTypeSymbol {
            TypeArguments.Length: 1,
            ContainingNamespace: { } containingNamespace,
            Name: "ReadOnlySpan" or "Span"
        } && containingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System";
    }

    private static bool IsTransientImmutableArrayFactoryArgument(IArrayCreationOperation operation) {
        var current = operation.Parent;
        while (current is IConversionOperation conversion) current = conversion.Parent;

        return current is IArgumentOperation {
            Parent: IInvocationOperation {
                TargetMethod.OriginalDefinition: {
                    Name: "CreateRange",
                    ContainingType.OriginalDefinition: { } containingType
                }
            }
        } && containingType.ToDisplayString() == "System.Collections.Immutable.ImmutableArray";
    }
}
