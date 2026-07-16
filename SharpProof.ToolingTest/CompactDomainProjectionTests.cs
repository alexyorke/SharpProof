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
        var service = new SymbolicQueryService();
        var targets = new[]
        {
            (Target: SymbolicQueryTarget.AllLines(), Kind: "file"),
            (Target: SymbolicQueryTarget.Line(1), Kind: "line"),
            (Target: SymbolicQueryTarget.Span(position, position + marker.Length), Kind: "span"),
            (Target: SymbolicQueryTarget.Position(position), Kind: "point")
        };

        foreach (var (target, expectedKind) in targets)
        {
            var result = service.Query(new SymbolicQueryContext(input, target, options));
            var filtered = result.Filter(new SymbolicSourceQueryFilter(
                methodNameContains: new[] { "Identity" }));

            Assert.Multiple(() =>
            {
                Assert.That(result.ToCompactResult().GetProperty("kind").GetString(), Is.EqualTo(expectedKind));
                Assert.That(result.ToInvariantQueryResult().GetProperty("scopeKind").GetString(), Is.EqualTo(expectedKind));
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
        var service = new SymbolicQueryService();

        var invariant = service.Query(new SymbolicQueryContext(
            input,
            SymbolicQueryTarget.AllLines(),
            options));
        var capability = service.QueryCapabilities(new SymbolicQueryContext(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "Console.WriteLine")),
                options))
            .ToCompactResult();
        var complexity = service.QueryComplexity(new SymbolicQueryContext(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "for (var index")),
                options))
            .ToCompactResult();
        var runtimeHazards = service.QueryRuntimeHazards(new SymbolicQueryContext(
                input,
                SymbolicQueryTarget.Line(FindLine(source, "throw new InvalidOperationException")),
                options))
            .ToCompactResult(new SymbolicCompactRuntimeHazardQueryOptions(maxHazards: 0, maxConditions: 0));

        var compactInvariant = invariant.ToCompactResult();
        var invariantQuery = invariant.ToInvariantQueryResult();
        var compactResults = new[]
        {
            (Kind: compactInvariant.GetProperty("kind").GetString()!, Root: compactInvariant),
            (Kind: invariantQuery.GetProperty("kind").GetString()!, Root: invariantQuery),
            (Kind: "capabilities", Root: capability),
            (Kind: "complexity", Root: complexity),
            (Kind: "runtimeHazards", Root: runtimeHazards)
        };
        foreach (var (kind, root) in compactResults)
        {
            SymbolicCliTestAssertions.AssertCompactEnvelope(root, kind);
            var expectedPropertyPrefix = kind is "capabilities" or "complexity"
                ? new[] { "schemaVersion", "evidenceSchemaVersion", "evidenceSchemaCompatibility", "kind" }
                : new[] { "kind", "schemaVersion", "evidenceSchemaVersion", "evidenceSchemaCompatibility" };
            Assert.That(
                root.EnumerateObject().Take(expectedPropertyPrefix.Length).Select(static property => property.Name),
                Is.EqualTo(expectedPropertyPrefix),
                kind);
            if (kind is "capabilities" or "complexity")
                Assert.That(
                    root.EnumerateObject().Skip(4).Take(9).Select(static property => property.Name),
                    Is.EqualTo(new[] { "filePath", "methodDisplayName", "declarationKind", "spanStart", "spanEnd",
                        "startLine", "startColumn", "endLine", "endColumn" }),
                    kind);
        }

        Assert.That(capability.GetProperty("kind").GetString(), Is.EqualTo("capabilities"));
        Assert.That(capability.GetProperty("capabilityText").GetString(), Does.Contain("Console"));
        Assert.That(complexity.GetProperty("kind").GetString(), Is.EqualTo("complexity"));
        Assert.That(complexity.GetProperty("methodDisplayName").GetString(), Does.Contain("Complexity"));
        Assert.That(runtimeHazards.GetProperty("kind").GetString(), Is.EqualTo("runtimeHazards"));
        Assert.That(runtimeHazards.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
        Assert.That(runtimeHazards.GetProperty("hazards").GetArrayLength(), Is.Zero);
        Assert.That(runtimeHazards.GetProperty("truncation").GetProperty("hazards").GetBoolean(), Is.True);
        Assert.That(runtimeHazards.GetProperty("statusCounts")
            .GetProperty(SymbolicRuntimeHazardStatus.Proven.ToString()).GetInt32(), Is.EqualTo(1));
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

    private static JsonElement Serialize(SymbolicSchemaResultBase result)
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
