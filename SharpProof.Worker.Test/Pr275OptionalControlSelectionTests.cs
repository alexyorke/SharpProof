using NUnit.Framework;
using SharpProof.Contracts;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275OptionalControlSelectionTests
{
    [Test]
    public void AnAbsentOptionalAttributeHasNoControlSelection()
    {
        var compilation = TestCompilation.Create("Pr275OptionalControl", "internal sealed class Subject { }");
        var inventory = ContractSelectionInventory.ForCompilation(compilation);
        Assert.That(inventory.Suppress, Is.Not.Null);
        Assert.That(inventory.Trusted, Is.Not.Null);
        Assert.That(ContractSelectionInventory.GetControlSelection(
            null, inventory.Suppress, inventory.Trusted), Is.Null);
        Assert.That(ContractSelectionInventory.GetControlSelection(
            inventory.Suppress, inventory.Suppress, inventory.Trusted), Is.True);
        Assert.That(ContractSelectionInventory.GetControlSelection(
            inventory.Trusted, inventory.Suppress, inventory.Trusted), Is.False);
        Assert.That(ContractSelectionInventory.GetControlSelection(
            compilation.GetTypeByMetadataName("Subject"), inventory.Suppress, inventory.Trusted), Is.Null);
    }
}
