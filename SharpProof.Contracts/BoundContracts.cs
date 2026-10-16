namespace SharpProof.Contracts;

public sealed partial class ContractBindingResult
{
    public bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ContractBindingResult Success(BoundMethodContracts contracts)
    {
        if (contracts is null)
        { throw new System.ArgumentNullException(nameof(contracts)); }
        return new(contracts, ContractBindingFailure.None);
    }

    internal static ContractBindingResult Fail(ContractBindingFailure failure)
    {
        return new(null, failure);
    }
}
