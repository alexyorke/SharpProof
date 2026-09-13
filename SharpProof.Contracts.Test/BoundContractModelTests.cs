using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class BoundContractModelTests
{
    [Test]
    public void GeneratedModelContainsOnlyVocabularyAndStorage()
    {
        var repository = TestRepository.FindRoot();
        var source = File.ReadAllText(Path.Combine(
            repository,
            "SharpProof.Contracts",
            "BoundContractModel.generated.cs"));
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "SharpProof.Contracts",
            "BoundContractModel.schema.json")));
        var clause = schema.RootElement
            .GetProperty("classes")
            .EnumerateArray()
            .Single(value => value.GetProperty("name").GetString() ==
                "BoundContractClause");
        var generatedProperties = typeof(BoundContractClause)
            .GetProperties()
            .Select(static property => property.Name);

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
            Assert.That(source, Does.Not.Contain("IsAssumptionEvidence"));
            Assert.That(
                clause.TryGetProperty("projections", out _),
                Is.False);
            Assert.That(
                generatedProperties,
                Does.Not.Contain("IsAssumptionEvidence"));
        }
    }

}
