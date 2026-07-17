using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicQueryWitnessTests
{
    [Test]
    public void QueryScopesAndImplication_ExposeReachabilityWitnessesAndDomains()
    {
        const string source = """
                              public static class WitnessSample
                              {
                                  public static int Read(int value, string text, int[] values, int index)
                                  {
                                      if (value < 2 || value > 9) return -1;
                                      if (text == null || text.Length < 3) return -2;
                                      if (!text.StartsWith("pre") || !text.EndsWith("end")) return -3;
                                      if (index < 0 || index >= values.Length) return -4;
                                      return values[index] + value;
                                  }
                              }
                              """;
        var targetText = "return values[index] + value;";
        var position = source.IndexOf(targetText, StringComparison.Ordinal);
        var line = FindLine(source, targetText);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var options = new SymbolicQueryOptions(
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis);
        var service = new SymbolicQueryService();

        var point = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "WitnessSample.cs"),
            SymbolicQueryTarget.Position(position),
            options));
        var lineResult = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "WitnessSample.cs"),
            SymbolicQueryTarget.Line(line),
            options));
        var span = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "WitnessSample.cs"),
            SymbolicQueryTarget.Span(position, position + targetText.Length),
            options));
        var allLines = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "WitnessSample.cs"),
            SymbolicQueryTarget.AllLines(),
            options));

        var programPoint = point.ProgramPoints.Single();
        var valueDomain = programPoint.InputDomainSummary.Domains.Single(domain => domain.Name == "value");
        var textDomain = programPoint.InputDomainSummary.Domains.Single(domain => domain.Name == "text");
        var indexDomain = programPoint.InputDomainSummary.Domains.Single(domain => domain.Name == "index");
        Assert.Multiple(() =>
        {
            Assert.That(programPoint.ReachabilityWitness.IsAvailable, Is.True);
            Assert.That(programPoint.ReachabilityWitness.Assignments, Has.Some.Property("SourceName").EqualTo("value"));
            Assert.That(valueDomain.Role, Is.EqualTo(SymbolicInputRole.Parameter));
            Assert.That(valueDomain.IntegerRange?.Minimum, Is.EqualTo(2));
            Assert.That(valueDomain.IntegerRange?.Maximum, Is.EqualTo(9));
            Assert.That(textDomain.Nullness, Is.EqualTo(SymbolicNullness.NotNull));
            Assert.That(textDomain.StringLengthRange?.Minimum, Is.EqualTo(3));
            Assert.That(textDomain.RequiredPrefixes, Does.Contain("pre"));
            Assert.That(textDomain.RequiredSuffixes, Does.Contain("end"));
            Assert.That(indexDomain.IsIndex, Is.True);
            Assert.That(indexDomain.RelatedCollection, Is.EqualTo("values"));
        });

        foreach (var result in new[] { point, lineResult, span, allLines })
        {
            Assert.That(result.ReachabilityWitnesses, Is.Not.Empty);
            Assert.That(result.InputDomainSummary, Is.Not.Null);
        }

        var proof = service.Prove(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "WitnessSample.cs"),
            SymbolicQueryTarget.Point(line, FindColumn(source, position)),
            options),
            "value > 5");
        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        Assert.That(proof.CounterexampleWitness.IsAvailable, Is.True);
        Assert.That(
            proof.CounterexampleWitness.Assignments.Single(assignment => assignment.SourceName == "value")
                .IntegerValue,
            Is.LessThanOrEqualTo(5));

        var sourcePath = Path.Combine(Path.GetTempPath(), "SharpProof.ProofQuery." + Guid.NewGuid() + ".cs");
        try
        {
            File.WriteAllText(sourcePath, source);
            var fileProof = service.Prove(new SymbolicQueryContext(
                SymbolicSourceInput.FromFile(sourcePath),
                SymbolicQueryTarget.Point(line, FindColumn(source, position)),
                options), "value > 5");
            Assert.That(fileProof.TruthValue, Is.EqualTo(proof.TruthValue));
            Assert.That(fileProof.CounterexampleWitness.IsAvailable, Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public void RuntimeHazardQuery_ExposesInputsThatSatisfyTheTrigger()
    {
        const string source = """
                              public static class HazardWitness
                              {
                                  public static int Divide(int numerator, int divisor)
                                  {
                                      return numerator / divisor;
                                  }
                              }
                              """;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicQueryService().QueryRuntimeHazards(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "HazardWitness.cs"),
            SymbolicQueryTarget.AllLines(),
            new SymbolicQueryOptions(
                AnalyzerTestHost.GetTrustedPlatformReferences(),
                smtAnalysis)),
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.DivideByZero }));

        var hazard = result.Hazards.Single(hazard => hazard.Kind == SymbolicRuntimeHazardKind.DivideByZero);
        var divisorAssignment = hazard.TriggerWitness.Assignments
            .Single(assignment => assignment.SourceName == "divisor");
        var divisorDomain = hazard.TriggerWitness.DomainSummary.Domains
            .Single(domain => domain.Name == "divisor");
        var serializedHazard = JsonSerializer.SerializeToElement(result, CanonicalJsonOptions)
            .GetProperty("hazards").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == SymbolicRuntimeHazardKind.DivideByZero.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.UnknownReasonInfo.Source, Is.EqualTo(SymbolicUnknownReasonSource.RuntimeHazard));
            Assert.That(hazard.UnknownReasonInfo.Code, Is.EqualTo("runtime_hazard.unknown"));
            Assert.That(serializedHazard.GetProperty("unknownReasonInfo").GetProperty("code").GetString(),
                Is.EqualTo(hazard.UnknownReasonInfo.Code));
            Assert.That(hazard.TriggerWitness.IsAvailable, Is.True);
            Assert.That(divisorAssignment.IntegerValue, Is.EqualTo(0));
            Assert.That(divisorDomain.IntegerRange?.ExactValue, Is.EqualTo(0));
            Assert.That(divisorDomain.Predicates.Count(
                predicate => predicate.Kind == SymbolicDomainPredicateKind.Range), Is.EqualTo(1));
            Assert.That(result.TriggerWitnesses, Does.Contain(hazard.TriggerWitness));
            Assert.That(result.InputDomainSummary.Domains, Has.Some.Property("Name").EqualTo("divisor"));
        });
    }

    private static JsonSerializerOptions CanonicalJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int FindLine(string source, string text)
    {
        var position = source.IndexOf(text, StringComparison.Ordinal);
        return source.Substring(0, position).Count(static character => character == '\n') + 1;
    }

    private static int FindColumn(string source, int position)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));
        return position - lineStart;
    }
}
