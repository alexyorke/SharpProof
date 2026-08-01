namespace SharpProof.Frontend.Host;

/// <summary>
/// The single audited boundary for obtaining Roslyn semantic models.
/// </summary>
public static class CompilationModelProvider
{
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
        var pending = new Stack<Compilation>();
        var visited = new List<Compilation>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (visited.Any(candidate =>
                    ReferenceEquals(candidate, current)))
            {
                continue;
            }

            visited.Add(current);
            if (current.SyntaxTrees.Any(candidate =>
                    ReferenceEquals(candidate, tree)))
            {
                return current;
            }

            foreach (var reference in current.References
                         .OfType<CompilationReference>())
            {
                pending.Push(reference.Compilation);
            }
        }

        return null;
    }
}
