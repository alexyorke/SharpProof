using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class RuntimePremiseIdentityProtocolCoverageTests
{
    [TestCase(WorkerResourceTaskRuntimeQuantity.Unspecified)]
    [TestCase((WorkerResourceTaskRuntimeQuantity)(-1))]
    [TestCase((WorkerResourceTaskRuntimeQuantity)999)]
    public void UnknownQuantitiesCannotAcquireAUnitOrScope(WorkerResourceTaskRuntimeQuantity quantity)
    {
        var unit = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            ResourceTaskRuntimePremiseIdentity.Unit(quantity)));
        var scope = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            ResourceTaskRuntimePremiseIdentity.Scope(quantity)));
        Assert.That(unit!.ParamName, Is.EqualTo("quantity"));
        Assert.That(scope!.ParamName, Is.EqualTo("quantity"));
    }

    [TestCase(0)]
    [TestCase(10)]
    [TestCase(12)]
    public void IncompleteOrExtraInventoriesCannotPublishAnyIdentity(int count)
    {
        var valid = ValidDeclarations();
        ImmutableArray<WorkerResourceTaskRuntimeDeclaration> quantities = count == 12
            ? valid.Quantities.Add(valid.Quantities[0]) : [.. valid.Quantities.Take(count)];
        AssertCannotPublish(valid with { Quantities = quantities }, "TaskRuntimePremisesIncomplete");
    }

    [Test]
    public void CompleteInventoriesStillRejectInvalidQuantityMetadata()
    {
        var valid = ValidDeclarations();
        var first = valid.Quantities[0];
        ImmutableArray<ImmutableArray<WorkerResourceTaskRuntimeDeclaration>> invalidInventories =
        [
            valid.Quantities.SetItem(1, first),
            valid.Quantities.SetItem(0, valid.Quantities[1]).SetItem(1, first),
            valid.Quantities.SetItem(0, first with { UpperBound = -1 }),
            valid.Quantities.SetItem(0, null!),
            valid.Quantities.SetItem(0, first with { Evidence = " " }),
            valid.Quantities.SetItem(0, first with { Evidence = "reviewed\nelsewhere" })
        ];
        foreach (var quantities in invalidInventories)
        {
            AssertCannotPublish(valid with { Quantities = quantities }, "TaskRuntimePremisesInvalid");
        }
    }

    [Test]
    public void InvalidDeploymentMetadataCannotPublishAnyIdentity()
    {
        var valid = ValidDeclarations();
        ImmutableArray<WorkerResourceTaskRuntimeDeclarations> invalidDeclarations =
        [
            valid with { RootCallableId = " " },
            valid with { RootCallableId = "M:Subject.Target\nM:Other.Target" },
            valid with { Profile = WorkerResourceTaskDeploymentProfile.Unspecified },
            valid with { DeploymentManifestSha256 = "not-a-manifest-digest" },
            valid with { Evidence = " " }
        ];
        foreach (var declarations in invalidDeclarations)
        {
            AssertCannotPublish(declarations, "TaskRuntimePremisesInvalid");
        }
    }

    [Test]
    public void QuantityIdentityRequiresAnOwnedValueAndBindsItsEvidenceAndBound()
    {
        var valid = ValidDeclarations();
        var owned = valid.Quantities[0];
        var identity = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(valid, owned);
        Assert.That(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(valid, owned with { }), Is.EqualTo(identity));
        ImmutableArray<WorkerResourceTaskRuntimeDeclaration> foreignDeclarations =
        [owned with { UpperBound = owned.UpperBound + 1 }, owned with { Evidence = "different evidence" }];
        foreach (var foreign in foreignDeclarations)
        {
            var error = Assert.Throws<ArgumentException>((Action)(() =>
                ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(valid, foreign)));
            Assert.That(error!.ParamName, Is.EqualTo("declaration"));
        }

        var changed = valid with { Quantities = valid.Quantities.SetItem(0, foreignDeclarations[0]) };
        Assert.That(ResourceTaskRuntimePremiseIdentity.Validate(changed), Is.Null);
        Assert.That(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(changed, changed.Quantities[0]), Is.Not.EqualTo(identity));
        Assert.That(ResourceTaskRuntimePremiseIdentity.DeclarationSha256(changed),
            Is.Not.EqualTo(ResourceTaskRuntimePremiseIdentity.DeclarationSha256(valid)));
        Assert.That(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(changed),
            Is.EqualTo(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(valid)));
        Assert.That(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(changed, changed.Quantities[1]),
            Is.EqualTo(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(valid, valid.Quantities[1])));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t ")]
    [TestCase("reviewed\nelsewhere")]
    [TestCase("reviewed\0elsewhere")]
    public void AbsentOrControlBearingIdentityTextIsRejected(string? value)
    {
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsEvidence(value), Is.False);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsRootCallableId(value), Is.False);
    }

    [Test]
    public void IdentityTextAcceptsTheMaximumLengthAndRejectsTheNextCharacter()
    {
        var evidence = new string('e', ResourceTaskRuntimePremiseIdentity.MaximumEvidenceLength);
        var root = new string('r', ResourceTaskRuntimePremiseIdentity.MaximumRootCallableIdLength);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsEvidence(evidence), Is.True);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsEvidence(evidence + "e"), Is.False);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsRootCallableId(root), Is.True);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsRootCallableId(root + "r"), Is.False);
        var declarations = ValidDeclarations() with { RootCallableId = root, Evidence = evidence };
        Assert.That(ResourceTaskRuntimePremiseIdentity.Validate(declarations), Is.Null);
        Assert.That(ResourceTaskRuntimePremiseIdentity.DeclarationSha256(declarations), Does.Match("^[0-9a-fA-F]{64}$"));
    }

    private static WorkerResourceTaskRuntimeDeclarations ValidDeclarations()
    {
        ImmutableArray<WorkerResourceTaskRuntimeDeclaration> quantities =
            [.. Enumerable.Range(1, 11).Select(static value => new WorkerResourceTaskRuntimeDeclaration(
                (WorkerResourceTaskRuntimeQuantity)value, value, "reviewed quantity evidence"))];
        var declarations = new WorkerResourceTaskRuntimeDeclarations("M:Subject.Target",
            WorkerResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed,
            new string('a', 64), "reviewed deployment evidence", quantities);
        Assert.That(ResourceTaskRuntimePremiseIdentity.Validate(declarations), Is.Null);
        return declarations;
    }

    private static void AssertCannotPublish(WorkerResourceTaskRuntimeDeclarations declarations, string reason)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(ResourceTaskRuntimePremiseIdentity.Validate(declarations), Is.EqualTo(reason));
        var full = Assert.Throws<InvalidDataException>((Action)(() =>
            ResourceTaskRuntimePremiseIdentity.DeclarationSha256(declarations)));
        var deployment = Assert.Throws<InvalidDataException>((Action)(() =>
            ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations)));
        var quantity = Assert.Throws<InvalidDataException>((Action)(() =>
            ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, ValidDeclarations().Quantities[0])));
        Assert.That(full!.Message, Is.EqualTo(reason));
        Assert.That(deployment!.Message, Is.EqualTo(reason));
        Assert.That(quantity!.Message, Is.EqualTo(reason));
    }
}
