using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class PropertyReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.PropertyReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IPropertyReferenceOperation propertyReferenceOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            IPropertySymbol propertySymbol = propertyReferenceOperation.Property;
            PurityAnalysisEngine.LogDebug($"  [PropRefRule] Checking PropertyReference: {propertySymbol.Name} on Type: {propertySymbol.ContainingType?.ToDisplayString()}");

            if (IsCompilerGeneratedArrayForeachCurrent(propertyReferenceOperation, context))
            {
                PurityAnalysisEngine.LogDebug("    [PropRefRule] Compiler-generated array foreach Current is treated as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var argumentResult = CheckArguments(propertyReferenceOperation, context, currentState);
            if (!argumentResult.IsPure)
            {
                return argumentResult;
            }

            if (IsPartOfAssignmentTarget(propertyReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Skipping property read {propertySymbol.Name} as it's an assignment target.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryCheckDictionaryIndexerKeyDispatchPurity(propertyReferenceOperation, context, out var dictionaryIndexerResult))
            {
                return dictionaryIndexerResult;
            }

            if (TryCheckSortedDictionaryIndexerComparisonDispatchPurity(propertyReferenceOperation, context, out var sortedDictionaryIndexerResult))
            {
                return sortedDictionaryIndexerResult;
            }

            var isPureEnforcedProperty = PurityAnalysisEngine.IsPureEnforced(
                propertySymbol,
                context.EnforcePureAttributeSymbol,
                context.PureAttributeSymbol);
            var getterSymbol = propertySymbol.GetMethod;
            GeneratedPurityCatalog.PurityEntry generatedPurity = default;
            var hasTrustedGeneratedPurity = getterSymbol != null &&
                getterSymbol.Locations.FirstOrDefault()?.IsInMetadata == true &&
                PurityAnalysisEngine.TryGetTrustedGeneratedPurity(
                    getterSymbol.OriginalDefinition,
                    context.SemanticModel.Compilation,
                    out generatedPurity);
            var allowsKnownPureFallback = !hasTrustedGeneratedPurity;
            var requiresDispatchCheck = getterSymbol != null &&
                IsPotentiallyDispatchedProperty(propertySymbol, context.SemanticModel.Compilation);
            var dispatchGetterWasProvenPure = false;
            var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(propertySymbol);
            var hasConfiguredKnownImpureMember = string.Equals(
                knownImpureMemberSource,
                "config_known_impure",
                StringComparison.Ordinal);

            if (isPureEnforcedProperty && !requiresDispatchCheck)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} has [EnforcePure]. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }



            string impureSig = propertySymbol.OriginalDefinition.ToDisplayString();
            PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownImpure for property: '{impureSig}'");
            if (hasConfiguredKnownImpureMember)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is configured known impure. Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (!requiresDispatchCheck && hasTrustedGeneratedPurity)
            {
                if (generatedPurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter '{getterSymbol.ToDisplayString()}' is trusted pure from generated purity summary.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (generatedPurity.IsImpure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter '{getterSymbol.ToDisplayString()}' is trusted impure from generated purity summary.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        propertyReferenceOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            ruleName: nameof(PropertyReferencePurityRule),
                            operation: propertyReferenceOperation,
                            syntaxNode: propertyReferenceOperation.Syntax,
                            symbol: getterSymbol,
                        catalogSource: "generated_purity_summary"));
                }
            }

            if (knownImpureMemberSource != null)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is built-in known impure. Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (PurityAnalysisEngine.IsKnownImpure(propertySymbol))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is known impure via non-member fallback. Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: "known_impure_member"));
            }

            if (!requiresDispatchCheck &&
                getterSymbol != null &&
                getterSymbol.Locations.FirstOrDefault()?.IsInMetadata == true &&
                !hasTrustedGeneratedPurity &&
                string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source", StringComparison.Ordinal))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Metadata-backed reflection-sensitive property '{propertySymbol.ToDisplayString()}' has no trusted generated summary. Classifying as reflection/environment impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol));
            }

            if (PurityAnalysisEngine.IsInConfiguredImpureNamespaceOrType(propertySymbol) &&
                !PurityAnalysisEngine.IsConfiguredKnownPureMember(propertySymbol))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is in a known impure namespace or type. Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(propertySymbol),
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol,
                        catalogSource: "known_impure_namespace_or_type"));
            }

            if (!requiresDispatchCheck && IsSourceAutoPropertyGetter(propertySymbol))
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is a source auto-property getter. Treating read as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!requiresDispatchCheck &&
                propertySymbol.ContainingType is INamedTypeSymbol containingType &&
                containingType.IsAnonymousType &&
                propertySymbol.IsReadOnly)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} is an anonymous-type readonly property. Treating read as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (requiresDispatchCheck)
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} may dispatch. Checking getter candidates.");
                var dispatchResult = CheckDispatchedGetterPurity(
                    propertyReferenceOperation,
                    context,
                    currentState);
                if (!dispatchResult.IsPure)
                {
                    return dispatchResult;
                }

                dispatchGetterWasProvenPure = true;
                if (isPureEnforcedProperty)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property {propertySymbol.Name} has [EnforcePure] and dispatched getter candidates were pure. Assuming Pure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
            }


            if (propertySymbol.IsStatic)
            {

                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property access: {propertySymbol.Name}");

                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(propertySymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' access IMPURE due to impure static constructor in {propertySymbol.ContainingType?.Name}.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        cctorResult.ImpureSyntaxNode ?? propertyReferenceOperation.Syntax,
                        cctorResult.Evidence);
                }


                string staticPureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                bool staticKnownPure = allowsKnownPureFallback && PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol);
                PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownPureBCLMember for static property: '{staticPureSig}' -> {staticKnownPure}");

                if (staticKnownPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' is a known pure BCL member. Read is Pure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (propertySymbol.GetMethod != null)
                {
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' has a getter. Checking getter purity via service/recursion.");
                    var staticGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for static property '{propertySymbol.Name}': IsPure={staticGetterResult.IsPure}");
                    return GetterResultOrPure(staticGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                }

                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Static property '{propertySymbol.Name}' has no accessible getter to analyze and is not a known pure BCL member. Read is Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property access: {propertySymbol.Name}");
                IOperation? instanceOperation = propertyReferenceOperation.Instance;


                string instanceKind = instanceOperation?.Kind.ToString() ?? "null";
                string instanceSyntax = instanceOperation?.Syntax.ToString() ?? "null";
                PurityAnalysisEngine.LogDebug($"      [PropRefRule] Instance Operation Kind: {instanceKind}, Syntax: {instanceSyntax}");

                if (instanceOperation == null)
                {

                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance operation is null for property '{propertySymbol.Name}'. Assuming Impure for safety.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                }


                if (instanceOperation is IParameterReferenceOperation paramRef &&
                    (paramRef.Parameter.RefKind == RefKind.In ||
                     paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                     paramRef.Parameter.RefKind == (RefKind)4))
                {
                    bool isValueStruct = paramRef.Parameter.Type.IsValueType && !paramRef.Parameter.Type.IsReferenceType;
                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is ParameterReference '{paramRef.Parameter.Name}', RefKind={paramRef.Parameter.RefKind}, IsValueStruct={isValueStruct}");

                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    if (propertySymbol.GetMethod != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance '{paramRef.Parameter.Name}' is value struct or readonly ref. Checking getter purity via service/recursion.");
                        var parameterGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for '{propertySymbol.Name}' on parameter '{paramRef.Parameter.Name}': IsPure={parameterGetterResult.IsPure}");
                        return GetterResultOrPure(parameterGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                    }


                    PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance '{paramRef.Parameter.Name}' has no accessible getter to analyze. Read is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                }
                else if (instanceOperation is IInstanceReferenceOperation instanceRef && instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                {
                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    bool isReadonlyStruct = context.ContainingMethodSymbol?.ContainingType is { IsReadOnly: true, IsValueType: true };

                    if (isReadonlyStruct)
                    {

                        if (propertySymbol.GetMethod != null)
                        {
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this' within a readonly struct. Checking getter purity via service/recursion.");
                            var readonlyStructGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for readonly struct property '{propertySymbol.Name}': IsPure={readonlyStructGetterResult.IsPure}");
                            return GetterResultOrPure(readonlyStructGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                        }


                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this' within a readonly struct, but property '{propertySymbol.Name}' has no accessible getter to analyze. Read is Impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                    }
                    else if (propertySymbol.IsReadOnly)
                    {
                        if (propertySymbol.GetMethod != null)
                        {
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this', property '{propertySymbol.Name}' is readonly (get/init-only). Checking getter purity via service/recursion.");
                            var readonlyGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for readonly property '{propertySymbol.Name}': IsPure={readonlyGetterResult.IsPure}");
                            return GetterResultOrPure(readonlyGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                        }

                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this', property '{propertySymbol.Name}' has no accessible getter to analyze. Read is Impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                    }
                    else if (propertySymbol.GetMethod != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this', property '{propertySymbol.Name}' has a getter. Checking getter purity via service/recursion.");
                        var thisGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for '{propertySymbol.Name}': IsPure={thisGetterResult.IsPure}");

                        return GetterResultOrPure(thisGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is 'this', property '{propertySymbol.Name}' is not readonly and has no accessible getter to analyze. Read is Impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(propertyReferenceOperation.Syntax);
                    }
                }
                else
                {
                    var instanceExprResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
                    if (!instanceExprResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance expression for '{propertySymbol.Name}' is impure. Propagating.");
                        return instanceExprResult;
                    }

                    if (dispatchGetterWasProvenPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    string instancePureSig = propertySymbol.OriginalDefinition.ToDisplayString();
                    bool instanceKnownPure = allowsKnownPureFallback && PurityAnalysisEngine.IsKnownPureBCLMember(propertySymbol);
                    PurityAnalysisEngine.LogDebug($"      [PropRefRule] Checking IsKnownPureBCLMember for instance property: '{instancePureSig}' -> {instanceKnownPure}");

                    if (instanceKnownPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property '{propertySymbol.Name}' is known pure BCL. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    else if (propertySymbol.GetMethod != null && context.PureAttributeSymbol != null &&
                             PurityAnalysisEngine.HasAttribute(propertySymbol.GetMethod, context.PureAttributeSymbol))
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Property '{propertySymbol.Name}' getter has [Pure] attribute. Checking getter purity via service/recursion.");
                        var attributedGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for [Pure] property '{propertySymbol.Name}': IsPure={attributedGetterResult.IsPure}");
                        return GetterResultOrPure(attributedGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                    }

                    else if (propertySymbol.GetMethod != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is complex ({instanceKind}), property '{propertySymbol.Name}' has getter. Checking getter purity via service/recursion.");
                        var complexGetterResult = PurityAnalysisEngine.GetCalleePurity(propertySymbol.GetMethod, context);
                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Getter purity result for complex instance access to '{propertySymbol.Name}': IsPure={complexGetterResult.IsPure}");
                        return GetterResultOrPure(complexGetterResult, propertySymbol, propertySymbol.GetMethod, propertyReferenceOperation);
                    }

                    else
                    {

                        if (propertySymbol.GetMethod != null &&
                            context.PurityCache.TryGetValue(propertySymbol.GetMethod.OriginalDefinition, out var cachedGetterResult) &&
                            !cachedGetterResult.IsPure)
                        {
                            PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance is complex, property {propertySymbol.Name} known pure BCL, but getter is known impure from cache. Returning Impure.");
                            return cachedGetterResult.WithCallee(propertySymbol.GetMethod, propertyReferenceOperation.Syntax);
                        }


                        PurityAnalysisEngine.LogDebug($"    [PropRefRule] Instance property '{propertySymbol.Name}' is known pure BCL. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                }
            }


        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckArguments(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            foreach (var argument in propertyReferenceOperation.Arguments)
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

        private static bool IsSourceAutoPropertyGetter(IPropertySymbol propertySymbol)
        {
            if (propertySymbol.GetMethod == null ||
                propertySymbol.GetMethod.IsAbstract ||
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

                var getterAccessor = propertyDeclaration.AccessorList.Accessors
                    .FirstOrDefault(accessor => accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration));
                if (getterAccessor != null &&
                    getterAccessor.Body == null &&
                    getterAccessor.ExpressionBody == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPartOfAssignmentTarget(IOperation operation)
        {


            if (operation.Parent is IAssignmentOperation assignment && assignment.Target == operation)
            {
                return true;
            }
            if (operation.Parent is ICompoundAssignmentOperation compoundAssignment && compoundAssignment.Target == operation)
            {
                return true;
            }
            if (operation.Parent is IIncrementOrDecrementOperation incrementOrDecrement && incrementOrDecrement.Target == operation)
            {
                return true;
            }
            return false;
        }

        private static bool TryCheckDictionaryIndexerKeyDispatchPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var propertySymbol = propertyReferenceOperation.Property;
            var typeDefinition = (propertySymbol.ContainingType as INamedTypeSymbol)?.OriginalDefinition.ToDisplayString();
            if (!propertySymbol.IsIndexer ||
                propertySymbol.ContainingType is not INamedTypeSymbol containingType ||
                containingType.TypeArguments.Length != 2 ||
                (typeDefinition != "System.Collections.Generic.Dictionary<TKey, TValue>" &&
                 typeDefinition != "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>") ||
                propertyReferenceOperation.Arguments.Length == 0)
            {
                return false;
            }

            var keyType = containingType.TypeArguments[0];
            var receiverComparerResult = CheckDictionaryReceiverComparerPurity(propertyReferenceOperation, context);
            if (!receiverComparerResult.IsPure)
            {
                result = receiverComparerResult;
                return true;
            }

            result = CheckDictionaryKeyDispatchPurity(keyType, propertyReferenceOperation, context);
            return true;
        }

        private static bool TryCheckSortedDictionaryIndexerComparisonDispatchPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var propertySymbol = propertyReferenceOperation.Property;
            var typeDefinition = (propertySymbol.ContainingType as INamedTypeSymbol)?.OriginalDefinition.ToDisplayString();
            if (!propertySymbol.IsIndexer ||
                propertySymbol.ContainingType is not INamedTypeSymbol containingType ||
                containingType.TypeArguments.Length != 2 ||
                (typeDefinition != "System.Collections.Generic.SortedDictionary<TKey, TValue>" &&
                 typeDefinition != "System.Collections.Generic.SortedList<TKey, TValue>" &&
                 typeDefinition != "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>") ||
                propertyReferenceOperation.Arguments.Length == 0)
            {
                return false;
            }

            var keyType = containingType.TypeArguments[0];
            result = CheckSortedDictionaryKeyDispatchPurity(keyType, propertyReferenceOperation, context);
            return true;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDictionaryKeyDispatchPurity(
            ITypeSymbol keyType,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            if (IsBuiltinValueKey(keyType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!TryGetObjectOverride(keyType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
            {
                return UnknownKeyDispatch(propertyReferenceOperation);
            }

            var hashPurity = CheckResolvedKeyImplementation(getHashCodeOverride, propertyReferenceOperation, context);
            if (!hashPurity.IsPure)
            {
                return hashPurity;
            }

            if (TryGetIEquatableEqualsImplementation(keyType, out var equalsImplementation))
            {
                return CheckResolvedKeyImplementation(equalsImplementation, propertyReferenceOperation, context);
            }

            if (TryGetObjectOverride(keyType, nameof(object.Equals), parameterCount: 1, out var objectEqualsOverride))
            {
                return CheckResolvedKeyImplementation(objectEqualsOverride, propertyReferenceOperation, context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDictionaryReceiverComparerPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(propertyReferenceOperation.Instance) ??
                propertyReferenceOperation.Instance;
            var knownConstructionComparerResult = CheckKnownDictionaryConstructionComparerPurity(
                receiverOperation,
                propertyReferenceOperation,
                context);
            if (!knownConstructionComparerResult.IsPure)
            {
                return knownConstructionComparerResult;
            }

            if (receiverOperation?.Type is not INamedTypeSymbol receiverType ||
                receiverType.DeclaringSyntaxReferences.Length == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var constructor in receiverType.InstanceConstructors)
            {
                foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
                {
                    if (syntaxReference.GetSyntax(context.CancellationToken) is not ConstructorDeclarationSyntax constructorSyntax ||
                        constructorSyntax.Initializer == null)
                    {
                        continue;
                    }

                    foreach (var argument in constructorSyntax.Initializer.ArgumentList.Arguments)
                    {
                        var argumentOperation = context.SemanticModel.GetOperation(argument.Expression, context.CancellationToken);
                        var value = PurityAnalysisEngine.SkipImplicitConversions(argumentOperation) ?? argumentOperation;
                        if (value?.Type == null || !IsComparerOrDerivedInterface(value.Type))
                        {
                            continue;
                        }

                        var comparerResult = CheckComparerValuePurity(value, propertyReferenceOperation, context);
                        if (!comparerResult.IsPure)
                        {
                            return comparerResult;
                        }
                    }
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckKnownDictionaryConstructionComparerPurity(
            IOperation? receiverOperation,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            var unwrappedReceiver = PurityAnalysisEngine.SkipImplicitConversions(receiverOperation) ?? receiverOperation;
            if (unwrappedReceiver is IObjectCreationOperation objectCreationOperation)
            {
                return CheckDictionaryObjectCreationComparerPurity(
                    objectCreationOperation,
                    propertyReferenceOperation,
                    context);
            }

            if (TryGetReceiverInitializerOperation(unwrappedReceiver, context, out var initializerOperation) &&
                PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is IObjectCreationOperation initializerObjectCreation)
            {
                return CheckDictionaryObjectCreationComparerPurity(
                    initializerObjectCreation,
                    propertyReferenceOperation,
                    context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool TryGetReceiverInitializerOperation(
            IOperation? receiverOperation,
            PurityAnalysisContext context,
            out IOperation initializerOperation)
        {
            ISymbol? receiverSymbol = receiverOperation switch
            {
                IFieldReferenceOperation fieldReference => fieldReference.Field,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null
            };

            if (receiverSymbol == null)
            {
                initializerOperation = null!;
                return false;
            }

            foreach (var syntaxReference in receiverSymbol.DeclaringSyntaxReferences)
            {
                SyntaxNode? initializerSyntax = syntaxReference.GetSyntax(context.CancellationToken) switch
                {
                    VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer?.Value,
                    PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Initializer?.Value,
                    _ => null
                };

                if (initializerSyntax == null)
                {
                    continue;
                }

                var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(initializerSyntax.SyntaxTree);
                var operation = semanticModel.GetOperation(initializerSyntax, context.CancellationToken);
                if (operation != null)
                {
                    initializerOperation = operation;
                    return true;
                }
            }

            initializerOperation = null!;
            return false;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDictionaryObjectCreationComparerPurity(
            IObjectCreationOperation objectCreationOperation,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            if (objectCreationOperation.Type is not INamedTypeSymbol objectType ||
                objectType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
                if (value?.Type == null ||
                    argument.Parameter?.Type is not INamedTypeSymbol parameterType ||
                    !IsComparerOrDerivedInterface(parameterType) &&
                    !IsComparerOrDerivedInterface(value.Type))
                {
                    continue;
                }

                var comparerArgumentResult = PurityAnalysisEngine.CheckSingleOperation(value, context, PurityAnalysisEngine.PurityAnalysisState.Pure);
                if (!comparerArgumentResult.IsPure)
                {
                    return comparerArgumentResult;
                }

                var comparerResult = CheckComparerValuePurity(value, propertyReferenceOperation, context);
                if (!comparerResult.IsPure)
                {
                    return comparerResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckComparerValuePurity(
            IOperation value,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            var foundImplementation = false;
            foreach (var comparerMethod in EnumerateComparerImplementations(value.Type!))
            {
                foundImplementation = true;
                var comparerPurity = PurityAnalysisEngine.GetCalleePurity(comparerMethod.OriginalDefinition, context);
                if (!comparerPurity.IsPure)
                {
                    return comparerPurity.WithCallee(comparerMethod.OriginalDefinition, propertyReferenceOperation.Syntax);
                }
            }

            if (!foundImplementation && IsUnresolvedComparerDispatch(value.Type!))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(value)));
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static IEnumerable<IMethodSymbol> EnumerateComparerImplementations(ITypeSymbol comparerType)
        {
            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                yield break;
            }

            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var interfaceType in namedComparerType.AllInterfaces)
            {
                if (!IsComparerInterface(interfaceType))
                {
                    continue;
                }

                foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = namedComparerType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                    if (implementation == null || implementation.DeclaringSyntaxReferences.Length == 0)
                    {
                        continue;
                    }

                    if (seen.Add(implementation.OriginalDefinition))
                    {
                        yield return implementation;
                    }
                }
            }
        }

        private static bool IsComparerInterface(INamedTypeSymbol typeSymbol)
        {
            var displayString = typeSymbol.OriginalDefinition.ToDisplayString();
            return displayString == "System.Collections.Generic.IEqualityComparer<T>" ||
                displayString == "System.Collections.Generic.IComparer<T>";
        }

        private static bool IsUnresolvedComparerDispatch(ITypeSymbol comparerType)
        {
            if (comparerType is ITypeParameterSymbol typeParameter)
            {
                return typeParameter.ConstraintTypes
                    .OfType<INamedTypeSymbol>()
                    .Any(IsComparerOrDerivedInterface);
            }

            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                return false;
            }

            if (IsComparerInterface(namedComparerType))
            {
                return true;
            }

            if (namedComparerType.TypeKind != TypeKind.Interface && !namedComparerType.IsAbstract)
            {
                return false;
            }

            return IsComparerOrDerivedInterface(namedComparerType);
        }

        private static bool IsComparerOrDerivedInterface(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                (IsComparerInterface(namedType) || namedType.AllInterfaces.Any(IsComparerInterface));
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckSortedDictionaryKeyDispatchPurity(
            ITypeSymbol keyType,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            if (IsBuiltinValueKey(keyType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryGetIComparableCompareToImplementation(keyType, out var compareToImplementation))
            {
                return CheckResolvedKeyImplementation(compareToImplementation, propertyReferenceOperation, context);
            }

            if (TryGetIComparableObjectCompareToImplementation(keyType, out var objectCompareToImplementation))
            {
                return CheckResolvedKeyImplementation(objectCompareToImplementation, propertyReferenceOperation, context);
            }

            return UnknownKeyDispatch(propertyReferenceOperation);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedKeyImplementation(
            IMethodSymbol implementation,
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            if (implementation.DeclaringSyntaxReferences.Length == 0 &&
                !PurityAnalysisEngine.HasTrustedGeneratedPurityCoverage(implementation, context.SemanticModel.Compilation) &&
                !PurityAnalysisEngine.HasPureExternalAttribute(implementation))
            {
                return UnknownKeyDispatch(propertyReferenceOperation, implementation);
            }

            var implementationPurity = PurityAnalysisEngine.GetCalleePurity(implementation.OriginalDefinition, context);
            return implementationPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : implementationPurity.WithCallee(implementation.OriginalDefinition, propertyReferenceOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult UnknownKeyDispatch(
            IPropertyReferenceOperation propertyReferenceOperation,
            ISymbol? symbol = null)
        {
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                propertyReferenceOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(PropertyReferencePurityRule),
                    propertyReferenceOperation,
                    symbol: symbol ?? propertyReferenceOperation.Property.GetMethod));
        }

        private static bool TryGetIEquatableEqualsImplementation(
            ITypeSymbol keyType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.OriginalDefinition.ToDisplayString() != "System.IEquatable<T>" ||
                    interfaceType.TypeArguments.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], keyType))
                {
                    continue;
                }

                var interfaceEquals = interfaceType
                    .GetMembers(nameof(IEquatable<object>.Equals))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceEquals == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceEquals) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIComparableCompareToImplementation(
            ITypeSymbol keyType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.OriginalDefinition.ToDisplayString() != "System.IComparable<T>" ||
                    interfaceType.TypeArguments.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], keyType))
                {
                    continue;
                }

                var interfaceCompareTo = interfaceType
                    .GetMembers(nameof(IComparable<object>.CompareTo))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceCompareTo == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceCompareTo) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIComparableObjectCompareToImplementation(
            ITypeSymbol keyType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.ToDisplayString() != "System.IComparable")
                {
                    continue;
                }

                var interfaceCompareTo = interfaceType
                    .GetMembers(nameof(IComparable.CompareTo))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceCompareTo == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceCompareTo) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetObjectOverride(
            ITypeSymbol keyType,
            string memberName,
            int parameterCount,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var foundImplementation = namedType
                .GetMembers(memberName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.IsOverride && method.Parameters.Length == parameterCount);
            if (foundImplementation == null)
            {
                return false;
            }

            implementation = foundImplementation;
            return true;
        }

        private static bool IsBuiltinValueKey(ITypeSymbol keyType)
        {
            if (keyType.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            return keyType.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_Char or
                SpecialType.System_String;
        }

        private static bool IsPotentiallyDispatchedGetter(IMethodSymbol getterSymbol, Compilation compilation)
        {
            if (getterSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                getterSymbol.IsAbstract)
            {
                return true;
            }

            if (!getterSymbol.IsVirtual && !getterSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(getterSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !getterSymbol.IsSealed;
        }

        private static bool IsPotentiallyDispatchedProperty(IPropertySymbol propertySymbol, Compilation compilation)
        {
            return propertySymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                   propertySymbol.IsAbstract ||
                   (propertySymbol.GetMethod != null && IsPotentiallyDispatchedGetter(propertySymbol.GetMethod, compilation));
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedGetterPurity(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var hasExactReceiverType = PurityAnalysisEngine.TryResolveKnownConcreteType(
                propertyReferenceOperation.Instance,
                currentState,
                out var exactReceiverType);

            var knownReceiverType = hasExactReceiverType
                ? exactReceiverType
                : GetKnownReceiverType(propertyReferenceOperation.Instance);

            if (!hasExactReceiverType &&
                CanDispatchToUnknownGetterTarget(
                    propertyReferenceOperation.Property,
                    knownReceiverType,
                    context.SemanticModel.Compilation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.GetMethod));
            }

            var candidates = ResolvePotentialGetterTargets(
                propertyReferenceOperation.Property,
                context.SemanticModel,
                knownReceiverType,
                hasExactReceiverType);

            if (candidates.IsDefaultOrEmpty)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(PropertyReferencePurityRule),
                        propertyReferenceOperation,
                        symbol: propertyReferenceOperation.Property.GetMethod));
            }

            foreach (var getter in candidates)
            {
                var getterResult = PurityAnalysisEngine.GetCalleePurity(getter, context);
                if (!getterResult.IsPure)
                {
                    return getterResult.WithCallee(getter, propertyReferenceOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool CanDispatchToUnknownGetterTarget(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol? knownReceiverType,
            Compilation compilation)
        {
            if (knownReceiverType == null)
            {
                return true;
            }

            if (knownReceiverType.TypeKind == TypeKind.Interface)
            {
                return true;
            }

            if (knownReceiverType.TypeKind == TypeKind.Class &&
                !knownReceiverType.IsSealed &&
                IsPotentiallyDispatchedProperty(propertySymbol, compilation))
            {
                return true;
            }

            return false;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult GetterResultOrPure(
            PurityAnalysisEngine.PurityAnalysisResult getterResult,
            IPropertySymbol propertySymbol,
            IMethodSymbol getterSymbol,
            IPropertyReferenceOperation propertyReferenceOperation)
        {
            if (!getterResult.IsPure &&
                string.Equals(getterResult.Evidence.Category, "unknown_external_call", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(getterResult.Evidence.CatalogSource) &&
                string.Equals(GetCatalogHitCategory(propertySymbol), "reflection_environment_source", StringComparison.Ordinal))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    propertyReferenceOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        ruleName: nameof(PropertyReferencePurityRule),
                        operation: propertyReferenceOperation,
                        syntaxNode: propertyReferenceOperation.Syntax,
                        symbol: propertySymbol));
            }

            return getterResult.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : getterResult.WithCallee(getterSymbol, propertyReferenceOperation.Syntax);
        }

        private static INamedTypeSymbol? GetTrackedLocalReceiverType(
            IOperation? instanceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(instanceOperation, currentState, out var concreteType)
                ? concreteType
                : null;
        }

        private static string GetCatalogHitCategory(ISymbol symbol)
        {
            var containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
            var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            if (containingNamespace.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                containingType.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
                containingType == "System.Type" ||
                containingType == "System.Runtime.Loader.AssemblyLoadContext" ||
                containingType == "System.Environment" ||
                containingType == "System.DateTime" ||
                containingType == "System.DateTimeOffset" ||
                containingType == "System.TimeProvider" ||
                containingType == "System.TimeZoneInfo" ||
                containingType == "System.Diagnostics.Stopwatch")
            {
                return "reflection_environment_source";
            }

            return "catalog_hit";
        }

        private static ImmutableArray<IMethodSymbol> ResolvePotentialGetterTargets(
            IPropertySymbol propertySymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol? knownReceiverType,
            bool hasExactReceiverType)
        {
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var targetProperty = propertySymbol.OriginalDefinition;

            if (knownReceiverType != null && hasExactReceiverType)
            {
                AddGetterForReceiverType(knownReceiverType, targetProperty, targets);
                if (targets.Count == 0 &&
                    targetProperty.GetMethod != null &&
                    !targetProperty.GetMethod.IsAbstract)
                {
                    targets.Add(targetProperty.GetMethod.OriginalDefinition);
                }

                return targets.ToImmutableArray();
            }

            if (knownReceiverType != null &&
                (knownReceiverType.TypeKind == TypeKind.Struct || knownReceiverType.IsSealed))
            {
                AddGetterForReceiverType(knownReceiverType, targetProperty, targets);
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

                    AddGetterForReceiverType(type, targetProperty, targets);
                }

                if (targetProperty.GetMethod != null && !targetProperty.GetMethod.IsAbstract)
                {
                    targets.Add(targetProperty.GetMethod.OriginalDefinition);
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
                        if (OverridesProperty(property, baseProperty) && property.GetMethod != null)
                        {
                            targets.Add(property.GetMethod.OriginalDefinition);
                        }
                    }
                }
            }

            if (baseProperty.GetMethod != null && !baseProperty.GetMethod.IsAbstract)
            {
                targets.Add(baseProperty.GetMethod.OriginalDefinition);
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

        private static void AddGetterForReceiverType(
            INamedTypeSymbol receiverType,
            IPropertySymbol targetProperty,
            HashSet<IMethodSymbol> targets)
        {
            ISymbol? implementation = null;
            if (targetProperty.ContainingType?.TypeKind == TypeKind.Interface)
            {
                implementation = receiverType.FindImplementationForInterfaceMember(targetProperty) ??
                    (targetProperty.GetMethod == null
                        ? null
                        : receiverType.FindImplementationForInterfaceMember(targetProperty.GetMethod));
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

            if (implementation is IPropertySymbol propertySymbol && propertySymbol.GetMethod != null)
            {
                targets.Add(propertySymbol.GetMethod.OriginalDefinition);
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

        private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            return type.AllInterfaces.Any(
                candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    interfaceSymbol.OriginalDefinition));
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol potentialBase)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, potentialBase.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root)
        {
            foreach (var member in root.GetMembers())
            {
                if (member is INamespaceSymbol namespaceSymbol)
                {
                    foreach (var nested in EnumerateAllNamedTypes(namespaceSymbol))
                    {
                        yield return nested;
                    }
                }
                else if (member is INamedTypeSymbol typeSymbol)
                {
                    yield return typeSymbol;
                    foreach (var nested in EnumerateNestedTypes(typeSymbol))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol typeSymbol)
        {
            foreach (var member in typeSymbol.GetTypeMembers())
            {
                yield return member;
                foreach (var nested in EnumerateNestedTypes(member))
                {
                    yield return nested;
                }
            }
        }

        private static bool IsCompilerGeneratedArrayForeachCurrent(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context)
        {
            if (propertyReferenceOperation.Property.Name != "Current" ||
                propertyReferenceOperation.Property.ContainingType?.ToDisplayString() != "System.Collections.IEnumerator" ||
                propertyReferenceOperation.Syntax.Parent is not ForEachStatementSyntax forEachStatement)
            {
                return false;
            }

            return context.SemanticModel.GetTypeInfo(forEachStatement.Expression).Type is IArrayTypeSymbol;
        }


    }
}
