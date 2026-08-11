namespace SharpProof.CompilerArtifact;

internal static class CompilerExceptionTypeIdentity
{
    internal static string Encode(INamedTypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));

        if (DocumentationCommentId.CreateReferenceId(type) is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "An exception type does not have a reference documentation ID.");
        }

        return CompilerIdentityBridge.CreateTypeDisplay(type);
    }

    internal static string[] EncodeHierarchy(INamedTypeSymbol? type)
    {
        var identities = new List<string>();
        for (var current = type; current != null; current = current.BaseType)
        {
            identities.Add(Encode(current));
        }

        return [.. identities
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];
    }
}
