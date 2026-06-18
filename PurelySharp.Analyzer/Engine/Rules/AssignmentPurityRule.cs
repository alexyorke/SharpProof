using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
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
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax, targetResult.Evidence);
            }


            var setterResult = CheckPropertySetterPurity(targetOperation, context, currentState);
            if (!setterResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule] Property/indexer setter is IMPURE for assignment target {targetOperation.Syntax}.");
                return setterResult;
            }

            var targetSymbol = TryResolveSymbol(targetOperation);
            bool isPureAssignment = IsAssignmentTargetPure(targetOperation, context, targetSymbol, currentState);

            if (!isPureAssignment)
            {
                PurityAnalysisEngine.LogDebug($"    [AssignRule] Assignment target itself is considered impure for assignment. Assignment is Impure.");
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
            if (operatorMethod.Locations.FirstOrDefault()?.IsInMetadata == true &&
                PurityAnalysisEngine.TryGetTrustedGeneratedPurity(
                    operatorMethod.OriginalDefinition,
                    context.SemanticModel.Compilation,
                    out var generatedPurity))
            {
                if (generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (generatedPurity.IsImpure)
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

            if (PurityAnalysisEngine.IsKnownPureBCLMember(operatorMethod) ||
                PurityAnalysisEngine.HasPureExternalAttribute(operatorMethod))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
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
                GetTrackedLocalReceiverType(propertyReferenceOperation.Instance, currentState) ??
                    GetKnownReceiverType(propertyReferenceOperation.Instance));

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
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, out var concreteType)
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
                foreach (var type in EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace))
                {
                    if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                    {
                        continue;
                    }

                    if (!ImplementsInterface(type, targetProperty.ContainingType))
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

            var baseProperty = GetRootOverriddenProperty(targetProperty);
            var baseType = baseProperty.ContainingType;
            if (baseType != null)
            {
                foreach (var type in EnumerateAllNamedTypes(semanticModel.Compilation.Assembly.GlobalNamespace))
                {
                    if (!DerivesFrom(type, baseType))
                    {
                        continue;
                    }

                    foreach (var property in type.GetMembers(baseProperty.Name).OfType<IPropertySymbol>())
                    {
                        if (OverridesProperty(property, baseProperty) && property.SetMethod != null)
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

        private static INamedTypeSymbol? GetKnownReceiverType(IOperation? instanceOperation)
        {
            var unwrapped = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation);
            if (unwrapped is IObjectCreationOperation objectCreationOperation)
            {
                return objectCreationOperation.Type as INamedTypeSymbol;
            }

            return unwrapped?.Type as INamedTypeSymbol;
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
                            OverridesProperty(property, targetProperty));
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

        private static IPropertySymbol GetRootOverriddenProperty(IPropertySymbol propertySymbol)
        {
            var current = propertySymbol;
            while (current.OverriddenProperty != null)
            {
                current = current.OverriddenProperty;
            }

            return current.OriginalDefinition;
        }

        private static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
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

        private static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol namespaceSymbol)
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

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
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

        private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            return type.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, interfaceSymbol.OriginalDefinition));
        }

        private bool IsAssignmentTargetPure(IOperation targetOperation, PurityAnalysisContext context, ISymbol? targetSymbol, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            switch (targetOperation.Kind)
            {
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
                    if (IsOwnedFreshMutableObjectReference(fieldRefOp.Instance, fieldRefOp.Syntax, context))
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



                    if (IsOwnedFreshMutableObjectReference(propRefOp.Instance, propRefOp.Syntax, context))
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

            return operation is ILocalReferenceOperation localReference &&
                   currentState.IsOwnedLocalArraySymbol(localReference.Local);
        }

        private static bool IsOwnedFreshMutableObjectReference(
            IOperation? operation,
            SyntaxNode observationSyntax,
            PurityAnalysisContext context)
        {
            if (operation is IConversionOperation conversionOperation && conversionOperation.Operand != null)
            {
                return IsOwnedFreshMutableObjectReference(conversionOperation.Operand, observationSyntax, context);
            }

            if (operation is ILocalReferenceOperation localReference)
            {
                return HasStableFreshMutableObjectValue(
                    localReference.Local,
                    observationSyntax,
                    context.SemanticModel,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
            }

            return operation is IFieldReferenceOperation fieldReference &&
                   fieldReference.Field.IsReadOnly &&
                   IsOwnedFreshMutableReadonlyFieldReference(fieldReference, observationSyntax, context.SemanticModel) ||
                   operation is IPropertyReferenceOperation propertyReference &&
                   IsOwnedFreshMutableStablePropertyReference(propertyReference, observationSyntax, context.SemanticModel);
        }

        private static bool IsOwnedFreshMutableReadonlyFieldReference(
            IFieldReferenceOperation fieldReferenceOperation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel)
        {
            if (!TryGetStableAssignedValue(
                    fieldReferenceOperation,
                    observationSyntax,
                    semanticModel,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    out var valueOperation))
            {
                return false;
            }

            return HasStableFreshMutableObjectValueInOperation(
                valueOperation,
                observationSyntax,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        }

        private static bool IsOwnedFreshMutableStablePropertyReference(
            IPropertyReferenceOperation propertyReferenceOperation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel)
        {
            if (propertyReferenceOperation.Property.SetMethod != null &&
                !propertyReferenceOperation.Property.SetMethod.IsInitOnly)
            {
                return false;
            }

            if (!TryGetStableAssignedValue(
                    propertyReferenceOperation,
                    observationSyntax,
                    semanticModel,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    out var valueOperation))
            {
                return false;
            }

            return HasStableFreshMutableObjectValueInOperation(
                valueOperation,
                observationSyntax,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        }

        private static bool TryGetStableAssignedValue(
            IFieldReferenceOperation fieldReferenceOperation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IOperation valueOperation)
        {
            if (!TryResolveStableObjectCreationInitializer(
                    fieldReferenceOperation.Instance,
                    observationSyntax,
                    semanticModel,
                    visitedLocals,
                    out var objectCreationOperation))
            {
                valueOperation = null!;
                return false;
            }

            foreach (var assignment in objectCreationOperation.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(GetReferencedMemberSymbol(assignment.Target), fieldReferenceOperation.Field))
                {
                    continue;
                }

                valueOperation = assignment.Value;
                return true;
            }

            if (objectCreationOperation.Constructor != null)
            {
                foreach (var argument in objectCreationOperation.Arguments)
                {
                    var parameter = argument.Parameter;
                    if (parameter != null &&
                        ConstructorStoresParameterInField(objectCreationOperation.Constructor, parameter, fieldReferenceOperation.Field, semanticModel))
                    {
                        valueOperation = argument.Value;
                        return true;
                    }
                }
            }

            valueOperation = null!;
            return false;
        }

        private static bool TryGetStableAssignedValue(
            IPropertyReferenceOperation propertyReferenceOperation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IOperation valueOperation)
        {
            if (!TryResolveStableObjectCreationInitializer(
                    propertyReferenceOperation.Instance,
                    observationSyntax,
                    semanticModel,
                    visitedLocals,
                    out var objectCreationOperation))
            {
                valueOperation = null!;
                return false;
            }

            foreach (var assignment in objectCreationOperation.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(GetReferencedMemberSymbol(assignment.Target), propertyReferenceOperation.Property))
                {
                    continue;
                }

                valueOperation = assignment.Value;
                return true;
            }

            if (objectCreationOperation.Constructor != null)
            {
                foreach (var argument in objectCreationOperation.Arguments)
                {
                    var parameter = argument.Parameter;
                    if (parameter != null &&
                        ConstructorStoresParameterInProperty(objectCreationOperation.Constructor, parameter, propertyReferenceOperation.Property, semanticModel))
                    {
                        valueOperation = argument.Value;
                        return true;
                    }
                }
            }

            valueOperation = null!;
            return false;
        }

        private static bool TryResolveStableObjectCreationInitializer(
            IOperation? operation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IObjectCreationOperation objectCreationOperation)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            switch (unwrappedOperation)
            {
                case IObjectCreationOperation directObjectCreation:
                    objectCreationOperation = directObjectCreation;
                    return true;

                case ILocalReferenceOperation localReference:
                    return TryGetStableLocalObjectCreationInitializer(
                        localReference.Local,
                        observationSyntax,
                        semanticModel,
                        visitedLocals,
                        out objectCreationOperation);

                case IInvocationOperation invocationOperation
                    when PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                        invocationOperation,
                        semanticModel,
                        out var returnedOperation,
                        out _,
                        out var returnedSemanticModel):
                    return TryResolveStableObjectCreationInitializer(
                        returnedOperation,
                        observationSyntax,
                        returnedSemanticModel,
                        visitedLocals,
                        out objectCreationOperation);

                case IFieldReferenceOperation fieldReference when fieldReference.Field.IsReadOnly &&
                                                                  TryGetStableAssignedValue(fieldReference, observationSyntax, semanticModel, visitedLocals, out var fieldValue):
                    return TryResolveStableObjectCreationInitializer(fieldValue, observationSyntax, semanticModel, visitedLocals, out objectCreationOperation);

                case IPropertyReferenceOperation propertyReference
                    when (propertyReference.Property.SetMethod == null || propertyReference.Property.SetMethod.IsInitOnly) &&
                         TryGetStableAssignedValue(propertyReference, observationSyntax, semanticModel, visitedLocals, out var propertyValue):
                    return TryResolveStableObjectCreationInitializer(propertyValue, observationSyntax, semanticModel, visitedLocals, out objectCreationOperation);

                default:
                    objectCreationOperation = null!;
                    return false;
            }
        }

        private static bool TryGetStableLocalObjectCreationInitializer(
            ILocalSymbol localSymbol,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IObjectCreationOperation objectCreationOperation)
        {
            if (!visitedLocals.Add(localSymbol))
            {
                objectCreationOperation = null!;
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null ||
                HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, observationSyntax, declaratorSyntax, semanticModel))
            {
                objectCreationOperation = null!;
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation directObjectCreation)
            {
                objectCreationOperation = directObjectCreation;
                return true;
            }

            if (initializerOperation is ILocalReferenceOperation localReference)
            {
                return TryGetStableLocalObjectCreationInitializer(
                    localReference.Local,
                    initializerSyntax,
                    semanticModel,
                    visitedLocals,
                    out objectCreationOperation);
            }

            if (initializerOperation is IInvocationOperation invocationOperation &&
                PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                    invocationOperation,
                    semanticModel,
                    out var returnedOperation,
                    out _,
                    out var returnedSemanticModel))
            {
                return TryResolveStableObjectCreationInitializer(
                    returnedOperation,
                    initializerSyntax,
                    returnedSemanticModel,
                    visitedLocals,
                    out objectCreationOperation);
            }

            objectCreationOperation = null!;
            return false;
        }

        private static bool ConstructorStoresParameterInField(
            IMethodSymbol constructor,
            IParameterSymbol parameter,
            IFieldSymbol fieldSymbol,
            SemanticModel semanticModel)
        {
            foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
            {
                var constructorSyntax = syntaxReference.GetSyntax();
                var constructorModel = semanticModel.Compilation.GetSemanticModel(constructorSyntax.SyntaxTree);
                foreach (var assignment in constructorSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (constructorModel.GetOperation(assignment) is not ISimpleAssignmentOperation assignmentOperation)
                    {
                        continue;
                    }

                    if (PurityAnalysisEngine.SkipImplicitConversions(assignmentOperation.Value) is not IParameterReferenceOperation parameterReference ||
                        !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter) ||
                        assignmentOperation.Target is not IFieldReferenceOperation fieldReference ||
                        !SymbolEqualityComparer.Default.Equals(fieldReference.Field, fieldSymbol) ||
                        !IsThisOrImplicitInstance(fieldReference.Instance))
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool ConstructorStoresParameterInProperty(
            IMethodSymbol constructor,
            IParameterSymbol parameter,
            IPropertySymbol propertySymbol,
            SemanticModel semanticModel)
        {
            foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
            {
                var constructorSyntax = syntaxReference.GetSyntax();
                var constructorModel = semanticModel.Compilation.GetSemanticModel(constructorSyntax.SyntaxTree);
                foreach (var assignment in constructorSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (constructorModel.GetOperation(assignment) is not ISimpleAssignmentOperation assignmentOperation)
                    {
                        continue;
                    }

                    if (PurityAnalysisEngine.SkipImplicitConversions(assignmentOperation.Value) is not IParameterReferenceOperation parameterReference ||
                        !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter) ||
                        assignmentOperation.Target is not IPropertyReferenceOperation propertyReference ||
                        !SymbolEqualityComparer.Default.Equals(propertyReference.Property, propertySymbol) ||
                        !IsThisOrImplicitInstance(propertyReference.Instance))
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        private static ISymbol? GetReferencedMemberSymbol(IOperation? operation)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            return unwrappedOperation switch
            {
                IFieldReferenceOperation fieldReference => fieldReference.Field,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null
            };
        }

        private static bool IsThisOrImplicitInstance(IOperation? operation)
        {
            return operation == null ||
                   operation is IInstanceReferenceOperation instanceReference &&
                   instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance;
        }

        private static bool HasStableFreshMutableObjectValue(
            ILocalSymbol localSymbol,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals)
        {
            if (!visitedLocals.Add(localSymbol))
            {
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null)
            {
                return false;
            }

            if (HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, observationSyntax, declaratorSyntax, semanticModel))
            {
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
                IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            {
                return true;
            }

            if (initializerOperation is ILocalReferenceOperation localReference)
            {
                return HasStableFreshMutableObjectValue(localReference.Local, initializerSyntax, semanticModel, visitedLocals);
            }

            if (initializerOperation is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return HasStableFreshMutableObjectValueInOperation(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        initializerSyntax,
                        semanticModel,
                        visitedLocals);
                }

                return HasStableFreshMutableObjectValueInOperation(conditionalOperation.WhenTrue, initializerSyntax, semanticModel, visitedLocals) ||
                       HasStableFreshMutableObjectValueInOperation(conditionalOperation.WhenFalse, initializerSyntax, semanticModel, visitedLocals);
            }

            if (initializerOperation is ICoalesceOperation coalesceOperation)
            {
                return HasStableFreshMutableObjectValueInOperation(coalesceOperation.Value, initializerSyntax, semanticModel, visitedLocals) ||
                       HasStableFreshMutableObjectValueInOperation(coalesceOperation.WhenNull, initializerSyntax, semanticModel, visitedLocals);
            }

            return initializerOperation != null &&
                   HasStableFreshMutableObjectValueInOperation(
                       initializerOperation,
                       initializerSyntax,
                       semanticModel,
                       visitedLocals);
        }

        private static bool HasStableFreshMutableObjectValueInOperation(
            IOperation operation,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            if (unwrappedOperation is IObjectCreationOperation objectCreationOperation)
            {
                if (IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
                {
                    return true;
                }

                if (objectCreationOperation.Constructor != null)
                {
                    foreach (var argument in objectCreationOperation.Arguments)
                    {
                        var parameter = argument.Parameter;
                        if (parameter == null ||
                            !ConstructorStoresParameterInStableMember(objectCreationOperation.Constructor, parameter, semanticModel))
                        {
                            continue;
                        }

                        if (HasStableFreshMutableObjectValueInOperation(
                            argument.Value,
                            observationSyntax,
                            semanticModel,
                            visitedLocals))
                        {
                            return true;
                        }
                    }
                }
            }

            if (unwrappedOperation is ILocalReferenceOperation localReference)
            {
                return HasStableFreshMutableObjectValue(localReference.Local, observationSyntax, semanticModel, visitedLocals);
            }

            if (unwrappedOperation is IInvocationOperation invocationOperation &&
                PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                    invocationOperation,
                    semanticModel,
                    out var returnedOperation,
                    out _,
                    out var returnedSemanticModel))
            {
                return HasStableFreshMutableObjectValueInOperation(
                    returnedOperation,
                    observationSyntax,
                    returnedSemanticModel,
                    visitedLocals);
            }

            return false;
        }

        private static bool ConstructorStoresParameterInStableMember(
            IMethodSymbol constructor,
            IParameterSymbol parameter,
            SemanticModel semanticModel)
        {
            foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
            {
                var constructorSyntax = syntaxReference.GetSyntax();
                var constructorModel = semanticModel.Compilation.GetSemanticModel(constructorSyntax.SyntaxTree);
                foreach (var assignment in constructorSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (constructorModel.GetOperation(assignment) is not ISimpleAssignmentOperation assignmentOperation)
                    {
                        continue;
                    }

                    if (PurityAnalysisEngine.SkipImplicitConversions(assignmentOperation.Value) is not IParameterReferenceOperation parameterReference ||
                        !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter))
                    {
                        continue;
                    }

                    if (assignmentOperation.Target is IFieldReferenceOperation fieldReference &&
                        fieldReference.Field.IsReadOnly &&
                        IsThisOrImplicitInstance(fieldReference.Instance))
                    {
                        return true;
                    }

                    if (assignmentOperation.Target is IPropertyReferenceOperation propertyReference &&
                        (propertyReference.Property.SetMethod == null || propertyReference.Property.SetMethod.IsInitOnly) &&
                        IsThisOrImplicitInstance(propertyReference.Instance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAssignmentToLocalBetweenDeclarationAndObservation(
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

        private static bool TryGetConstantCondition(IConditionalOperation conditionalOperation, out bool conditionValue)
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

        private static bool IsFreshMutableEscapingReferenceType(ITypeSymbol? typeSymbol)
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
