using SharpProof.Attributes;
public interface IAmount
{
    int Normalize(int value);
}
[ContractFor(typeof(IAmount))]
public static class AmountContracts
{
    public static int Normalize(IAmount receiver, int value)
    {
        Contract.Requires(receiver != null);
        Contract.Requires(value >= 0);
        Contract.Ensures(Contract.Result<int>() == value);
        return value;
    }
}
public static class AmountConsumer
{
    public static int Use(IAmount amount)
    {
        Contract.Requires(amount != null);
        return amount.Normalize(1);
    }
}
