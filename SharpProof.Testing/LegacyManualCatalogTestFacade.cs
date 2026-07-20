using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine;

// Test-owned absence assertions for the retired handwritten BCL catalogs.
public static class Constants
{
    public static readonly ImmutableHashSet<string> KnownImpureNamespaces =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public static readonly ImmutableHashSet<string> KnownImpureTypeNames = KnownImpureNamespaces;
    public static readonly ImmutableHashSet<string> KnownImpureMethods = KnownImpureNamespaces;
    public static readonly ImmutableHashSet<string> KnownFreshOwnedArrayReturningMembers = KnownImpureNamespaces;
    public static readonly ImmutableHashSet<string> KnownPureBCLMembers = KnownImpureNamespaces;
}
