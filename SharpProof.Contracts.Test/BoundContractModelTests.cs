using NUnit.Framework;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class BoundContractModelTests
{
    [Test]
    public void GeneratedModelContainsOnlyVocabularyAndStorage()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Contracts",
            "BoundContractModel.generated.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Not.Contain("if ("));
            Assert.That(source, Does.Not.Contain("switch"));
            Assert.That(source, Does.Not.Contain("foreach"));
            Assert.That(
                Enum.GetNames<ContractBindingFailure>(),
                Is.EqualTo([
                    "None",
                    "ContractApiUnavailable",
                    "UnsupportedExpression",
                    "NonBooleanCondition",
                    "ResultOutsideEnsures",
                    "OldOutsideEnsures",
                    "NestedOld",
                    "InvalidIntrinsicSignature",
                    "MissingCompanion",
                    "AmbiguousCompanion",
                    "CompanionSignatureMismatch",
                    "CompanionBodyUnavailable",
                    "InvalidClosedAttribute",
                    "InvalidClausePlacement",
                    "UnsupportedTarget"]));
        }
    }

}
