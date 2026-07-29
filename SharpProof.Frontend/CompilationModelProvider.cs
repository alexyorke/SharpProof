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
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (tree == null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

#pragma warning disable RS0030 // Audited compiler-host boundary; all consumers route through this method.
        return compilation.GetSemanticModel(tree, ignoreAccessibility: false);
#pragma warning restore RS0030
    }
}
