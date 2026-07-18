using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using static SharpProof.Test.SourceMarker;

namespace SharpProof.Test;

[TestFixture]
public sealed class CompactDomainProjectionTests
{
    [Test]
    public void InvariantProjection_DispatchesEveryQueryScope()
    {
        const string source = "class ScopeFixture { static int Identity(int value) => value; }";
        const string marker = "value;";
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        var input = SymbolicSourceInput.FromText(source, "ScopeFixture.cs");
        var options = new SymbolicQueryOptions(AnalyzerTestHost.GetTrustedPlatformReferences());
        var service = new SymbolicQueryExecutor();
        var targets = new[]
        {
            (Target: SharpProofTarget.AllLines(), Kind: "file"),
            (Target: SharpProofTarget.LineNumber(1), Kind: "line"),
            (Target: SharpProofTarget.Span(position, position + marker.Length), Kind: "span"),
            (Target: SharpProofTarget.AtPosition(position), Kind: "point")
        };

        foreach (var (target, expectedKind) in targets)
        {
            var result = service.Query(new SymbolicQueryContext(input, target, options));
            var filtered = result.Filter(new SymbolicSourceQueryFilter(
                methodNameContains: new[] { "Identity" }));

            Assert.Multiple(() =>
            {
                Assert.That(Serialize(result).GetProperty("scopeKind").GetString(), Is.EqualTo(expectedKind));
                Assert.That(filtered.ScopeKind, Is.EqualTo(expectedKind));
            });
        }
    }

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
        var service = new SymbolicQueryExecutor();

        var invariant = service.Query(new SymbolicQueryContext(
            input,
            SharpProofTarget.AllLines(),
            options));
        var capability = Serialize(service.QueryCapabilities(new SymbolicQueryContext(
                input,
                SharpProofTarget.LineNumber(FindLine(source, "Console.WriteLine")),
                options)));
        var complexity = Serialize(service.QueryComplexity(new SymbolicQueryContext(
                input,
                SharpProofTarget.LineNumber(FindLine(source, "for (var index")),
                options)));
        var runtimeHazards = Serialize(service.QueryRuntimeHazards(new SymbolicQueryContext(
                input,
                SharpProofTarget.LineNumber(FindLine(source, "throw new InvalidOperationException")),
                options)));

        var invariantJson = Serialize(invariant);
        Assert.That(invariantJson.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
        Assert.That(capability.GetProperty("capabilityText").GetString(), Does.Contain("Console"));
        Assert.That(complexity.GetProperty("methodDisplayName").GetString(), Does.Contain("Complexity"));
        Assert.That(runtimeHazards.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
        Assert.That(runtimeHazards.GetProperty("hazards").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public void PublicCompactRuntimeHazardOptions_ValidateBounds()
    {
        Assert.That(
            () => SharpProofTarget.Span(-1, 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => SharpProofTarget.Span(2, 1),
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

    private static JsonElement Serialize(object result)
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

}
