using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class Pr275CorpusInputParserTests
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void ExistingSupportPreservesTheWholeReviewedCatalog()
    {
        var catalog = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var expected = catalog.Methods.ToImmutableDictionary(
            method => method.DeclarationSha256, method => method.Support, StringComparer.Ordinal);
        var actual = CorpusInputParsers.ReadExistingSupport(catalog);
        Assert.That(actual, Is.EquivalentTo(expected));
        Assert.That(actual.KeyComparer, Is.SameAs(StringComparer.Ordinal));
    }

    [TestCase("missing", "The existing OSS corpus manifest has no methods array.")]
    [TestCase("null-entry", "The existing OSS corpus manifest contains a null method entry.")]
    public void ExistingSupportRequiresAnInitializedArrayOfMethods(string input, string message)
    {
        var path = Path.Combine(RepositoryLayout.FindRoot(), "SharpProof.Gates", "Corpus", "oss-methods.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (input == "missing")
        {
            json.Remove("methods");
        }
        else
        {
            json["methods"]!.AsArray()[0] = null;
        }
        var parsed = JsonSerializer.Deserialize<OpenSourceCorpusDocument>(json.ToJsonString(), ImportJsonOptions)!;
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusInputParsers.ReadExistingSupport(parsed)));
        Assert.That(error!.Message, Is.EqualTo(message));
    }

    [Test]
    public void ReviewedAllowancesRemainEmpty()
    {
        var path = Path.Combine(RepositoryLayout.FindRoot(), "SharpProof.Gates", "Corpus", "proven-to-unknown.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var actual = CorpusInputParsers.ReadAllowances(document.RootElement);
        Assert.That(actual, Is.Empty);
        Assert.That(actual.KeyComparer, Is.SameAs(StringComparer.Ordinal));
    }

    [Test]
    public void ParserPreservesAnActualCatalogCaseAndItsExplanation()
    {
        var json = CreateAllowanceInput();
        var entry = json["allowances"]!.AsArray()[0]!.AsObject();
        var caseId = entry["caseId"]!.GetValue<string>();
        var reason = entry["reason"]!.GetValue<string>();
        using var document = JsonDocument.Parse(json.ToJsonString());
        var actual = CorpusInputParsers.ReadAllowances(document.RootElement);
        Assert.That(actual.Count, Is.EqualTo(1));
        Assert.That(actual[caseId], Is.EqualTo(new ProvenToUnknownAllowance(caseId, reason)));
        Assert.That(actual.KeyComparer, Is.SameAs(StringComparer.Ordinal));
    }

    [TestCase("schemaVersion")]
    [TestCase("allowances")]
    [TestCase("caseId")]
    [TestCase("reason")]
    public void AllowancePropertiesAreRequired(string property)
    {
        var json = CreateAllowanceInput();
        var target = property is "schemaVersion" or "allowances"
            ? json
            : json["allowances"]!.AsArray()[0]!.AsObject();
        target.Remove(property);
        AssertAllowanceError(json, $"Corpus JSON is missing required property '{property}'.");
    }

    [Test]
    public void UnsupportedAllowanceSchemaRetainsItsError()
    {
        var json = CreateAllowanceInput();
        json["schemaVersion"] = 2;
        AssertAllowanceError(json, "Unsupported Proven->Unknown allowance schema.");
    }

    [TestCase("caseId", null)]
    [TestCase("caseId", "")]
    [TestCase("caseId", " \t")]
    [TestCase("reason", null)]
    [TestCase("reason", "")]
    [TestCase("reason", " \t")]
    public void AllowanceFieldsRequireText(string field, string? value)
    {
        var json = CreateAllowanceInput();
        json["allowances"]!.AsArray()[0]!.AsObject()[field] = value;
        AssertAllowanceError(json, "Every Proven->Unknown allowance needs a caseId and explanation.");
    }

    [Test]
    public void DuplicateActualCatalogCaseRetainsItsError()
    {
        var json = CreateAllowanceInput();
        var entries = json["allowances"]!.AsArray();
        var caseId = entries[0]!["caseId"]!.GetValue<string>();
        entries.Add(entries[0]!.DeepClone());
        AssertAllowanceError(json, $"Duplicate Proven->Unknown allowance: {caseId}");
    }

    private static JsonObject CreateAllowanceInput()
    {
        var catalog = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["allowances"] = new JsonArray(new JsonObject
            {
                ["caseId"] = catalog.Methods[0].Id,
                ["reason"] = "Parser control for the existing reviewed corpus case."
            })
        };
    }

    private static void AssertAllowanceError(JsonObject json, string message)
    {
        using var document = JsonDocument.Parse(json.ToJsonString());
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusInputParsers.ReadAllowances(document.RootElement)));
        Assert.That(error!.Message, Is.EqualTo(message));
    }
}
