using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Gates.Performance;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class AcceptancePerformanceContractTests
{
    [TestCase("performance", "smokeMaximumRatio", "1e400")]
    [TestCase("performance", "maximumMedianRatio", "1e400")]
    [TestCase("performance", "maximumP95Ratio", "1e400")]
    [TestCase("performance", "maximumRetainedMemoryRatio", "1e400")]
    [TestCase("performance", "ideEditP95Milliseconds", "1e400")]
    [TestCase("performance", "ideEditMaximumMilliseconds", "1e400")]
    [TestCase("worker", "cancellationP95Milliseconds", "1e400")]
    [TestCase("worker", "forcedTerminationMilliseconds", "1e400")]
    [TestCase("performance", "maximumMedianRatio", "0")]
    [TestCase("performance", "maximumMedianRatio", "-1")]
    public void LoadRejectsNonfiniteOrNonpositiveDoubleLimits(
        string sectionName,
        string propertyName,
        string jsonValue)
    {
        var sourceRoot = RepositoryLayout.FindRoot();
        var sourcePath = Path.Combine(
            sourceRoot,
            "eng",
            "acceptance",
            "contract.json");
        var contract = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        contract[sectionName]!.AsObject()[propertyName] =
            JsonNode.Parse(jsonValue);

        using var probeRoot = new TempDirectory("SharpProof.Gates.Test-");
        var probeDirectory = Path.Combine(
            probeRoot.FullName,
            "eng",
            "acceptance");
        Directory.CreateDirectory(probeDirectory);
        File.WriteAllText(
            Path.Combine(probeDirectory, "contract.json"),
            contract.ToJsonString());
        Assert.That(
            (Action)(() =>
                _ = AcceptancePerformanceContract.Load(probeRoot.FullName)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains(propertyName));
    }
}
