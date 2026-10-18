namespace SharpProof.Attributes;

internal static class SharpProofAttributeValidation
{
    internal static string RequireReason(
        string reason,
        /* SPINT001: all values valid - Every message, including null, is accepted by ArgumentException. */ string message)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(message, nameof(reason))
            : reason;
    }
}
