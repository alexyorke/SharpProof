internal static class GeneratedPurityCatalogEntryRelations
{
    public static bool AreEquivalent(
        GeneratedPurityCatalogEntry left,
        GeneratedPurityCatalogEntry right)
    {
        return string.Equals(left.Classification, right.Classification, StringComparison.Ordinal) &&
               string.Equals(left.PrimaryCategory, right.PrimaryCategory, StringComparison.Ordinal) &&
               string.Equals(left.FreshnessClassification, right.FreshnessClassification, StringComparison.Ordinal) &&
               string.Equals(left.EffectVisibilityClassification, right.EffectVisibilityClassification,
                   StringComparison.Ordinal) &&
               left.HasFreshArrayAllocationEvidence == right.HasFreshArrayAllocationEvidence &&
               left.HasFreshObjectAllocationEvidence == right.HasFreshObjectAllocationEvidence &&
               left.HasUnsupportedEffects == right.HasUnsupportedEffects &&
               HaveSameSet(left.Categories, right.Categories) &&
               left.FirstBlockingCallChain.SequenceEqual(right.FirstBlockingCallChain, StringComparer.Ordinal);
    }

    public static bool DoesDominate(
        GeneratedPurityCatalogEntry stronger,
        GeneratedPurityCatalogEntry weaker)
    {
        return string.Equals(stronger.Classification, weaker.Classification, StringComparison.Ordinal) &&
               string.Equals(stronger.PrimaryCategory, weaker.PrimaryCategory, StringComparison.Ordinal) &&
               string.Equals(stronger.FreshnessClassification, weaker.FreshnessClassification,
                   StringComparison.Ordinal) &&
               string.Equals(stronger.EffectVisibilityClassification, weaker.EffectVisibilityClassification,
                   StringComparison.Ordinal) &&
               (!weaker.HasFreshArrayAllocationEvidence || stronger.HasFreshArrayAllocationEvidence) &&
               (!weaker.HasFreshObjectAllocationEvidence || stronger.HasFreshObjectAllocationEvidence) &&
               (!weaker.HasUnsupportedEffects || stronger.HasUnsupportedEffects) &&
               IsSetSuperset(stronger.Categories, weaker.Categories) &&
               IsPrefix(weaker.FirstBlockingCallChain, stronger.FirstBlockingCallChain);
    }

    private static bool HaveSameSet(string[] left, string[] right)
    {
        return left.Length == right.Length &&
               IsSetSuperset(left, right);
    }

    private static bool IsSetSuperset(string[] left, string[] right)
    {
        if (right.Length == 0) return true;

        var set = new HashSet<string>(left, StringComparer.Ordinal);
        return right.All(set.Contains);
    }

    private static bool IsPrefix(string[] prefix, string[] sequence)
    {
        if (prefix.Length > sequence.Length) return false;

        for (var i = 0; i < prefix.Length; i++)
            if (!string.Equals(prefix[i], sequence[i], StringComparison.Ordinal))
                return false;

        return true;
    }
}