using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class MethodInvocationPurityRule
    {

        private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedEqualityImplementation(
            IMethodSymbol implementation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (implementation.DeclaringSyntaxReferences.Length == 0 &&
                !PurityAnalysisEngine.HasTrustedGeneratedPurityCoverage(implementation, context.SemanticModel.Compilation) &&
                !PurityAnalysisEngine.HasPureExternalAttribute(implementation))
            {
                return CreateUnknownExternalCallImpurity(invocationOperation, implementation);
            }

            var implementationPurity = PurityAnalysisEngine.GetCalleePurity(implementation.OriginalDefinition, context);
            return implementationPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : implementationPurity.WithCallee(implementation.OriginalDefinition, invocationOperation.Syntax);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CreateUnknownExternalCallImpurity(
            IInvocationOperation invocationOperation,
            ISymbol? symbol = null)
        {
            return PurityAnalysisEngine.ImpureResult(
                invocationOperation,
                "unknown_external_call",
                nameof(MethodInvocationPurityRule),
                symbol ?? invocationOperation.TargetMethod);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultHashDispatchPurity(
            ITypeSymbol elementType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
            {
                return CreateUnknownExternalCallImpurity(invocationOperation);
            }

            return CheckResolvedEqualityImplementation(
                getHashCodeOverride,
                invocationOperation,
                context);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultEqualityDispatchPurity(
            ITypeSymbol elementType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            bool requiresHashCode = false)
        {
            if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (requiresHashCode)
            {
                if (!DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
                {
                    return CreateUnknownExternalCallImpurity(invocationOperation);
                }

                var hashPurity = CheckResolvedEqualityImplementation(
                    getHashCodeOverride,
                    invocationOperation,
                    context);
                if (!hashPurity.IsPure)
                {
                    return hashPurity;
                }
            }

            if (DispatchedMemberResolution.TryGetIEquatableEqualsImplementation(elementType, out var equalsImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    equalsImplementation,
                    invocationOperation,
                    context);
            }

            if (DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.Equals), parameterCount: 1, out var objectEqualsOverride))
            {
                return CheckResolvedEqualityImplementation(
                    objectEqualsOverride,
                    invocationOperation,
                    context);
            }

            if (elementType is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true })
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            return CreateUnknownExternalCallImpurity(invocationOperation);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultComparisonDispatchPurity(
            ITypeSymbol keyType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (ComparerDispatchHelper.IsBuiltinValueComparerKey(keyType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (DispatchedMemberResolution.TryGetIComparableCompareToImplementation(keyType, out var compareToImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    compareToImplementation,
                    invocationOperation,
                    context);
            }

            if (DispatchedMemberResolution.TryGetIComparableObjectCompareToImplementation(keyType, out var objectCompareToImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    objectCompareToImplementation,
                    invocationOperation,
                    context);
            }

            return CreateUnknownExternalCallImpurity(invocationOperation);
        }
    }
}
