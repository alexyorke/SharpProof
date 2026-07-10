using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class LoopPurityRule : IPurityRule
    {

        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Loop);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ILoopOperation loopOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }


            if (loopOperation is IForLoopOperation forLoopOperation)
            {
                foreach (var beforeOperation in forLoopOperation.Before)
                {
                    var beforeResult = PurityAnalysisEngine.CheckSingleOperation(beforeOperation, context, currentState);
                    if (!beforeResult.IsPure)
                    {
                        return beforeResult;
                    }
                }

                if (forLoopOperation.Condition != null)
                {
                    var conditionResult = PurityAnalysisEngine.CheckSingleOperation(forLoopOperation.Condition, context, currentState);
                    if (!conditionResult.IsPure)
                    {
                        return conditionResult;
                    }
                }
            }
            else if (loopOperation is IWhileLoopOperation whileLoopOperation &&
                whileLoopOperation.Condition != null)
            {
                var conditionResult = PurityAnalysisEngine.CheckSingleOperation(whileLoopOperation.Condition, context, currentState);
                if (!conditionResult.IsPure)
                {
                    return conditionResult;
                }
            }

            if (HasStaticallyUnreachableBody(loopOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (loopOperation is IForEachLoopOperation forEachLoopOperation)
            {
                var collectionResult = PurityAnalysisEngine.CheckSingleOperation(forEachLoopOperation.Collection, context, currentState);
                if (!collectionResult.IsPure)
                {
                    return collectionResult;
                }

                var enumeratorResult = CheckForEachEnumeratorPurity(forEachLoopOperation.Collection, context);
                if (!enumeratorResult.IsPure)
                {
                    return enumeratorResult;
                }
            }


            if (loopOperation.Body != null)
            {
                foreach (var bodyOp in loopOperation.Body.DescendantsAndSelf())
                {

                    var opResult = PurityAnalysisEngine.CheckSingleOperation(bodyOp, context, currentState);
                    if (!opResult.IsPure)
                    {
                        return opResult;
                    }
                }
            }

            if (loopOperation is IForLoopOperation reachableForLoopOperation)
            {
                foreach (var atLoopBottomOperation in reachableForLoopOperation.AtLoopBottom)
                {
                    var atLoopBottomResult = PurityAnalysisEngine.CheckSingleOperation(atLoopBottomOperation, context, currentState);
                    if (!atLoopBottomResult.IsPure)
                    {
                        return atLoopBottomResult;
                    }
                }
            }




            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool HasStaticallyUnreachableBody(ILoopOperation loopOperation)
        {
            return loopOperation switch
            {
                IWhileLoopOperation whileLoop => whileLoop.ConditionIsTop && IsCompileTimeFalse(whileLoop.Condition),
                IForLoopOperation forLoop => IsCompileTimeFalse(forLoop.Condition),
                _ => false
            };
        }

        private static bool IsCompileTimeFalse(IOperation? conditionOperation)
        {
            if (conditionOperation == null)
            {
                return false;
            }

            var constantValue = conditionOperation.ConstantValue;
            return constantValue.HasValue &&
                   constantValue.Value is bool boolValue &&
                   !boolValue;
        }

        internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorPurity(
            IOperation collectionOperation,
            PurityAnalysisContext context)
        {
            var unwrappedCollection = PurityAnalysisEngine.SkipImplicitConversions(collectionOperation) ?? collectionOperation;
            if (unwrappedCollection.Type == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var getEnumerator in EnumerateGetEnumeratorImplementations(unwrappedCollection.Type))
            {
                var enumeratorPurity = PurityAnalysisEngine.GetCalleePurity(getEnumerator.OriginalDefinition, context);
                if (!enumeratorPurity.IsPure)
                {
                    return enumeratorPurity.WithCallee(getEnumerator, unwrappedCollection.Syntax);
                }

                var runtimeMemberPurity = CheckForEachEnumeratorRuntimeMemberPurity(
                    getEnumerator.ReturnType,
                    unwrappedCollection.Syntax,
                    context);
                if (!runtimeMemberPurity.IsPure)
                {
                    return runtimeMemberPurity;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachAsyncEnumeratorPurity(
            IOperation collectionOperation,
            PurityAnalysisContext context)
        {
            var unwrappedCollection = PurityAnalysisEngine.SkipImplicitConversions(collectionOperation) ?? collectionOperation;
            if (unwrappedCollection.Type == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var getAsyncEnumerator in EnumerateGetAsyncEnumeratorImplementations(unwrappedCollection.Type))
            {
                var enumeratorPurity = PurityAnalysisEngine.GetCalleePurity(getAsyncEnumerator.OriginalDefinition, context);
                if (!enumeratorPurity.IsPure)
                {
                    return enumeratorPurity.WithCallee(getAsyncEnumerator, unwrappedCollection.Syntax);
                }

                var runtimeMemberPurity = CheckForEachAsyncEnumeratorRuntimeMemberPurity(
                    getAsyncEnumerator.ReturnType,
                    unwrappedCollection.Syntax,
                    context);
                if (!runtimeMemberPurity.IsPure)
                {
                    return runtimeMemberPurity;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorRuntimeMemberPurity(
            ITypeSymbol enumeratorType,
            SyntaxNode foreachSyntax,
            PurityAnalysisContext context)
        {
            if (enumeratorType.TypeKind == TypeKind.Interface ||
                enumeratorType.DeclaringSyntaxReferences.Length == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var runtimeMember in EnumerateEnumeratorRuntimeMembers(enumeratorType))
            {
                var memberPurity = PurityAnalysisEngine.GetCalleePurity(runtimeMember.OriginalDefinition, context);
                if (!memberPurity.IsPure)
                {
                    return memberPurity.WithCallee(runtimeMember, foreachSyntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static IEnumerable<IMethodSymbol> EnumerateEnumeratorRuntimeMembers(ITypeSymbol enumeratorType)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var method in EnumerateInstanceMethods(enumeratorType, "MoveNext", parameterCount: 0))
            {
                if (seen.Add(method.OriginalDefinition))
                {
                    yield return method;
                }
            }

            foreach (var getter in EnumerateCurrentGetters(enumeratorType))
            {
                if (seen.Add(getter.OriginalDefinition))
                {
                    yield return getter;
                }
            }

            foreach (var dispose in EnumerateDisposeImplementations(enumeratorType))
            {
                if (seen.Add(dispose.OriginalDefinition))
                {
                    yield return dispose;
                }
            }
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckForEachAsyncEnumeratorRuntimeMemberPurity(
            ITypeSymbol enumeratorType,
            SyntaxNode foreachSyntax,
            PurityAnalysisContext context)
        {
            if (enumeratorType.TypeKind == TypeKind.Interface ||
                enumeratorType.DeclaringSyntaxReferences.Length == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var runtimeMember in EnumerateAsyncEnumeratorRuntimeMembers(enumeratorType))
            {
                var memberPurity = PurityAnalysisEngine.GetCalleePurity(runtimeMember.OriginalDefinition, context);
                if (!memberPurity.IsPure)
                {
                    return memberPurity.WithCallee(runtimeMember, foreachSyntax);
                }

                if (runtimeMember.Name is "MoveNextAsync" or "DisposeAsync")
                {
                    var awaitablePurity = AwaitPurityRule.CheckAwaitablePatternMembers(
                        runtimeMember.ReturnType,
                        foreachSyntax,
                        context);
                    if (!awaitablePurity.IsPure)
                    {
                        return awaitablePurity;
                    }
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static IEnumerable<IMethodSymbol> EnumerateAsyncEnumeratorRuntimeMembers(ITypeSymbol enumeratorType)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var method in EnumerateInstanceMethods(enumeratorType, "MoveNextAsync", parameterCount: 0))
            {
                if (seen.Add(method.OriginalDefinition))
                {
                    yield return method;
                }
            }

            foreach (var getter in EnumerateCurrentGetters(enumeratorType))
            {
                if (seen.Add(getter.OriginalDefinition))
                {
                    yield return getter;
                }
            }

            foreach (var disposeAsync in EnumerateDisposeAsyncImplementations(enumeratorType))
            {
                if (seen.Add(disposeAsync.OriginalDefinition))
                {
                    yield return disposeAsync;
                }
            }
        }

        private static IEnumerable<IMethodSymbol> EnumerateInstanceMethods(
            ITypeSymbol type,
            string methodName,
            int parameterCount)
        {
            var current = type as INamedTypeSymbol;
            while (current != null)
            {
                foreach (var method in current
                             .GetMembers(methodName)
                             .OfType<IMethodSymbol>()
                             .Where(method =>
                                 !method.IsStatic &&
                                 method.Parameters.Length == parameterCount &&
                                 method.DeclaringSyntaxReferences.Length > 0))
                {
                    yield return method;
                }

                current = current.BaseType;
            }
        }

        private static IEnumerable<IMethodSymbol> EnumerateCurrentGetters(ITypeSymbol type)
        {
            var current = type as INamedTypeSymbol;
            while (current != null)
            {
                foreach (var property in current
                             .GetMembers("Current")
                             .OfType<IPropertySymbol>())
                {
                    if (property.GetMethod is { DeclaringSyntaxReferences.Length: > 0 } getter)
                    {
                        yield return getter;
                    }
                }

                current = current.BaseType;
            }
        }

        private static IEnumerable<IMethodSymbol> EnumerateDisposeImplementations(ITypeSymbol type)
        {
            foreach (var dispose in EnumerateInstanceMethods(type, "Dispose", parameterCount: 0))
            {
                yield return dispose;
            }

            if (type is not INamedTypeSymbol namedType)
            {
                yield break;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.SpecialType != SpecialType.System_IDisposable)
                {
                    continue;
                }

                foreach (var interfaceDispose in interfaceType
                             .GetMembers(nameof(System.IDisposable.Dispose))
                             .OfType<IMethodSymbol>()
                             .Where(method => method.Parameters.Length == 0))
                {
                    var implementation = namedType.FindImplementationForInterfaceMember(interfaceDispose) as IMethodSymbol;
                    if (implementation?.DeclaringSyntaxReferences.Length > 0)
                    {
                        yield return implementation;
                    }
                }
            }
        }

        private static IEnumerable<IMethodSymbol> EnumerateDisposeAsyncImplementations(ITypeSymbol type)
        {
            foreach (var disposeAsync in EnumerateInstanceMethods(type, "DisposeAsync", parameterCount: 0))
            {
                yield return disposeAsync;
            }

            if (type is not INamedTypeSymbol namedType)
            {
                yield break;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.ToDisplayString() != "System.IAsyncDisposable")
                {
                    continue;
                }

                foreach (var interfaceDisposeAsync in interfaceType
                             .GetMembers("DisposeAsync")
                             .OfType<IMethodSymbol>()
                             .Where(method => method.Parameters.Length == 0))
                {
                    var implementation = namedType.FindImplementationForInterfaceMember(interfaceDisposeAsync) as IMethodSymbol;
                    if (implementation?.DeclaringSyntaxReferences.Length > 0)
                    {
                        yield return implementation;
                    }
                }
            }
        }

        internal static IEnumerable<IMethodSymbol> EnumerateGetEnumeratorImplementations(ITypeSymbol collectionType)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var getEnumerator in collectionType
                         .GetMembers("GetEnumerator")
                         .OfType<IMethodSymbol>()
                         .Where(method => method.Parameters.Length == 0 && method.DeclaringSyntaxReferences.Length > 0))
            {
                if (seen.Add(getEnumerator.OriginalDefinition))
                {
                    yield return getEnumerator;
                }
            }

            if (collectionType is not INamedTypeSymbol namedCollectionType)
            {
                yield break;
            }

            foreach (var interfaceType in namedCollectionType.AllInterfaces)
            {
                if (!IsEnumerableInterface(interfaceType))
                {
                    continue;
                }

                foreach (var interfaceGetEnumerator in interfaceType
                             .GetMembers("GetEnumerator")
                             .OfType<IMethodSymbol>()
                             .Where(method => method.Parameters.Length == 0))
                {
                    var implementation = namedCollectionType.FindImplementationForInterfaceMember(interfaceGetEnumerator) as IMethodSymbol;
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

        private static IEnumerable<IMethodSymbol> EnumerateGetAsyncEnumeratorImplementations(ITypeSymbol collectionType)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var getAsyncEnumerator in collectionType
                         .GetMembers("GetAsyncEnumerator")
                         .OfType<IMethodSymbol>()
                         .Where(IsGetAsyncEnumeratorPatternMethod))
            {
                if (seen.Add(getAsyncEnumerator.OriginalDefinition))
                {
                    yield return getAsyncEnumerator;
                }
            }

            if (collectionType is not INamedTypeSymbol namedCollectionType)
            {
                yield break;
            }

            foreach (var interfaceType in namedCollectionType.AllInterfaces)
            {
                if (interfaceType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.IAsyncEnumerable<T>")
                {
                    continue;
                }

                foreach (var interfaceGetAsyncEnumerator in interfaceType
                             .GetMembers("GetAsyncEnumerator")
                             .OfType<IMethodSymbol>()
                             .Where(IsGetAsyncEnumeratorPatternMethod))
                {
                    var implementation = namedCollectionType.FindImplementationForInterfaceMember(interfaceGetAsyncEnumerator) as IMethodSymbol;
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

        private static bool IsGetAsyncEnumeratorPatternMethod(IMethodSymbol method)
        {
            if (method.IsStatic ||
                method.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            return method.Parameters.Length == 0 ||
                method.Parameters.Length == 1 && method.Parameters[0].IsOptional;
        }

        private static bool IsEnumerableInterface(INamedTypeSymbol typeSymbol)
        {
            var originalDefinition = typeSymbol.OriginalDefinition;
            return originalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable ||
                originalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
        }
    }
}
