using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Linq;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal static class OwnedFreshMutableObjectClassifier
    {
        internal static bool IsOwnedFreshMutableObjectReference(
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

        internal static bool IsOwnedFreshMutableReadonlyFieldReference(
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
                RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, observationSyntax, declaratorSyntax, semanticModel))
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

            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, observationSyntax, declaratorSyntax, semanticModel))
            {
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
                RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            {
                return true;
            }

            if (initializerOperation is ILocalReferenceOperation localReference)
            {
                return HasStableFreshMutableObjectValue(localReference.Local, initializerSyntax, semanticModel, visitedLocals);
            }

            if (initializerOperation is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
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
                if (RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
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
    }
}
