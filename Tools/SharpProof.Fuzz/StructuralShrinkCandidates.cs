using System.Collections.Immutable;

namespace SharpProof.Fuzz;

internal static class StructuralShrinkCandidates
{
    internal static ImmutableArray<T> GetCandidates<T, TIdentity>(
        T value,
        Func<T, ImmutableArray<T>> getChildren,
        Func<T, T, bool> hasCompatibleType,
        Func<T, int> getSize,
        Func<T, TIdentity> getIdentity,
        Func<T, IEnumerable<T>> getDomainCandidates,
        Func<T, int, T, T?> tryReplaceChild,
        IEqualityComparer<TIdentity> identityComparer)
        where T : class
    {
        var candidates = new List<T>();
        var seen = new HashSet<TIdentity>(identityComparer);
        var originalSize = getSize(value);
        var originalIdentity = getIdentity(value);

        void Add(T candidate)
        {
            var identity = getIdentity(candidate);
            if (identityComparer.Equals(identity, originalIdentity))
            {
                return;
            }

            if (getSize(candidate) >= originalSize ||
                !seen.Add(identity))
            {
                return;
            }

            candidates.Add(candidate);
        }

        var children = getChildren(value);
        foreach (var child in children)
        {
            if (hasCompatibleType(value, child))
            {
                Add(child);
            }
        }

        foreach (var domainCandidate in getDomainCandidates(value))
        {
            Add(domainCandidate);
        }

        for (var index = 0; index < children.Length; index++)
        {
            foreach (var childCandidate in GetCandidates(
                         children[index],
                         getChildren,
                         hasCompatibleType,
                         getSize,
                         getIdentity,
                         getDomainCandidates,
                         tryReplaceChild,
                         identityComparer))
            {
                var rebuilt = tryReplaceChild(value, index, childCandidate);
                if (rebuilt != null)
                {
                    Add(rebuilt);
                }
            }
        }

        return [.. candidates];
    }
}
