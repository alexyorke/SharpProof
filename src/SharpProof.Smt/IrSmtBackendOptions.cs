namespace SharpProof.Smt;

internal sealed class IrSmtBackendOptions
{
    public const uint DefaultQueryRlimit = SharpProof.Configuration.SolverDefaults.QueryRlimit;

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
