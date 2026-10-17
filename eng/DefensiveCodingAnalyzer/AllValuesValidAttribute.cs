namespace SharpProof.Internal;

// Documents an input whose entire type domain is accepted by the callable.
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class AllValuesValidAttribute : Attribute
{
    public AllValuesValidAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason { get; }
}
