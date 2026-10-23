namespace SharpProof.Ir;

// A declaration reference, never a producer-supplied numeric verdict.
internal sealed class ResourceMetadataReference
{
    internal string WitnessIdentifier { get; }
    internal string CatalogSha256 { get; }
    internal string AssemblyIdentity { get; }

    internal ResourceMetadataReference(string witnessIdentifier, string catalogSha256, string assemblyIdentity)
    {
        WitnessIdentifier = Checked(witnessIdentifier, nameof(witnessIdentifier));
        CatalogSha256 = Checked(catalogSha256, nameof(catalogSha256));
        AssemblyIdentity = Checked(assemblyIdentity, nameof(assemblyIdentity));
    }

    private static string Checked(string value, string parameter)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        parameter = ArgumentNullGuard.NotNull(parameter, nameof(parameter));
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(static character => char.IsControl(character)))
        { throw new ArgumentException("Resource metadata identity is malformed.", parameter); }
        return value;
    }
}
