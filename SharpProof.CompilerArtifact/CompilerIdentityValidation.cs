namespace SharpProof.CompilerArtifact;

internal static class CompilerIdentityValidation
{
    internal const int MaximumCallIdentityLength = 512;

    internal static bool IsValidCallIdentity(string? value)
    {
        return value is { Length: > 0 and <= MaximumCallIdentityLength } &&
            value.All(static character => !char.IsControl(character));
    }
}
