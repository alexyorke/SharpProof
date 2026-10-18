namespace SharpProof.CompilerArtifact;

// Encodes a declaration's structural role within its compiler-owned syntax.
internal static class CompilerOwnedSyntaxPath
{
    internal static void Write(CanonicalHashWriter writer, SyntaxNode? declaration, SyntaxNode? owner)
    {
        writer = RepositoryParameterGuard.NotNull(writer, nameof(writer));
        if (declaration == null || owner == null)
        {
            writer.Add(-1);
            return;
        }
        var path = new Stack<int>();
        for (var current = declaration; !SyntaxSite.IsSame(current, owner); current = current.Parent!)
        {
            if (current.Parent == null)
            {
                writer.Add(-1);
                return;
            }
            path.Push(current.Parent.ChildNodes().TakeWhile(
                child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (!SyntaxSite.IsSame(child, current))).Count());
        }
        writer.Add(path.Count);
        foreach (var ordinal in path)
        {
            writer.Add(ordinal);
        }
    }
}
