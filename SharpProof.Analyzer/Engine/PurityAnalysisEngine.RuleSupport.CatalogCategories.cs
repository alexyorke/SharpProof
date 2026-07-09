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
        internal static string GetKnownImpureCatalogHitCategory(ISymbol symbol, bool includeSynchronizationCategory = false)
        {
            var containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
            var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            if (includeSynchronizationCategory &&
                (containingType == "System.Threading.Interlocked" ||
                 containingType == "System.Threading.Monitor" ||
                 containingType == "System.Threading.Mutex" ||
                 containingType == "System.Threading.Semaphore" ||
                 containingType == "System.Threading.SemaphoreSlim" ||
                 containingType == "System.Collections.Immutable.ImmutableInterlocked"))
            {
                return "synchronization";
            }

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



        internal static bool IsPureEnforced(
            ISymbol symbol,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? pureAttributeSymbol)
        {
            if (symbol == null || enforcePureAttributeSymbol == null)
            {
                return false;
            }

            if (HasPureExternalAttribute(symbol) || HasRecognizedExternalPureAttribute(symbol))
            {
                return true;
            }

            var pureAttributeFullyQualifiedName = "global::SharpProof.Attributes.PureAttribute";
            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, enforcePureAttributeSymbol) ||
                (pureAttributeSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, pureAttributeSymbol)) ||
                string.Equals(
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    pureAttributeFullyQualifiedName,
                    StringComparison.Ordinal)
            );
        }
        private static bool HasDirectAttributeNamed(ISymbol symbol, string fullyQualifiedMetadataName)
        {
            if (symbol == null)
            {
                return false;
            }

            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                    IsAttributeMetadataName(ad, fullyQualifiedMetadataName));
        }

        private static bool HasAssemblyAttributeNamed(ISymbol symbol, string fullyQualifiedMetadataName)
        {
            if (symbol == null)
            {
                return false;
            }

            return symbol.ContainingAssembly?.GetAttributes().Any(ad =>
                IsAttributeMetadataName(ad, fullyQualifiedMetadataName)) == true;
        }

        private static bool HasRecognizedExternalPureAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                IsAttributeMetadataName(ad, "JetBrains.Annotations.PureAttribute") ||
                IsAttributeMetadataName(ad, "System.Diagnostics.Contracts.PureAttribute"));
        }

        private static bool IsAttributeMetadataName(AttributeData attributeData, string fullyQualifiedMetadataName)
        {
            return
                string.Equals(attributeData.AttributeClass?.ToDisplayString(), fullyQualifiedMetadataName, StringComparison.Ordinal) ||
                string.Equals(
                    attributeData.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "global::" + fullyQualifiedMetadataName,
                    StringComparison.Ordinal);
        }

        private static IEnumerable<AttributeData> GetAttributesIncludingAssociatedSymbol(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                yield return attribute;
            }

            if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol })
            {
                foreach (var attribute in associatedSymbol.GetAttributes())
                {
                    yield return attribute;
                }
            }

            if (symbol is IPropertySymbol { GetMethod: { } getMethod } &&
                getMethod.DeclaringSyntaxReferences.Length == 0)
            {
                foreach (var attribute in getMethod.GetAttributes())
                {
                    yield return attribute;
                }
            }
        }
    }
}
