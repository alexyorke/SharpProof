using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class FieldReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.FieldReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IFieldReferenceOperation fieldReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: FieldReferencePurityRule called with unexpected operation type: {operation.Kind}. Assuming Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }

            var fieldSymbol = fieldReferenceOperation.Field;
            if (fieldSymbol == null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Impure due to null fieldSymbol for {fieldReferenceOperation.Syntax.ToString()}");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);
            }


            if (IsPartOfAssignmentTarget(fieldReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Skipping field read {fieldSymbol.Name} as it's an assignment target.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (fieldSymbol.IsVolatile)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' is volatile - Impure read.");
                return ImpureFieldRead(fieldReferenceOperation, "volatile");
            }


            if (fieldSymbol.IsConst)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' is const - Pure");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (!fieldSymbol.IsStatic && fieldReferenceOperation.Instance != null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Checking instance expression for field '{fieldSymbol.Name}': {fieldReferenceOperation.Instance.Syntax} ({fieldReferenceOperation.Instance.Kind})");
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(fieldReferenceOperation.Instance, context, currentState);
                if (!instanceResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance expression is IMPURE. Field reference is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        instanceResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                        instanceResult.Evidence);
                }
            }


            if (fieldSymbol.IsStatic)
            {
                var staticCtorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(fieldSymbol.ContainingType, context, currentState);
                if (!staticCtorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static constructor trigger for field '{fieldSymbol.Name}' is IMPURE. Field reference is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        staticCtorResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                        staticCtorResult.Evidence);
                }

                if (fieldSymbol.IsReadOnly)
                {
                    if (PurityAnalysisEngine.IsKnownImpure(fieldSymbol))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is explicitly known impure.");
                        return ImpureFieldRead(fieldReferenceOperation, "known_impure_member");
                    }

                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' - Pure");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static non-readonly field '{fieldSymbol.Name}' - Impure");
                    return ImpureFieldRead(fieldReferenceOperation);
                }
            }


            if (fieldReferenceOperation.Instance != null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}'. Checking instance...");

                IOperation instanceOperation = fieldReferenceOperation.Instance;

                if (instanceOperation is IParameterReferenceOperation paramRef)
                {


                    bool isReadOnlyRef = paramRef.Parameter.RefKind == RefKind.In ||
                                         paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                                         paramRef.Parameter.RefKind == (RefKind)4;
                    bool isValueStruct = paramRef.Parameter.RefKind == RefKind.None && paramRef.Parameter.Type.IsValueType;

                    if (isReadOnlyRef || isValueStruct)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance '{paramRef.Parameter.Name}' is {(isValueStruct ? "value struct" : "readonly ref")}. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance '{paramRef.Parameter.Name}' is mutable ref/out. Read is Impure.");
                        return ImpureFieldRead(fieldReferenceOperation);
                    }
                }
                else if (instanceOperation is IInstanceReferenceOperation instanceRef && instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                {

                    bool isReadonlyStruct = context.ContainingMethodSymbol.ContainingType.IsReadOnly &&
                                            context.ContainingMethodSymbol.ContainingType.IsValueType;
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Checking 'this' instance. isReadonlyStruct={isReadonlyStruct}, fieldSymbol.IsReadOnly={fieldSymbol.IsReadOnly}");

                    if (isReadonlyStruct)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this' within a readonly struct. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else if (fieldSymbol.IsReadOnly)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this', field '{fieldSymbol.Name}' is readonly. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else if (PurityAnalysisEngine.IsStrictPurityProfile)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Strict profile: mutable instance field '{fieldSymbol.Name}' read from 'this' is Impure.");
                        return ImpureFieldRead(fieldReferenceOperation, "strict_profile");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this' within a non-readonly type and field '{fieldSymbol.Name}' is not readonly. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                }
                else
                {



                    var unwrappedInstance = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation) ?? instanceOperation;
                    if (IsByValueValueTypeReceiver(unwrappedInstance))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is a by-value value-type receiver ({unwrappedInstance.Kind}). Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    if (fieldSymbol.IsReadOnly &&
                        IsOwnedFreshMutableReadonlyFieldReference(fieldReferenceOperation, fieldReferenceOperation.Syntax, context.SemanticModel))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Readonly field '{fieldSymbol.Name}' on stable local wrapper carries an owned fresh mutable object. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    if (IsOwnedFreshMutableObjectReference(instanceOperation, fieldReferenceOperation.Syntax, context))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}' on fresh local object receiver. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }




                    var receiverResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
                    if (!receiverResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance operation is impure ({instanceOperation.Kind}). Read is impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            receiverResult.ImpureSyntaxNode ?? instanceOperation.Syntax,
                            receiverResult.Evidence);
                    }


                    string fieldPureSig = fieldSymbol.OriginalDefinition.ToDisplayString();
                    bool fieldKnownPure = PurityAnalysisEngine.IsKnownPureBCLMember(fieldSymbol);
                    PurityAnalysisEngine.LogDebug($"      [FieldRefRule] Checking IsKnownPureBCLMember for instance field accessed via {instanceOperation.Kind}: '{fieldPureSig}' -> {fieldKnownPure}");

                    if (fieldKnownPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}' is known pure BCL. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else
                    {

                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is complex ({instanceOperation.Kind})/non-readonly-local and field '{fieldSymbol.Name}' not known pure BCL. Assuming read is Impure.");
                        return ImpureFieldRead(fieldReferenceOperation);
                    }
                }
            }


            PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Unhandled case for field '{fieldSymbol.Name}'. Assuming Impure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult ImpureFieldRead(
            IFieldReferenceOperation fieldReferenceOperation,
            string? catalogSource = null)
        {
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                fieldReferenceOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "mutable_state_read",
                    ruleName: nameof(FieldReferencePurityRule),
                    operation: fieldReferenceOperation,
                    syntaxNode: fieldReferenceOperation.Syntax,
                    symbol: fieldReferenceOperation.Field,
                    catalogSource: catalogSource));
        }


        private bool IsPartOfAssignmentTarget(IOperation operation)
        {
            IOperation? parent = operation.Parent;
            while (parent != null)
            {
                if (parent is IAssignmentOperation assignmentOperation && assignmentOperation.Target == operation)
                {
                    return true;
                }

                if (parent is IExpressionStatementOperation || parent is IBlockOperation)
                {
                    return false;
                }

                if (parent is ICompoundAssignmentOperation compoundAssignment && compoundAssignment.Target == operation)
                {
                    return true;
                }
                if (parent is IIncrementOrDecrementOperation incrementOrDecrement && incrementOrDecrement.Target == operation)
                {
                    return true;
                }

                operation = parent;
                parent = parent.Parent;
            }
            return false;
        }

        private static bool IsByValueValueTypeReceiver(IOperation operation)
        {
            if (operation.Type == null || !operation.Type.IsValueType)
            {
                return false;
            }

            return operation switch
            {
                IObjectCreationOperation => true,
                IDefaultValueOperation => true,
                ILocalReferenceOperation localReference => localReference.Local.RefKind == RefKind.None,
                IParameterReferenceOperation parameterReference => parameterReference.Parameter.RefKind == RefKind.None,
                _ => false
            };
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
    }
}
