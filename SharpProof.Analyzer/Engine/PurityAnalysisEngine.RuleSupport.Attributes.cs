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
        internal static string? GetKnownImpureMemberSource(ISymbol symbol) => ImpurityCatalog.GetKnownImpureMemberSource(symbol);


        internal static bool HasPureExternalAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            if (HasDirectAttributeNamed(symbol, "PureExternalAttribute", "SharpProof.Attributes.PureExternalAttribute"))
            {
                return true;
            }

            if (HasRecognizedExternalPureAttribute(symbol))
            {
                return true;
            }

            if (HasDirectAttributeNamed(symbol, "ImpureAttribute", "SharpProof.Attributes.ImpureAttribute") ||
                HasAssemblyAttributeNamed(symbol, "ImpureAttribute", "SharpProof.Attributes.ImpureAttribute"))
            {
                return false;
            }

            return HasAssemblyAttributeNamed(symbol, "PureExternalAttribute", "SharpProof.Attributes.PureExternalAttribute");
        }

        internal static bool IsKnownMutableCollectionBoundaryType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType ||
                namedType.IsValueType ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.SpecialType == SpecialType.System_String)
            {
                return false;
            }

            return namedType.OriginalDefinition.ToDisplayString() is
                "System.Collections.Generic.List<T>" or
                "System.Collections.Generic.HashSet<T>" or
                "System.Collections.Generic.Dictionary<TKey, TValue>";
        }


        internal static bool HasImpureAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            if (HasDirectAttributeNamed(symbol, "ImpureAttribute", "SharpProof.Attributes.ImpureAttribute"))
            {
                return true;
            }

            if (HasDirectAttributeNamed(symbol, "PureExternalAttribute", "SharpProof.Attributes.PureExternalAttribute"))
            {
                return false;
            }

            return HasAssemblyAttributeNamed(symbol, "ImpureAttribute", "SharpProof.Attributes.ImpureAttribute");
        }


        internal static PurityAnalysisResult GetCalleePurity(
            IMethodSymbol methodSymbol,
            Rules.PurityAnalysisContext context)
        {
            PurityAnalysisResult result;
            if (context.PurityService != null)
            {
                result = context.PurityService.GetPurity(
                    methodSymbol.OriginalDefinition,
                    context.SemanticModel,
                    context.EnforcePureAttributeSymbol,
                    context.AllowSynchronizationAttributeSymbol,
                    context.CancellationToken);
            }
            else
            {
                result = DeterminePurityRecursiveInternal(
                    methodSymbol.OriginalDefinition,
                    context.SemanticModel,
                    context.EnforcePureAttributeSymbol,
                    context.AllowSynchronizationAttributeSymbol,
                    context.VisitedMethods,
                    context.PurityCache,
                    context.SmtAnalysis,
                    context.CancellationToken,
                    context.PurityService);
            }

            return IsRecursivePlaceholderImpurity(result)
                ? result.WithEvidence(result.Evidence.WithSymbol(context.ContainingMethodSymbol.ToDisplayString(_signatureFormat)))
                : result;
        }



    }
}
