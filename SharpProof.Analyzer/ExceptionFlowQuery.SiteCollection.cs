using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionSources = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionSources;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Analyzer
{
    internal static partial class ExceptionFlowQuery
    {
        private static IEnumerable<UncaughtExceptionSiteEntry> CollectUncaughtExceptionSiteEntries(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var throwNode in ExceptionFlowAnalyzer.GetThrowNodes(methodNode))
            {
                if (IsInStaticallyUnreachableBranch(throwNode, semanticModel, cancellationToken, smtAnalysis))
                {
                    continue;
                }

                if (IsShadowedByThrowingFinally(throwNode, semanticModel, cancellationToken, smtAnalysis))
                {
                    continue;
                }

                var isDefinitelyThrowNull = ExceptionFlowAnalyzer.IsDefinitelyThrowNull(
                    throwNode,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis);
                var exceptionType = isDefinitelyThrowNull
                    ? semanticModel.Compilation.GetTypeByMetadataName(ExceptionTypes.NullReferenceException)
                    : ExceptionFlowAnalyzer.GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
                if (IsCaughtWithinMethod(throwNode, exceptionType, methodNode, semanticModel, cancellationToken, smtAnalysis))
                {
                    continue;
                }

                yield return new UncaughtExceptionSiteEntry(
                    throwNode,
                    methodSymbol,
                    new ExceptionCandidate(
                        exceptionType,
                        isDefinitelyThrowNull
                            ? ExceptionTypes.NullReferenceException
                            : exceptionType?.ToDisplayString(ExceptionTypeDisplayFormat) ?? ExceptionTypes.Unknown,
                        isDefinitelyThrowNull
                            ? ExceptionCategories.DefiniteThrowNull
                            : IsRethrow(throwNode) ? ExceptionCategories.Rethrow : ExceptionCategories.DirectThrow,
                        ExceptionSources.Throw));
            }

            foreach (var calleeCallSite in ExceptionFlowAnalyzer.GetCalleeCallSites(methodNode, semanticModel, cancellationToken))
            {
                if (!ExceptionFlowAnalyzer.IsMethodCallCandidatePathReachable(calleeCallSite, semanticModel, cancellationToken, smtAnalysis))
                {
                    continue;
                }

                if (IsShadowedByThrowingFinally(calleeCallSite.CallSite, semanticModel, cancellationToken, smtAnalysis))
                {
                    continue;
                }

                var calleeDisplay = calleeCallSite.Method.OriginalDefinition.ToDisplayString();
                if (calleeCallSite.IsDynamicDispatch)
                {
                    var dynamicDispatchException = new ExceptionCandidate(
                        null,
                        ExceptionTypes.Unknown,
                        ExceptionCategories.DynamicDispatch,
                        GetExceptionSourceMethodDisplay(calleeCallSite.Method.OriginalDefinition));
                    if (!IsCaughtWithinMethod(calleeCallSite.CallSite, dynamicDispatchException.Type, methodNode, semanticModel, cancellationToken, smtAnalysis))
                    {
                        yield return new UncaughtExceptionSiteEntry(
                            calleeCallSite.CallSite,
                            calleeCallSite.Method,
                            dynamicDispatchException,
                            calleeDisplay);
                    }
                }

                foreach (var exception in CollectCalleeExceptions(
                             calleeCallSite.Method,
                             semanticModel.Compilation,
                             cancellationToken,
                             exceptionSummaryCatalog,
                             visitedMethods,
                             smtAnalysis))
                {
                    if (IsCaughtWithinMethod(calleeCallSite.CallSite, exception.Type, methodNode, semanticModel, cancellationToken, smtAnalysis))
                    {
                        continue;
                    }

                    yield return new UncaughtExceptionSiteEntry(calleeCallSite.CallSite, calleeCallSite.Method, exception, calleeDisplay);
                }
            }

            foreach (var divideByZeroNode in ExceptionFlowAnalyzer.GetDefiniteDivideByZeroNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    divideByZeroNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.DivideByZeroException,
                    ExceptionCategories.DefiniteDivideByZero,
                    ExceptionSources.BinaryOperator);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var checkedOverflowNode in ExceptionFlowAnalyzer.GetDefiniteCheckedIntegralOverflowNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    checkedOverflowNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.OverflowException,
                    ExceptionCategories.DefiniteCheckedIntegralOverflow,
                    checkedOverflowNode is CastExpressionSyntax ? ExceptionSources.CheckedConversion : ExceptionSources.CheckedOperator);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var negativeArrayLengthNode in ExceptionFlowAnalyzer.GetDefiniteNegativeArrayLengthNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    negativeArrayLengthNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.OverflowException,
                    ExceptionCategories.DefiniteNegativeArrayLength,
                    ExceptionSources.ArrayLength);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var negativeStackAllocLengthHazard in CollectProvenNegativeStackAllocLengthHazards(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var stackAllocNode = FindHazardSiteNode(methodNode, negativeStackAllocLengthHazard);
                var entry = TryCreateProvenExceptionSiteEntry(
                    stackAllocNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.OverflowException,
                    ExceptionCategories.DefiniteNegativeStackAllocLength,
                    ExceptionSources.StackAllocLength);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var nullDereferenceNode in ExceptionFlowAnalyzer.GetDefiniteNullDereferenceNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    nullDereferenceNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.NullReferenceException,
                    nullDereferenceNode is AwaitExpressionSyntax ? ExceptionCategories.DefiniteAwaitNull : ExceptionCategories.DefiniteNullDereference,
                    nullDereferenceNode is AwaitExpressionSyntax ? ExceptionSources.AwaitExpression : ExceptionSources.NullReceiver);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var lockNullNode in ExceptionFlowAnalyzer.GetDefiniteLockNullNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    lockNullNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.ArgumentNullException,
                    ExceptionCategories.DefiniteLockNull,
                    ExceptionSources.LockReceiver);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var dynamicNullBindingSite in ExceptionFlowAnalyzer.GetDefiniteDynamicNullBindingSites(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    dynamicNullBindingSite.Site,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
                    dynamicNullBindingSite.Category,
                    dynamicNullBindingSite.Source);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var nullableValueAccessNode in ExceptionFlowAnalyzer.GetDefiniteNullableValueAccessNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    nullableValueAccessNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.InvalidOperationException,
                    ExceptionCategories.DefiniteNullableValueWithoutValue,
                    ExceptionSources.NullableValue);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var unboxNullCastNode in ExceptionFlowAnalyzer.GetDefiniteUnboxNullCastNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    unboxNullCastNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.NullReferenceException,
                    ExceptionCategories.DefiniteUnboxNull,
                    ExceptionSources.Cast);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var invalidCastNode in ExceptionFlowAnalyzer.GetDefiniteInvalidCastNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    invalidCastNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.InvalidCastException,
                    ExceptionCategories.DefiniteInvalidCast,
                    ExceptionSources.Cast);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var arrayTypeMismatchNode in ExceptionFlowAnalyzer.GetDefiniteArrayTypeMismatchStoreNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    arrayTypeMismatchNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.ArrayTypeMismatchException,
                    ExceptionCategories.DefiniteArrayTypeMismatch,
                    ExceptionSources.ArrayStore);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var indexOutOfRangeNode in ExceptionFlowAnalyzer.GetDefiniteIndexOutOfRangeNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    indexOutOfRangeNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.IndexOutOfRangeException,
                    ExceptionCategories.DefiniteIndexOutOfRange,
                    ExceptionSources.ArrayIndex);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var arrayGetValueNode in ExceptionFlowAnalyzer.GetDefiniteArrayGetValueIndexOutOfRangeNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    arrayGetValueNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.IndexOutOfRangeException,
                    ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                    ExceptionSources.ArrayGetValue);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var argumentOutOfRangeNode in ExceptionFlowAnalyzer.GetDefiniteArgumentOutOfRangeNodes(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var entry = TryCreateProvenExceptionSiteEntry(
                    argumentOutOfRangeNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.ArgumentOutOfRangeException,
                    ExceptionCategories.DefiniteRangeOutOfRange,
                    argumentOutOfRangeNode is InvocationExpressionSyntax ? ExceptionSources.SpanSlice : ExceptionSources.RangeSlice);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var countIndexHazard in CollectProvenCountIndexOutOfRangeHazards(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var countIndexNode = FindHazardSiteNode(methodNode, countIndexHazard);
                var entry = TryCreateProvenExceptionSiteEntry(
                    countIndexNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.ArgumentOutOfRangeException,
                    ExceptionCategories.DefiniteCountIndexOutOfRange,
                    ExceptionSources.CountIndex);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var switchNoMatchHazard in CollectProvenSwitchExpressionNoMatchHazards(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var switchExpressionNode = FindHazardSiteNode(methodNode, switchNoMatchHazard);
                var entry = TryCreateProvenExceptionSiteEntry(
                    switchExpressionNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    ExceptionTypes.SwitchExpressionException,
                    ExceptionCategories.DefiniteSwitchExpressionNoMatch,
                    ExceptionSources.SwitchExpression);
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }

            foreach (var symbolicHazard in CollectProvenAnalyzerOnlySymbolicHazards(methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                var hazardNode = FindHazardSiteNode(methodNode, symbolicHazard);
                var entry = TryCreateProvenExceptionSiteEntry(
                    hazardNode,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    smtAnalysis,
                    symbolicHazard.ExceptionType,
                    symbolicHazard.Category,
                    GetAnalyzerOnlySymbolicHazardSource(symbolicHazard.Category));
                if (entry == null)
                {
                    continue;
                }

                yield return entry;
            }
        }

        private static UncaughtExceptionSiteEntry? TryCreateProvenExceptionSiteEntry(
            SyntaxNode site,
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            SmtAnalysisService smtAnalysis,
            string exceptionMetadataName,
            string category,
            string source)
        {
            if (IsInStaticallyUnreachableBranch(site, semanticModel, cancellationToken, smtAnalysis))
            {
                return null;
            }

            if (IsShadowedByThrowingFinally(site, semanticModel, cancellationToken, smtAnalysis))
            {
                return null;
            }

            var exceptionType = semanticModel.Compilation.GetTypeByMetadataName(exceptionMetadataName);
            if (IsCaughtWithinMethod(site, exceptionType, methodNode, semanticModel, cancellationToken, smtAnalysis))
            {
                return null;
            }

            return new UncaughtExceptionSiteEntry(
                site,
                methodSymbol,
                new ExceptionCandidate(
                    exceptionType,
                    exceptionMetadataName,
                    category,
                    source));
        }
    }
}
