using Ardalis.GuardClauses;
using SharpProof.Attributes;

namespace SharpProof.Pilots.ArdalisContracts;

public static class GuardAdapter
{
    public static string Required(string value)
    {
        Contract.Requires(value.Length > 0);
        return Guard.Against.NullOrWhiteSpace(value);
    }

    public static string KnownGood() => Required("pilot");

#if SHARPPROOF_NEGATIVE_PROBE
    public static string RejectedCallProbe() => Required("");
#endif
}
