using System.Reflection;

namespace SharpProof.Tool;

// The consumer container scaffold has one source: the standard template's
// compose and output-isolation files, embedded into this tool at build time.
internal static class ContainerScaffold
{
    private const string ImagePlaceholder = "__SHARPPROOF_CONSUMER_IMAGE__";

    internal static string ComposeTemplate { get; } = Read("SharpProof.Tool.compose.yaml.template");

    internal static string OutputProperties { get; } = Read("SharpProof.Tool.Directory.Build.props.template");

    internal static string Compose(string image)
    {
        ArgumentException.ThrowIfNullOrEmpty(image);
        return ComposeTemplate.Replace(ImagePlaceholder, image, StringComparison.Ordinal);
    }

    internal static string ComposeShape(string image)
    {
        ArgumentException.ThrowIfNullOrEmpty(image);
        return ComposeTemplate.Replace(ImagePlaceholder, image, StringComparison.Ordinal).TrimEnd('\n');
    }

    private static string Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using var stream = typeof(ContainerScaffold).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded scaffold resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
