namespace SharpProof.Compiler.Semantics;

// The one rule for matching a referenced assembly to an approved spec identity:
// the simple name and the public key token, never the version.
internal static class ApiSpecAssemblyMatching
{
    internal static bool Matches(ApiSpecAssemblyIdentity approved, AssemblyIdentity identity)
    {
        approved = ArgumentNullGuard.NotNull(approved, nameof(approved));
        identity = ArgumentNullGuard.NotNull(identity, nameof(identity));
        var token = identity.PublicKeyToken.IsDefaultOrEmpty ? string.Empty : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) &&
            string.Equals(approved.PublicKeyToken, token, StringComparison.OrdinalIgnoreCase);
    }
}
