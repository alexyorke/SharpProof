namespace SharpProof.Package.Test;

internal static class ProductBuildOutputs
{
    internal static string AttributesAssemblyPath()
    {
        return AssemblyPath(
            "SharpProof.Attributes",
            "netstandard2.0",
            "SharpProof.Attributes.dll");
    }

    internal static string CompilerProbeAssemblyPath()
    {
        return AssemblyPath(
            "SharpProof.CompilerProbe.TestAsset",
            "netstandard2.0",
            "SharpProof.CompilerProbe.TestAsset.dll");
    }

    private static string AssemblyPath(
        string project,
        string targetFramework,
        string assembly)
    {
        var testAssemblyDirectory = new DirectoryInfo(
            Path.GetDirectoryName(
                typeof(ProductBuildOutputs).Assembly.Location)!);
        var configuration = testAssemblyDirectory.Parent?.Name ??
            throw new InvalidOperationException(
                "The test build configuration was not found.");
        var path = Path.Combine(
            PackagedProductFeed.FindRepositoryRoot(),
            project,
            "bin",
            configuration,
            targetFramework,
            assembly);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The pristine product build output was not found.",
                path);
        }

        return path;
    }
}
