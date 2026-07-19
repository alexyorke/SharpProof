using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Tools.Baseline;

namespace SharpProof.ToolingTest;

[TestFixture]
public sealed class BaselineWorkflowTests
{
    [Test]
    public void NormalizePath_CollapsesRedundantSegments()
    {
        Assert.That(
            SharpProofBaseline.NormalizePath("src//nested/./generated/../File.cs"),
            Is.EqualTo("src/nested/File.cs"));
    }

    [Test]
    public void GenerateFromSarifJson_CreatesSuppressibleBaselineEntries()
    {
        var baseline = SharpProofBaseline.GenerateFromSarifJson(
            SarifResult("SP0002", "M:Sample.Impure", "src/Sample.cs", "Method is impure"));

        Assert.That(baseline.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(baseline.Diagnostics[0].Id, Is.EqualTo("SP0002"));
        Assert.That(baseline.Diagnostics[0].Symbol, Is.EqualTo("M:Sample.Impure"));
        Assert.That(baseline.Diagnostics[0].Path, Is.EqualTo("src/Sample.cs"));
        Assert.That(baseline.Diagnostics[0].Message, Is.EqualTo("Method is impure"));
        Assert.That(baseline.Diagnostics[0].Line, Is.EqualTo(10));
        Assert.That(baseline.Diagnostics[0].Column, Is.EqualTo(15));
        Assert.That(baseline.Diagnostics[0].OperationKind, Is.EqualTo("Invocation"));
        Assert.That(baseline.Diagnostics[0].Contract, Is.EqualTo("[EnforcePure]"));
        Assert.That(baseline.Diagnostics[0].EvidenceKey, Is.EqualTo("impure-call"));
        Assert.That(baseline.EvidenceSchemaVersion, Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(baseline.EvidenceSchemaCompatibility,
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        Assert.That(baseline.Diagnostics[0].EvidenceSchemaVersion,
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(baseline.Diagnostics[0].EvidenceSchemaCompatibility,
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));

        var json = SharpProofBaseline.ToJson(baseline);
        Assert.That(json, Does.Contain(@"""diagnostics"""));
        Assert.That(json, Does.Contain(@"""symbol"": ""M:Sample.Impure"""));
        using var jsonDocument = JsonDocument.Parse(json);
        Assert.That(jsonDocument.RootElement.GetProperty("evidenceSchemaVersion").GetInt32(),
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(jsonDocument.RootElement.GetProperty("evidenceSchemaCompatibility").GetString(),
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        Assert.That(jsonDocument.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("evidenceSchemaVersion").GetInt32(),
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));

        var reparsed = SharpProofBaseline.ParseBaselineJson(json);
        Assert.That(reparsed.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(reparsed.Diagnostics[0].Symbol, Is.EqualTo("M:Sample.Impure"));
    }

    [Test]
    public void ParseBaselineJson_RejectsLegacyAndFutureEvidenceSchemas()
    {
        const string legacy =
            "{\"version\":1,\"diagnostics\":[{\"id\":\"SP0002\",\"symbol\":\"M:C.M\",\"path\":\"C.cs\"}]}";
        Assert.That(
            () => SharpProofBaseline.ParseBaselineJson(legacy),
            Throws.TypeOf<NotSupportedException>());

        const string legacyV1 =
            "{\"version\":1,\"evidenceSchemaVersion\":1," +
            "\"evidenceSchemaCompatibility\":\"additive-v1\",\"diagnostics\":[{" +
            "\"id\":\"SP0002\",\"symbol\":\"M:C.M\",\"path\":\"C.cs\"," +
            "\"evidenceSchemaVersion\":1,\"evidenceSchemaCompatibility\":\"additive-v1\"}]}";
        Assert.That(
            () => SharpProofBaseline.ParseBaselineJson(legacyV1),
            Throws.TypeOf<NotSupportedException>());

        const string future =
            "{\"evidenceSchemaVersion\":99,\"evidenceSchemaCompatibility\":\"future\"," +
            "\"diagnostics\":[{\"id\":\"SP0002\",\"symbol\":\"M:C.M\",\"path\":\"C.cs\"}]}";
        Assert.That(
            () => SharpProofBaseline.ParseBaselineJson(future),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void GenerateFromSarifJson_IgnoresDiagnosticsWithoutBaselineSymbol()
    {
        var baseline = SharpProofBaseline.GenerateFromSarifJson(@"{
  ""runs"": [
    {
      ""results"": [
        {
          ""ruleId"": ""SP0002"",
          ""message"": { ""text"": ""missing owner"" },
          ""properties"": {
            ""sharpproof.baseline.path"": ""src/Sample.cs""
          }
        }
      ]
    }
  ]
}");

        Assert.That(baseline.Diagnostics, Is.Empty);
    }

    [Test]
    public void Explain_ReportsMatchedAndStaleReasons()
    {
        var current = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0002", "M:Sample.Impure", "src/Sample.cs")));
        var baseline = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0002", "M:Sample.Impure", "SRC/SAMPLE.CS"),
            Entry("SP0099", "M:Sample.Impure", "src/Sample.cs"),
            Entry("SP0002", "M:Sample.Other", "src/Sample.cs"),
            Entry("SP0002", "M:Sample.Impure", "src/Renamed.cs")));

        var explanations = SharpProofBaseline.Explain(baseline, current);

        Assert.That(explanations[0].Matched, Is.True);
        Assert.That(explanations[0].Reason, Is.EqualTo("matched id, symbol, and path"));
        Assert.That(explanations[1].Reason, Is.EqualTo("no current diagnostic with id 'SP0099'"));
        Assert.That(explanations[2].Reason, Is.EqualTo("diagnostic id matched but symbol did not"));
        Assert.That(explanations[3].Reason, Is.EqualTo("diagnostic id and symbol matched but path did not"));
    }

    [Test]
    public void Prune_RemovesEntriesThatNoLongerMatchCurrentDiagnostics()
    {
        var current = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0002", "M:Sample.Impure", "src/Sample.cs")));
        var baseline = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0002", "M:Sample.Impure", "src/Sample.cs"),
            Entry("SP0002", "M:Sample.Other", "src/Sample.cs")));

        var result = SharpProofBaseline.Prune(baseline, current);

        Assert.That(result.Kept, Is.EqualTo(1));
        Assert.That(result.Pruned, Is.EqualTo(1));
        Assert.That(result.Baseline.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(result.Baseline.Diagnostics[0].Symbol, Is.EqualTo("M:Sample.Impure"));
    }

    [Test]
    public void Explain_TreatsMissingOptionalIdentityAsWildcard()
    {
        var current = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0013", "M:Sample.Allocate", "src/Sample.cs") with
            {
                Line = 12,
                Column = 20,
                OperationKind = "ObjectCreation",
                EvidenceKey = "object_creation@100:112"
            }));
        var baseline = new BaselineDocument(ImmutableArray.Create(
            Entry("SP0013", "M:Sample.Allocate", "src/Sample.cs"),
            Entry("SP0013", "M:Sample.Allocate", "src/Sample.cs") with
            {
                Line = 15,
                Column = 20,
                OperationKind = "ObjectCreation",
                EvidenceKey = "object_creation@200:212"
            }));

        var explanations = SharpProofBaseline.Explain(baseline, current);

        Assert.That(explanations[0].Matched, Is.True);
        Assert.That(explanations[0].Reason, Is.EqualTo("matched id, symbol, and path"));
        Assert.That(explanations[1].Matched, Is.False);
        Assert.That(explanations[1].Reason,
            Is.EqualTo("diagnostic id, symbol, and path matched but instance identity did not"));
    }

    private static BaselineEntry Entry(string id, string symbol, string path)
    {
        return new BaselineEntry(id, symbol, path);
    }

    private static string SarifResult(
        string id,
        string symbol,
        string path,
        string message)
    {
        return @"{
  ""runs"": [
    {
      ""results"": [
        {
          ""ruleId"": """ + id + @""",
          ""message"": { ""text"": """ + message + @""" },
          ""locations"": [
            {
              ""physicalLocation"": {
                ""artifactLocation"": { ""uri"": """ + path + @""" },
                ""region"": { ""startLine"": 10, ""startColumn"": 15 }
              }
            }
          ],
          ""properties"": {
            ""sharpproof.baseline.symbol"": """ + symbol + @""",
            ""sharpproof.baseline.path"": """ + path + @""",
            ""sharpproof.baseline.operation_kind"": ""Invocation"",
            ""sharpproof.baseline.contract"": ""[EnforcePure]"",
            ""sharpproof.baseline.evidence_key"": ""impure-call"",
            ""sharpproof.evidence.schema_version"": ""2"",
            ""sharpproof.evidence.schema_compatibility"": ""exact-v2""
          }
        }
      ]
    }
  ]
}";
    }
}
