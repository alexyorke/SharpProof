using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class AssignmentPurityRule : IPurityRule
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



            if (valueOperation != null && targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL] Detected delegate assignment to: {targetSymbol.Name} ({targetSymbol.Kind})");
                PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value Op Kind: {valueOperation.Kind} | Syntax: {valueOperation.Syntax}");


                PurityAnalysisEngine.PotentialTargets? valueTargets = null;
                if (valueOperation is IMethodReferenceOperation methodRef)
                {

                    valueTargets = PurityAnalysisEngine.PotentialTargets.FromSingle(methodRef.Method.OriginalDefinition);
                    PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value is Method Group: {methodRef.Method.ToDisplayString()}");
                }
                else if (valueOperation is IDelegateCreationOperation delegateCreation)
                {
                    if (delegateCreation.Target is IMethodReferenceOperation lambdaRef)
                    {

                        valueTargets = PurityAnalysisEngine.PotentialTargets.FromSingle(lambdaRef.Method.OriginalDefinition);
                        PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value is Lambda/Delegate Creation targeting: {lambdaRef.Method.ToDisplayString()}");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value is Lambda/Delegate Creation with unresolvable target ({delegateCreation.Target?.Kind}). Cannot track.");
                    }
                }
                else
                {
                    ISymbol? valueSourceSymbol = TryResolveSymbol(valueOperation);
                    if (valueSourceSymbol != null && currentState.DelegateTargetMap.TryGetValue(valueSourceSymbol, out var sourceTargets))
                    {
                        valueTargets = sourceTargets;
                        PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value is reference to {valueSourceSymbol.Name}. Propagating {sourceTargets.MethodSymbols.Count} targets.");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   Value is reference ({valueOperation.Kind}) but source symbol ({valueSourceSymbol?.Name ?? "null"}) not found in map or unresolved. Cannot track.");
                    }
                }

                if (valueTargets != null)
                {


                    var nextState = currentState.WithDelegateTarget(targetSymbol, valueTargets.Value);

                    PurityAnalysisEngine.LogDebug($"    [AssignRule-DEL]   ---> Updating state map for {targetSymbol.Name} with {valueTargets.Value.MethodSymbols.Count} target(s). New Map Count: {nextState.DelegateTargetMap.Count}");



                }
            }


            PurityAnalysisEngine.LogDebug("AssignmentPurityRule: Both target and value (if applicable) are pure. Result: Pure");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckCompoundAssignmentOperatorPurity(
            IMethodSymbol operatorMethod,
            IOperation operation,
            PurityAnalysisContext context)
        {
            var hasTrustedGeneratedPurity = PurityAnalysisEngine.TryGetTrustedGeneratedPurityCoverage(
                operatorMethod,
                context.SemanticModel.Compilation,
                out var generatedPurity);

            if ((!hasTrustedGeneratedPurity &&
                PurityAnalysisEngine.IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation)) ||
                PurityAnalysisEngine.HasPureExternalAttribute(operatorMethod))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (hasTrustedGeneratedPurity)
            {
				if (generatedPurity.IsPure)
				{
					return PurityAnalysisEngine.PurityAnalysisResult.Pure;
				}

				if (!generatedPurity.IsPure)
				{
					return PurityAnalysisEngine.PurityAnalysisResult.Impure(
						operation.Syntax,
						PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            nameof(AssignmentPurityRule),
                            operation,
                            syntaxNode: operation.Syntax,
                            symbol: operatorMethod.OriginalDefinition,
                            catalogSource: "generated_purity_summary"));
                }
            }

            if (!ShouldAnalyzeCompoundAssignmentOperator(operatorMethod))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var operatorPurity = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);
            return operatorPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : operatorPurity.WithCallee(operatorMethod, operation.Syntax);
        }

        private static bool ShouldAnalyzeCompoundAssignmentOperator(IMethodSymbol operatorMethod)
        {
            return PurityAnalysisEngine.ShouldAnalyzeCompoundAssignmentOperator(operatorMethod);
        }

        private static bool TryCreateMutableBorrowConflictEvidence(
            IOperation operation,
            ISymbol? targetSymbol,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityEvidence evidence)
        {
            return PurityAnalysisEngine.TryCreateMutableBorrowConflictEvidence(
                operation,
                targetSymbol,
                currentState,
                context.SemanticModel,
                context.CancellationToken,
                nameof(AssignmentPurityRule),
                out evidence);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckPropertySetterPurity(
            IOperation targetOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (targetOperation is not IPropertyReferenceOperation propertyReference ||
                propertyReference.Property.SetMethod is not { } setter)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsValueTypeWithInitializerAssignment(propertyReference, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsSourceAutoPropertySetter(propertyReference.Property))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsPotentiallyDispatchedSetter(setter))
            {
                return CheckDispatchedSetterPurity(propertyReference, context, currentState);
            }

            var setterResult = PurityAnalysisEngine.GetCalleePurity(setter.OriginalDefinition, context);
            return setterResult.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : setterResult.WithCallee(setter.OriginalDefinition, targetOperation.Syntax);
        }

        private static bool IsSourceAutoPropertySetter(IPropertySymbol propertySymbol)
        {
            if (propertySymbol.SetMethod == null ||
                propertySymbol.SetMethod.IsAbstract ||
                propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                return false;
            }

            foreach (var syntaxReference in propertySymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertyDeclaration ||
                    propertyDeclaration.AccessorList == null)
                {
                    continue;
                }

                var setterAccessor = propertyDeclaration.AccessorList.Accessors
                    .FirstOrDefault(accessor =>
                        accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration) ||
                        accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration));
                if (setterAccessor != null &&
                    setterAccessor.Body == null &&
                    setterAccessor.ExpressionBody == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPotentiallyDispatchedSetter(IMethodSymbol setterSymbol)
        {
            return setterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                   setterSymbol.IsVirtual ||
                   setterSymbol.IsAbstract ||
                   setterSymbol.IsOverride;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedSetterPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var candidates = ResolvePotentialSetterTargets(
                propertyReferenceOperation.Property,
                context.SemanticModel,
                GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState, context.SemanticModel.Compilation) ??
                    PropertyDispatchHelper.GetKnownReceiverType(propertyReferenceOperation.Instance));

            if (candidates.IsDefaultOrEmpty)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(AssignmentPurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.SetMethod));
            }

            foreach (var setterCandidate in candidates)
            {
                var setterResult = PurityAnalysisEngine.GetCalleePurity(setterCandidate, context);
                if (!setterResult.IsPure)
                {
                    return setterResult.WithCallee(setterCandidate, propertyReferenceOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static INamedTypeSymbol? GetTrackedLocalReceiverType(
            IOperation? instanceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            Compilation compilation)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, compilation, out var concreteType)
                ? concreteType
                : null;
        }

        private static ImmutableArray<IMethodSymbol> ResolvePotentialSetterTargets(
            IPropertySymbol propertySymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol? knownReceiverType)
        {
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var targetProperty = propertySymbol.OriginalDefinition;

            if (knownReceiverType != null &&
                (knownReceiverType.TypeKind == TypeKind.Struct || knownReceiverType.IsSealed))
            {
                AddSetterForReceiverType(knownReceiverType, targetProperty, targets);
                return targets.ToImmutableArray();
            }

            if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
            {
                foreach (var type in PropertyDispatchHelper.EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace))
                {
                    if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                    {
                        continue;
                    }

                    if (!PropertyDispatchHelper.ImplementsInterface(type, targetProperty.ContainingType))
                    {
                        continue;
                    }

                    AddSetterForReceiverType(type, targetProperty, targets);
                }

                if (targetProperty.SetMethod != null && !targetProperty.SetMethod.IsAbstract)
                {
                    targets.Add(targetProperty.SetMethod.OriginalDefinition);
                }

                return targets.ToImmutableArray();
            }

            var baseProperty = PropertyDispatchHelper.GetRootOverriddenProperty(targetProperty);
            var baseType = baseProperty.ContainingType;
            if (baseType != null)
            {
                foreach (var type in PropertyDispatchHelper.EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace))
                {
                    if (!PropertyDispatchHelper.DerivesFrom(type, baseType))
                    {
                        continue;
                    }

                    foreach (var property in type.GetMembers(baseProperty.Name).OfType<IPropertySymbol>())
                    {
                        if (PropertyDispatchHelper.OverridesProperty(property, baseProperty) && property.SetMethod != null)
                        {
                            targets.Add(property.SetMethod.OriginalDefinition);
                        }
                    }
                }
            }

            if (baseProperty.SetMethod != null && !baseProperty.SetMethod.IsAbstract)
            {
                targets.Add(baseProperty.SetMethod.OriginalDefinition);
            }

            return targets.ToImmutableArray();
        }

        private static void AddSetterForReceiverType(
            INamedTypeSymbol receiverType,
            IPropertySymbol targetProperty,
            HashSet<IMethodSymbol> targets)
        {
            ISymbol? implementation = null;
            if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
            {
                implementation = receiverType.FindImplementationForInterfaceMember(targetProperty);
            }
            else
            {
                for (INamedTypeSymbol? current = receiverType; current != null; current = current.BaseType)
                {
                    implementation = current
                        .GetMembers(targetProperty.Name)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault(property =>
                            SymbolEqualityComparer.Default.Equals(property.OriginalDefinition, targetProperty) ||
                            PropertyDispatchHelper.OverridesProperty(property, targetProperty));
                    if (implementation != null)
                    {
                        break;
                    }
                }
            }

            if (implementation is IPropertySymbol propertySymbol && propertySymbol.SetMethod != null)
            {
                targets.Add(propertySymbol.SetMethod.OriginalDefinition);
            }
            else if (implementation is IMethodSymbol methodSymbol)
            {
                targets.Add(methodSymbol.OriginalDefinition);
            }
        }

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

    internal static class PropertyDispatchHelper
    {
        internal static INamedTypeSymbol? GetKnownReceiverType(IOperation? instanceOperation)
        {
            var unwrapped = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation);
            if (unwrapped is IObjectCreationOperation objectCreationOperation)
            {
                return objectCreationOperation.Type as INamedTypeSymbol;
            }

            return unwrapped?.Type as INamedTypeSymbol;
        }

        internal static IPropertySymbol GetRootOverriddenProperty(IPropertySymbol propertySymbol)
        {
            var current = propertySymbol;
            while (current.OverriddenProperty != null)
            {
                current = current.OverriddenProperty;
            }

            return current.OriginalDefinition;
        }

        internal static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
        {
            var current = property;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }

                current = current.OverriddenProperty;
            }

            return false;
        }

        internal static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            return type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, interfaceSymbol.OriginalDefinition));
        }

        internal static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                foreach (var nested in EnumerateTypeAndNestedTypes(type))
                {
                    yield return nested;
                }
            }

            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in EnumerateAllNamedTypes(nestedNamespace))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNestedTypes(INamedTypeSymbol typeSymbol)
        {
            yield return typeSymbol;

            foreach (var nestedType in typeSymbol.GetTypeMembers())
            {
                foreach (var nested in EnumerateTypeAndNestedTypes(nestedType))
                {
                    yield return nested;
                }
            }
        }
    }

    internal static class RuleAnalysisHelper
    {
        internal static bool HasAssignmentToLocalBetweenDeclarationAndObservation(
            ILocalSymbol localSymbol,
            SyntaxNode observationSyntax,
            VariableDeclaratorSyntax declaratorSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var start = declaratorSyntax.Span.End;
            var end = observationSyntax.SpanStart;
            if (end <= start)
            {
                return false;
            }

            foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.SpanStart < start || assignment.SpanStart >= end)
                {
                    continue;
                }

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (SymbolEqualityComparer.Default.Equals(assignedSymbol, localSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TryGetConstantCondition(IConditionalOperation conditionalOperation, out bool conditionValue)
        {
            var constantValue = conditionalOperation.Condition.ConstantValue;
            if (constantValue.HasValue && constantValue.Value is bool boolValue)
            {
                conditionValue = boolValue;
                return true;
            }

            conditionValue = false;
            return false;
        }

        internal static bool IsFreshMutableEscapingReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.IsValueType ||
                namedType.SpecialType == SpecialType.System_String ||
                namedType.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            return namedType.GetMembers().Any(member =>
                member switch
                {
                    IFieldSymbol field => !field.IsStatic &&
                                          !field.IsReadOnly &&
                                          field.DeclaredAccessibility != Accessibility.Private,
                    IPropertySymbol property => !property.IsStatic &&
                                                property.SetMethod != null &&
                                                !property.SetMethod.IsInitOnly &&
                                                property.SetMethod.DeclaredAccessibility != Accessibility.Private,
                    _ => false
                });
        }
    }
}
