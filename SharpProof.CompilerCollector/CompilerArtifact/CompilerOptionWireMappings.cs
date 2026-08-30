// Internal compiler-option reflection remains confined to the build-time collector.
namespace SharpProof.CompilerArtifact;

internal static partial class CompilerOptionWireMappings
{
    internal static bool ReadInternalBoolean(
        CSharpCompilationOptions options,
        string name)
    {
        return ReadInternalBoolean(
            options,
            typeof(CompilationOptions),
            name);
    }

    internal static bool ReadInternalBoolean(
        MetadataReferenceProperties properties,
        string name)
    {
        return ReadInternalBoolean(
            properties,
            typeof(MetadataReferenceProperties),
            name);
    }

    private static bool ReadInternalBoolean(
        object value,
        Type declaringType,
        string name)
    {
        var property = declaringType.GetProperty(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (property?.PropertyType != typeof(bool) ||
            property.GetIndexParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"The compiler option '{name}' is unavailable or has an unexpected shape.");
        }

        return (bool)(property.GetValue(value) ??
            throw new InvalidOperationException(
                $"The compiler option '{name}' returned no value."));
    }
}
