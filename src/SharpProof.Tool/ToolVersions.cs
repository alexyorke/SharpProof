using System.Reflection;

namespace SharpProof.Tool;

internal static class ToolVersions
{
    internal static string TestSdk => Read("SharpProof.TestSdk");
    internal static string NUnit => Read("SharpProof.NUnit");
    internal static string NUnitAdapter => Read("SharpProof.NUnitAdapter");

    private static string Read(string key)
    {
        return typeof(ToolVersions).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().Single(attribute =>
        {
            global::SharpProof.Internal.InputValidation.Require(attribute, nameof(attribute));
            return attribute.Key == key;
        }).Value ?? throw new InvalidOperationException("Missing exported dependency version.");
    }
}
