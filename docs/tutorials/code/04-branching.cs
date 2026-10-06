using SharpProof.Attributes;
public static class Decisions
{
    public static bool IsAuthorized(bool administrator, bool owner)
    {
        Contract.Ensures(Contract.Result<bool>() == (administrator || owner));
        if (administrator) return true;
        return owner;
    }
    public static long SelectLimit(bool premium, long standardLimit, long premiumLimit)
    {
        Contract.Ensures(Contract.Result<long>() == (premium ? premiumLimit : standardLimit));
        if (premium) return premiumLimit;
        return standardLimit;
    }
    public static bool Flip(bool enabled)
    {
        Contract.Ensures(Contract.Result<bool>() != Contract.Old(enabled));
        enabled = !enabled;
        return enabled;
    }
}
