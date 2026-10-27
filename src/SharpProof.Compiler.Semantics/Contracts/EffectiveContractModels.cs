#nullable enable
namespace SharpProof.Contracts;

internal sealed partial class EffectiveContractSourceResolution
{
    internal EffectiveContractSourceResolution(
        IMethodSymbol source,
        ContractClauseInventory directInventory,
        ContractClauseInventory inventory,
        bool usesCompanion,
        ContractBindingFailure failure
    )
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        directInventory = RepositoryParameterGuard.NotNull(directInventory, nameof(directInventory));
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        Source = source;
        DirectInventory = directInventory;
        Inventory = inventory;
        UsesCompanion = usesCompanion;
        Failure = failure;
    }
    internal IMethodSymbol Source { get; }
    internal ContractClauseInventory DirectInventory { get; }
    internal ContractClauseInventory Inventory { get; }
    internal bool UsesCompanion { get; }
    internal ContractBindingFailure Failure { get; }
}
