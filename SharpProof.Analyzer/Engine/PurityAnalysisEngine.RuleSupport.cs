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

        internal static bool IsKnownPureBCLMember(ISymbol symbol, Compilation? compilation) =>
            IsTriviallyPureObjectConstructor(symbol) ||
            ImpurityCatalog.IsKnownPureBCLMember(symbol, compilation);

        private static bool IsTriviallyPureObjectConstructor(ISymbol symbol)
        {
            return symbol is IMethodSymbol methodSymbol &&
                methodSymbol.MethodKind == MethodKind.Constructor &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Object;
        }
        internal static bool IsStrictPurityProfile => ImpurityCatalog.IsStrictPurityProfile;
        internal static bool IsKnownImpure(ISymbol symbol) => ImpurityCatalog.IsKnownImpure(symbol);
        internal static bool IsInImpureNamespaceOrType(ISymbol symbol) => ImpurityCatalog.IsInImpureNamespaceOrType(symbol);
        internal static bool IsInConfiguredImpureNamespaceOrType(ISymbol symbol) => ImpurityCatalog.IsInConfiguredImpureNamespaceOrType(symbol);
        internal static bool IsConfiguredKnownPureMember(ISymbol symbol) => ImpurityCatalog.IsConfiguredKnownPureMember(symbol);
    }
}
