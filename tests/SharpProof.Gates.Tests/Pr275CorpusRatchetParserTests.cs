using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class Pr275CorpusRatchetParserTests
{
    [Test]
    public void ParserPreservesTheCompleteReviewedRatchet()
    {
        using var document = JsonDocument.Parse(ReadReviewedJson());
        var root = document.RootElement;
        var actual = CorpusInputParsers.ReadUnknownReasonRatchet(root);
        Assert.That(actual.MinimumSupportedCases, Is.EqualTo(root.GetProperty("minimumSupportedCases").GetInt32()));
        Assert.That(actual.MinimumSupportedOpenSourceMethods, Is.EqualTo(root.GetProperty("minimumSupportedOpenSourceMethods").GetInt32()));
        Assert.That(actual.MaximumTotalUnknown, Is.EqualTo(root.GetProperty("maximumTotalUnknown").GetInt32()));
        var reasons = root.GetProperty("maximumByReason").EnumerateObject().ToImmutableArray();
        Assert.That(actual.MaximumByReason.Count, Is.EqualTo(reasons.Length));
        foreach (var reason in reasons)
        {
            Assert.That(actual.MaximumByReason[reason.Name], Is.EqualTo(reason.Value.GetInt32()));
        }
        Assert.That(actual.MaximumByReason.KeyComparer, Is.SameAs(StringComparer.Ordinal));
    }

    [TestCase("schemaVersion")]
    [TestCase("minimumSupportedCases")]
    [TestCase("minimumSupportedOpenSourceMethods")]
    [TestCase("maximumTotalUnknown")]
    [TestCase("maximumByReason")]
    public void RatchetPropertiesAreRequired(string property)
    {
        System.ArgumentNullException.ThrowIfNull(property);
        var json = ReviewedInput();
        json.Remove(property);
        AssertError(json, $"Corpus JSON is missing required property '{property}'.");
    }

    [Test]
    public void UnsupportedSchemaRetainsItsError()
    {
        var json = ReviewedInput();
        json["schemaVersion"] = 1;
        AssertError(json, "Unsupported corpus Unknown-reason ratchet schema.");
    }

    [TestCase("minimumSupportedCases")]
    [TestCase("minimumSupportedOpenSourceMethods")]
    [TestCase("maximumTotalUnknown")]
    public void ThresholdsCannotBeNegative(string property)
    {
        System.ArgumentNullException.ThrowIfNull(property);
        var json = ReviewedInput();
        json[property] = -1;
        AssertError(json, "Corpus support minima and Unknown maxima cannot be negative.");
    }

    [TestCase("minimumSupportedCases")]
    [TestCase("minimumSupportedOpenSourceMethods")]
    [TestCase("maximumTotalUnknown")]
    public void ZeroIsAnAdmittedThreshold(string property)
    {
        var json = ReviewedInput();
        json[property] = 0;
        using var document = JsonDocument.Parse(json.ToJsonString());
        var actual = CorpusInputParsers.ReadUnknownReasonRatchet(document.RootElement);
        var threshold = property switch
        {
            "minimumSupportedCases" => actual.MinimumSupportedCases,
            "minimumSupportedOpenSourceMethods" => actual.MinimumSupportedOpenSourceMethods,
            "maximumTotalUnknown" => actual.MaximumTotalUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        Assert.That(threshold, Is.Zero);
    }

    [Test]
    public void AReasonNeedsAName()
    {
        var json = ReviewedInput();
        var reasons = json["maximumByReason"]!.AsObject();
        var existing = reasons.First();
        reasons.Remove(existing.Key);
        reasons[" \t"] = existing.Value!.DeepClone();
        AssertError(json, "Corpus Unknown-reason maxima need a name and a non-negative count.");
    }

    [Test]
    public void AnExistingReasonCannotHaveANegativeMaximum()
    {
        var json = ReviewedInput();
        var reasons = json["maximumByReason"]!.AsObject();
        reasons[reasons.First().Key] = -1;
        AssertError(json, "Corpus Unknown-reason maxima need a name and a non-negative count.");
    }

    [Test]
    public void AnExistingReasonCanHaveAZeroMaximum()
    {
        var json = ReviewedInput();
        var reasons = json["maximumByReason"]!.AsObject();
        var reason = reasons.First().Key;
        reasons[reason] = 0;
        using var document = JsonDocument.Parse(json.ToJsonString());
        var actual = CorpusInputParsers.ReadUnknownReasonRatchet(document.RootElement);
        Assert.That(actual.MaximumByReason[reason], Is.Zero);
    }

    [Test]
    public void DuplicateReviewedReasonRetainsItsError()
    {
        using var original = JsonDocument.Parse(ReadReviewedJson());
        using var stream = new MemoryStream();
        string? duplicate = null;
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in original.RootElement.EnumerateObject())
            {
                if (property.Name != "maximumByReason")
                {
                    property.WriteTo(writer);
                    continue;
                }
                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var reason in property.Value.EnumerateObject())
                {
                    reason.WriteTo(writer);
                    if (duplicate is null)
                    {
                        duplicate = reason.Name;
                        reason.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusInputParsers.ReadUnknownReasonRatchet(document.RootElement)));
        Assert.That(error!.Message, Is.EqualTo($"Duplicate corpus Unknown reason: {duplicate}."));
    }

    private static string ReadReviewedJson()
    {
        return File.ReadAllText(Path.Combine(
            RepositoryLayout.FindRoot(), "tests/SharpProof.Gates", "Corpus", "unknown-reason-ratchet.json"));
    }

    private static JsonObject ReviewedInput()
    {
        return JsonNode.Parse(ReadReviewedJson())!.AsObject();
    }

    private static void AssertError(JsonObject json, string message)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        System.ArgumentNullException.ThrowIfNull(message);
        using var document = JsonDocument.Parse(json.ToJsonString());
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusInputParsers.ReadUnknownReasonRatchet(document.RootElement)));
        Assert.That(error!.Message, Is.EqualTo(message));
    }
}
