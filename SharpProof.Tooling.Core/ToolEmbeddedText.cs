using System.Reflection;
using System.Text;

namespace SharpProof.Tools.Shared;

public static class ToolEmbeddedText
{
    public static string Load(Assembly assembly, string resourceName)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(resourceName)) throw new ArgumentException(
            "An embedded resource name is required.", nameof(resourceName));

        using var stream = assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd().TrimEnd('\r', '\n');
    }
}
