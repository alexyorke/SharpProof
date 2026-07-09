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
        private static readonly SymbolDisplayFormat ExceptionTypeDisplayFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static MethodExceptionQueryResult AnalyzeMethod(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            SmtAnalysisService smtAnalysis)
        {
            var visitedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
            {
                methodSymbol.OriginalDefinition
            };

            return AnalyzeMethod(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis);
        }

        private static MethodExceptionQueryResult AnalyzeMethod(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods,
            SmtAnalysisService smtAnalysis)
        {
            var siteEntries = CollectUncaughtExceptionSiteEntries(
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    exceptionSummaryCatalog,
                    visitedMethods,
                    smtAnalysis)
                .ToImmutableArray();

            var exceptionEvidence = new ExceptionEvidenceSet();
            foreach (var siteEntry in siteEntries)
            {
                exceptionEvidence.Add(siteEntry.Exception);
            }

            return new MethodExceptionQueryResult(exceptionEvidence, siteEntries);
        }

    }
}
