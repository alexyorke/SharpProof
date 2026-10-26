namespace SharpProof.Contracts;

internal sealed partial class ContractBindingResult
{
    public bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ContractBindingResult Success(BoundMethodContracts contracts)
    {
        contracts = RepositoryParameterGuard.NotNull(contracts, nameof(contracts));
        return new(contracts, ContractBindingFailure.None);
    }

    internal static ContractBindingResult Fail(ContractBindingFailure failure)
    {
        return new(null, failure);
    }
}
