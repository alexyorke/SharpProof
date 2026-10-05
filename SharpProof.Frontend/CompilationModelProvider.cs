using System.Runtime.CompilerServices;
using SharpProof.Frontend;

namespace SharpProof.Frontend.Host;

/// <summary>
/// The single audited boundary for obtaining Roslyn semantic models.
/// </summary>
public static class CompilationModelProvider
{
    private static readonly ConditionalWeakTable<
        Compilation,
        TreeOwnershipIndex> OwnershipCache = new();

    public static SemanticModel GetSemanticModel(
        Compilation compilation,
        SyntaxTree tree)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));

#pragma warning disable RS0030 // Audited compiler-host boundary; all consumers route through this method.
        var owner = FindOwningCompilation(compilation, tree);
        if (owner == null)
        {
            throw new ArgumentException(
                "SyntaxTree is not part of the compilation or any source " +
                "compilation reference.",
                nameof(tree));
        }

        return owner.GetSemanticModel(tree, ignoreAccessibility: false);
#pragma warning restore RS0030
    }

    private static Compilation? FindOwningCompilation(
        Compilation root,
        SyntaxTree tree)
    {
        var index = OwnershipCache.GetValue(
            root,
            static compilation => TreeOwnershipIndex.Create(compilation));
        if (index.IsAmbiguous(tree))
        {
            throw new ArgumentException(
                "SyntaxTree is part of multiple compilations in the " +
                "source compilation reference closure.",
                nameof(tree));
        }

        return index.TryGetOwner(tree, out var owner)
            ? owner
            : null;
    }

    private sealed class TreeOwnershipIndex
    {
        private readonly Dictionary<SyntaxTree, Compilation?> _owners;

        private TreeOwnershipIndex(
            Dictionary<SyntaxTree, Compilation?> owners)
        {
            _owners = owners;
        }

        internal static TreeOwnershipIndex Create(Compilation root)
        {
            var owners = new Dictionary<SyntaxTree, Compilation?>(
                ReferenceComparer<SyntaxTree>.Instance);
            var pending = new Stack<Compilation>();
            var visited = new HashSet<Compilation>(
                ReferenceComparer<Compilation>.Instance);
            pending.Push(root);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                foreach (var tree in current.SyntaxTrees)
                {
                    if (!owners.TryGetValue(tree, out var owner))
                    {
                        owners.Add(tree, current);
                    }
                    else if (owner != null &&
                             !ReferenceEquals(owner, current))
                    {
                        owners[tree] = null;
                    }
                }

                foreach (var reference in current.References
                             .OfType<CompilationReference>())
                {
                    pending.Push(reference.Compilation);
                }
            }

            return new TreeOwnershipIndex(owners);
        }

        internal bool IsAmbiguous(SyntaxTree tree)
        {
            return _owners.TryGetValue(tree, out var owner) &&
                owner == null;
        }

        internal bool TryGetOwner(
            SyntaxTree tree,
            out Compilation? owner)
        {
            if (!_owners.TryGetValue(tree, out owner) || owner == null)
            {
                owner = null;
                return false;
            }

            return true;
        }
    }

}
