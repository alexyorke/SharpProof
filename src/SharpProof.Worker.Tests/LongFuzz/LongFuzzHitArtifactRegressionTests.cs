using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
[NonParallelizable]
public sealed class LongFuzzHitArtifactRegressionTests
{
    [Test]
    public void DistinctFindingsWithTheSameSeedAndKindKeepTheirOwnSources()
    {
        using var output = new TempDirectory("longfuzz-hit-artifacts-");
        var previousOutput = Environment.GetEnvironmentVariable(LongFuzzSession.OutputVariable);
        try
        {
            Environment.SetEnvironmentVariable(LongFuzzSession.OutputVariable, output.FullName);
            var session = new LongFuzzSession("contract-matrix", 0, 1);
            const string firstSource = "public static class First { public static int Target() => 1; }";
            const string secondSource = "public static class Second { public static int Target() => 2; }";
            session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, 56, "spec-max",
                "", "", "", "first failure", firstSource));
            session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, 56, "spec-abs",
                "", "", "", "second failure", secondSource));

            var records = File.ReadAllLines(Path.Combine(output.FullName, "hits.jsonl"))
                .Select(line => line is null ? throw new System.ArgumentNullException(nameof(line)) : (JsonSerializer.Deserialize<LongFuzzHitRecord>(line)!)).ToArray();
            Assert.That(records, Has.Length.EqualTo(2));
            Assert.That(records[0].SourceFile, Is.Not.EqualTo(records[1].SourceFile));
            Assert.That(File.ReadAllText(Path.Combine(output.FullName, records[0].SourceFile)),
                Does.Contain(firstSource));
            Assert.That(File.ReadAllText(Path.Combine(output.FullName, records[1].SourceFile)),
                Does.Contain(secondSource));
        }
        finally
        {
            Environment.SetEnvironmentVariable(LongFuzzSession.OutputVariable, previousOutput);
        }
    }

    [Test]
    public void ReusedOutputPreservesPreviousHitAndDistinctKindsKeepTheirLegacyNames()
    {
        using var output = new TempDirectory("longfuzz-hit-artifacts-");
        var previousOutput = Environment.GetEnvironmentVariable(LongFuzzSession.OutputVariable);
        try
        {
            Environment.SetEnvironmentVariable(LongFuzzSession.OutputVariable, output.FullName);
            var firstRun = new LongFuzzSession("contract-matrix", 0, 1);
            firstRun.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, 56, "first",
                "", "", "", "", "public class FirstRun {}"));
            var secondRun = new LongFuzzSession("contract-matrix", 0, 1);
            secondRun.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, 56, "second",
                "", "", "", "", "public class SecondRun {}"));
            secondRun.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, 56, "third",
                "", "", "", "", "public class DifferentKind {}"));

            var records = File.ReadAllLines(Path.Combine(output.FullName, "hits.jsonl"))
                .Select(line => line is null ? throw new System.ArgumentNullException(nameof(line)) : (JsonSerializer.Deserialize<LongFuzzHitRecord>(line)!)).ToArray();
            Assert.That(records, Has.Length.EqualTo(3));
            Assert.That(records[0].SourceFile, Is.EqualTo("hits/contract-matrix-seed56-HarnessException.cs"));
            Assert.That(records[1].SourceFile, Is.EqualTo("hits/contract-matrix-seed56-HarnessException-1.cs"));
            Assert.That(records[2].SourceFile, Is.EqualTo("hits/contract-matrix-seed56-FalseProven.cs"));
            Assert.That(File.ReadAllText(Path.Combine(output.FullName, records[0].SourceFile)),
                Does.Contain("public class FirstRun {}"));
            Assert.That(File.ReadAllText(Path.Combine(output.FullName, records[1].SourceFile)),
                Does.Contain("public class SecondRun {}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(LongFuzzSession.OutputVariable, previousOutput);
        }
    }
}
