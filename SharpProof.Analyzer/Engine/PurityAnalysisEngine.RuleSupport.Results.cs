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
        private static PurityEvidence CreateUnsupportedOperationEvidence(IOperation operation)
        {
            return IsUnsafePointerOperation(operation)
                ? PurityEvidence.Create("unsafe_pointer", ruleName: "UnsupportedOperation", operation: operation)
                : PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", operation: operation);
        }

        private static bool IsUnsafePointerOperation(IOperation operation)
        {
            var operationKind = operation.Kind.ToString();
            var typeKind = operation.Type?.TypeKind.ToString() ?? string.Empty;

            return operationKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   operationKind.Equals("AddressOf", StringComparison.Ordinal) ||
                   operationKind.Equals("Fixed", StringComparison.Ordinal) ||
                   operationKind.Equals("SizeOf", StringComparison.Ordinal) ||
                   operationKind.Equals("StackAlloc", StringComparison.Ordinal) ||
                   typeKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static PurityAnalysisResult ImpureResult(SyntaxNode? syntaxNode, PurityEvidence evidence = default)
        {
            if (syntaxNode != null)
            {
                return evidence.IsEmpty
                    ? PurityAnalysisResult.Impure(syntaxNode)
                    : PurityAnalysisResult.Impure(syntaxNode, evidence);
            }

            return evidence.IsEmpty
                ? PurityAnalysisResult.ImpureUnknownLocation
                : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(evidence);
        }
        internal static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
        {
            if (attributeSymbol == null) return false;
            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, attributeSymbol.OriginalDefinition));
        }



        internal static PurityAnalysisResult CheckStaticConstructorPurity(ITypeSymbol? typeSymbol, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            if (typeSymbol == null)
            {
                return PurityAnalysisResult.Pure;
            }


            IMethodSymbol? staticConstructor = typeSymbol.GetMembers(".cctor").OfType<IMethodSymbol>().FirstOrDefault();

            if (staticConstructor == null)
            {
                return PurityAnalysisResult.Pure;
            }





            var cctorResult = GetCalleePurity(staticConstructor, context);





            return cctorResult.IsPure
                ? PurityAnalysisResult.Pure
                : PurityAnalysisResult.Impure(
                    cctorResult.ImpureSyntaxNode ??
                        typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) ??
                        context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) ??
                        throw new InvalidOperationException("Cannot find syntax node for static constructor impurity"),
                    cctorResult.Evidence);
        }


    }
}
