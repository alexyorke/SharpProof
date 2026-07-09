using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

        internal static bool ShouldAnalyzeCompoundAssignmentOperator(IMethodSymbol operatorMethod)
        {
            return operatorMethod.DeclaringSyntaxReferences.Length > 0 ||
                   IsKnownImpure(operatorMethod) ||
                   HasImpureAttribute(operatorMethod);
        }


        internal static PurityAnalysisEngine.PotentialTargets? ResolvePotentialTargets(
            IOperation valueOperation,
            PurityAnalysisState currentState,
            CancellationToken cancellationToken,
            SemanticModel? semanticModel = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unwrapped = SkipImplicitConversions(valueOperation);
            if (unwrapped == null) return null;
            if (unwrapped is IFlowCaptureReferenceOperation flowCaptureReference &&
                currentState.FlowCaptureTargets.TryGetValue(flowCaptureReference.Id, out var capturedTargets))
            {
                return capturedTargets;
            }

            if (unwrapped is IConditionalOperation conditionalOperation)
            {
                if (conditionalOperation.WhenTrue == null || conditionalOperation.WhenFalse == null)
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                var trueTargets = ResolvePotentialTargets(conditionalOperation.WhenTrue, currentState, cancellationToken, semanticModel);
                var falseTargets = ResolvePotentialTargets(conditionalOperation.WhenFalse, currentState, cancellationToken, semanticModel);
                if (trueTargets == null || falseTargets == null)
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                return PurityAnalysisEngine.PotentialTargets.Merge(trueTargets.Value, falseTargets.Value);
            }

            if (unwrapped is IMethodReferenceOperation methodRef)
            {
                if (IsPotentiallyDispatchedDelegateTarget(methodRef))
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                return PurityAnalysisEngine.PotentialTargets.FromSingle(methodRef.Method.OriginalDefinition);
            }

            if (unwrapped is IAnonymousFunctionOperation anonymousFunction && anonymousFunction.Symbol != null)
            {
                return PurityAnalysisEngine.PotentialTargets.FromSingle(anonymousFunction.Symbol.OriginalDefinition);
            }
            if (unwrapped is IFlowAnonymousFunctionOperation flowAnonymousFunction && flowAnonymousFunction.Symbol != null)
            {
                return PurityAnalysisEngine.PotentialTargets.FromSingle(flowAnonymousFunction.Symbol.OriginalDefinition);
            }

            if (unwrapped is IDelegateCreationOperation delegateCreation)
            {
                var target = SkipImplicitConversions(delegateCreation.Target);
                if (target is IMethodReferenceOperation lambdaRef)
                {
                    if (IsPotentiallyDispatchedDelegateTarget(lambdaRef))
                    {
                        return PurityAnalysisEngine.PotentialTargets.Unresolved;
                    }

                    return PurityAnalysisEngine.PotentialTargets.FromSingle(lambdaRef.Method.OriginalDefinition);
                }
                if (target is IAnonymousFunctionOperation anonymousTarget && anonymousTarget.Symbol != null)
                {
                    return PurityAnalysisEngine.PotentialTargets.FromSingle(anonymousTarget.Symbol.OriginalDefinition);
                }
                if (target is IFlowAnonymousFunctionOperation flowAnonymousTarget && flowAnonymousTarget.Symbol != null)
                {
                    return PurityAnalysisEngine.PotentialTargets.FromSingle(flowAnonymousTarget.Symbol.OriginalDefinition);
                }
            }

            ISymbol? valueSourceSymbol = TryResolveSymbol(unwrapped);
            if (valueSourceSymbol != null && currentState.DelegateTargetMap.TryGetValue(valueSourceSymbol, out var sourceTargets))
            {
                return sourceTargets;
            }

            if (valueSourceSymbol != null &&
                semanticModel != null &&
                CanTrustDelegateInitializerSymbol(valueSourceSymbol, semanticModel, cancellationToken))
            {
                var initializerTargets = TryResolveDelegateInitializerTargets(valueSourceSymbol, semanticModel, currentState, cancellationToken);
                if (initializerTargets != null)
                {
                    return initializerTargets;
                }
            }

            return null;
        }

        private static bool IsPotentiallyDispatchedDelegateTarget(IMethodReferenceOperation methodReference)
        {
            var method = methodReference.Method;
            if (method.IsSealed || method.ContainingType?.IsSealed == true)
            {
                return false;
            }

            if (method.ContainingType?.TypeKind != TypeKind.Interface &&
                !method.IsAbstract &&
                !method.IsVirtual &&
                !method.IsOverride)
            {
                return false;
            }

            if (methodReference.Instance == null)
            {
                return false;
            }

            if (SkipImplicitConversions(methodReference.Instance) is IObjectCreationOperation)
            {
                return false;
            }

            return methodReference.Instance.Type is not INamedTypeSymbol receiverType ||
                !receiverType.IsSealed;
        }

        private static bool CanTrustDelegateInitializerSymbol(
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (symbol is ILocalSymbol)
            {
                return true;
            }

            if (symbol is IFieldSymbol fieldSymbol)
            {
                return fieldSymbol.IsReadOnly &&
                    !HasAssignmentToField(fieldSymbol, semanticModel, cancellationToken);
            }

            return false;
        }

        private static bool HasAssignmentToField(
            IFieldSymbol fieldSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var syntaxReference in fieldSymbol.ContainingType.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax typeDeclaration)
                {
                    continue;
                }

                foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var model = semanticModel.Compilation.GetSemanticModel(assignment.SyntaxTree);
                    var targetOperation = model.GetOperation(assignment.Left, cancellationToken);
                    var targetSymbol = TryResolveSymbol(SkipImplicitConversions(targetOperation));
                    if (SymbolEqualityComparer.Default.Equals(targetSymbol, fieldSymbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static PurityAnalysisEngine.PotentialTargets? TryResolveDelegateInitializerTargets(
            ISymbol symbol,
            SemanticModel semanticModel,
            PurityAnalysisState currentState,
            CancellationToken cancellationToken)
        {
            foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var syntax = syntaxReference.GetSyntax(cancellationToken);
                var model = semanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);

                SyntaxNode? initializerSyntax = syntax switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax variableDeclaratorSyntax => variableDeclaratorSyntax.Initializer?.Value,
                    Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax propertyDeclarationSyntax => propertyDeclarationSyntax.Initializer?.Value,
                    _ => null
                };

                if (initializerSyntax == null)
                {
                    continue;
                }

                var initializerOperation = model.GetOperation(initializerSyntax, cancellationToken);
                if (initializerOperation == null)
                {
                    continue;
                }

                var initializerTargets = ResolvePotentialTargets(initializerOperation, currentState, cancellationToken, model);
                if (initializerTargets != null)
                {
                    return initializerTargets;
                }
            }

            return null;
        }

        internal static IOperation? SkipImplicitConversions(IOperation? operation)
        {
            while (operation is IConversionOperation conv && conv.IsImplicit)
            {
                operation = conv.Operand;
            }
            return operation;
        }


        internal static ISymbol? TryResolveSymbol(IOperation? operation)
        {
            return operation switch
            {
                ILocalReferenceOperation localRef => localRef.Local,
                IParameterReferenceOperation paramRef => paramRef.Parameter,
                IFieldReferenceOperation fieldRef => fieldRef.Field,
                IPropertyReferenceOperation propRef => propRef.Property,
                IEventReferenceOperation eventRef => eventRef.Event,
                _ => null
            };
        }

        internal static ISymbol? TryResolveTrackedSymbol(
            IOperation? operation,
            PurityAnalysisState currentState)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            var symbol = TryResolveSymbol(operation);
            if (symbol != null)
            {
                return symbol;
            }

            return operation is IFlowCaptureReferenceOperation flowCaptureReference &&
                   currentState.TryGetFlowCaptureSymbol(flowCaptureReference.Id, out var capturedSymbol)
                ? capturedSymbol
                : null;
        }

        private static bool IsTransientCharArrayConsumedByStringConstructor(IInvocationOperation invocationOperation, SemanticModel semanticModel)
        {
            var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
            var targetDefinition = targetMethod?.OriginalDefinition;
            if (targetDefinition == null ||
                targetDefinition.Name != "ToArray" ||
                invocationOperation.Type is not IArrayTypeSymbol arrayType ||
                arrayType.ElementType.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var enumerableType = semanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            if (enumerableType == null ||
                !SymbolEqualityComparer.Default.Equals(targetDefinition.ContainingType?.OriginalDefinition, enumerableType))
            {
                return false;
            }

            IOperation? parent = invocationOperation.Parent;
            if (parent is IArgumentOperation argumentOperation)
            {
                parent = argumentOperation.Parent;
            }

            if (parent is not IObjectCreationOperation objectCreationOperation)
            {
                return false;
            }

            var constructorSymbol = objectCreationOperation.Constructor;
            return constructorSymbol?.ContainingType?.SpecialType == SpecialType.System_String &&
                   objectCreationOperation.Arguments.Length == 1;
        }
    }
}
