namespace SharpProof.Gates;

internal static class RepositoryLayout
{
    public static string FindRoot(string? start = null)
    {
        return RepositoryRoot.Find(
            start,
            "SharpProof.slnx",
            "eng/acceptance/contract.json") ??
            throw new InvalidOperationException(
                "Could not locate the SharpProof repository root.");
    }
}
