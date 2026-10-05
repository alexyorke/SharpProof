namespace SharpProof.Attributes;

internal static class SharpProofAttributeValidation
{
    internal static string RequireReason(string reason, string message)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(message, nameof(reason))
            : reason;
    }
}
