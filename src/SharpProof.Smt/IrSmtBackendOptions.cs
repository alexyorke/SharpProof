namespace SharpProof.Smt;

public sealed class IrSmtBackendOptions
{
    public const uint DefaultQueryRlimit = 3_000_000;

    public IrSmtBackendOptions(uint queryRlimit = DefaultQueryRlimit)
    {
        QueryRlimit = ArgumentNullGuard.RequirePositive(
            queryRlimit, nameof(queryRlimit));
    }

    public uint QueryRlimit
    {
        get;
    }
}
