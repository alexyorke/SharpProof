using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class CompactDomainProjectionTests
{
    [Test]
    public void PublicCompactDomainDtos_ShareSchemaAndPreserveTotals()
    {
        const string source = """
                              using System;

                              public static class CompactFixture
                              {
                                  public static int Identity(int value) => value;

                                  public static void Capability()
                                  {
                                      Console.WriteLine("compact");
                                  }

                                  public static int Complexity(int count)
                                  {
                                      var total = 0;
                                      for (var index = 0; index < count; index++) total += index;
                                      return total;
                                  }

                                  public static void Hazard()
                                  {
                                      throw new InvalidOperationException();
                                  }
                              }
                              """;
        var input = SymbolicSourceInput.FromText(source, "CompactFixture.cs");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var options = new SymbolicQueryOptions(
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis);
        var service = new SymbolicQueryService();

        var invariant = service.Query(new SymbolicQueryRequest(
            input,
            SymbolicQueryTarget.AllLines(),
            options));
        var capability = service.QueryCapabilities(new SymbolicCapabilityRequest(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "Console.WriteLine")),
                options))
            .ToCompactResult();
        var complexity = service.QueryComplexity(new SymbolicComplexityRequest(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "for (var index")),
                options))
            .ToCompactResult();
        var runtimeHazards = service.QueryRuntimeHazards(new SymbolicRuntimeHazardRequest(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "throw new InvalidOperationException")),
                options))
            .ToCompactResult(new SymbolicCompactRuntimeHazardQueryOptions(maxHazards: 0, maxConditions: 0));

        var compactResults = new ISymbolicCompactResult[]
        {
            invariant.ToCompactResult(),
            invariant.ToInvariantQueryResult(),
            capability,
            complexity,
            runtimeHazards
        };
        foreach (var compactResult in compactResults)
        {
            Assert.That(compactResult.SchemaVersion, Is.EqualTo(1), compactResult.Kind);
            Assert.That(compactResult.EvidenceSchemaVersion,
                Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion), compactResult.Kind);
            Assert.That(compactResult.EvidenceSchemaCompatibility,
                Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy), compactResult.Kind);

            var root = Serialize(compactResult);
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo(compactResult.Kind));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("evidenceSchemaVersion").GetInt32(),
                Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
            Assert.That(root.GetProperty("evidenceSchemaCompatibility").GetString(),
                Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        }

        Assert.That(capability.Kind, Is.EqualTo("capabilities"));
        Assert.That(capability.CapabilityText, Does.Contain("Console"));
        Assert.That(complexity.Kind, Is.EqualTo("complexity"));
        Assert.That(complexity.MethodDisplayName, Does.Contain("Complexity"));
        Assert.That(runtimeHazards.Kind, Is.EqualTo("runtimeHazards"));
        Assert.That(runtimeHazards.HazardCount, Is.EqualTo(1));
        Assert.That(runtimeHazards.Hazards, Is.Empty);
        Assert.That(runtimeHazards.Truncation.Hazards, Is.True);
        Assert.That(runtimeHazards.StatusCounts[SymbolicRuntimeHazardStatus.Proven.ToString()], Is.EqualTo(1));
    }

    [Test]
    public void PublicCompactRuntimeHazardOptions_ValidateBounds()
    {
        Assert.That(SymbolicCompactRuntimeHazardQueryOptions.Default.MaxHazards,
            Is.EqualTo(SymbolicCompactRuntimeHazardQueryOptions.DefaultMaxHazards));
        Assert.That(
            () => new SymbolicCompactRuntimeHazardQueryOptions(maxHazards: -1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new SymbolicCompactRuntimeHazardQueryOptions(maxConditions: -1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void BoundedProjection_ReportsItemsAndOmittedCountTogether()
    {
        var projection = SymbolicCompactProjection.Project(new[] { "a", "b", "c" }, 2);

        Assert.That(projection.Items, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(projection.TotalCount, Is.EqualTo(3));
        Assert.That(projection.OmittedCount, Is.EqualTo(1));
        Assert.That(projection.IsTruncated, Is.True);

        var complete = SymbolicCompactProjection.Project(new[] { "a" }, 2);
        Assert.That(complete.Items, Is.EqualTo(new[] { "a" }));
        Assert.That(complete.OmittedCount, Is.Zero);
        Assert.That(complete.IsTruncated, Is.False);
    }

    [Test]
    public void FormulaKind_IsSharedByInvariantConditionsAndProofResults()
    {
        var formula = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtBooleanConstant(true));

        var condition = SymbolicInvariantCondition.FromFormula(0, formula);
        var proof = new SymbolicConditionProofResult(
            condition.Text,
            SymbolicTruthValue.Unknown,
            "test",
            formula);

        Assert.That(condition.DisplayKind, Is.EqualTo("SmtUnary"));
        Assert.That(proof.DisplayKind, Is.EqualTo(condition.DisplayKind));
    }

    private static JsonElement Serialize(ISymbolicCompactResult result)
    {
        var json = JsonSerializer.Serialize(
            result,
            result.GetType(),
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static int FindLine(string source, string marker)
    {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");

        var line = 1;
        for (var index = 0; index < position; index++)
            if (source[index] == '\n')
                line++;

        return line;
    }
}
