namespace SharpProof.Internal;

// Shared by optional developer tooling. Value types have no null state; this
// boundary also works for inferred Roslyn callback parameters without imposing
// restrictions on their otherwise valid numeric or enum domains.
#if SHARPPROOF_EDITOR_INPUTS
internal static class CodeFixInputValidation
#else
internal static class InputValidation
#endif
{
    internal static void Require(object? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("A parameter name is required.", nameof(parameterName));
        }
        if (value == null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }
}
