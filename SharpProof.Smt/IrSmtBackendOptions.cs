namespace SharpProof.Smt;

public sealed class IrSmtBackendOptions {
    public const uint DefaultQueryRlimit = 3_000_000;

    public IrSmtBackendOptions(uint queryRlimit = DefaultQueryRlimit) {
        if (queryRlimit == 0) throw new ArgumentOutOfRangeException(nameof(queryRlimit));
        QueryRlimit = queryRlimit;
    }

    public uint QueryRlimit { get; }
}
