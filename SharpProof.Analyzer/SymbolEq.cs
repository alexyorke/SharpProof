using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

/// <summary>
/// Centralizes the analyzer's symbol-identity comparisons on
/// <see cref="SymbolEqualityComparer.Default"/> so the idiom is declared in one
/// place. Living in the root <c>SharpProof.Analyzer</c> namespace, it is visible
/// unqualified from every nested analyzer namespace.
/// </summary>
internal static class SymbolEq
{
    /// <summary>The shared default symbol comparer.</summary>
    public static readonly SymbolEqualityComparer Default = SymbolEqualityComparer.Default;

    /// <summary>Compares two symbols for identity using <see cref="Default"/>.</summary>
    public static bool AreEqual(ISymbol? x, ISymbol? y) => Default.Equals(x, y);
}
