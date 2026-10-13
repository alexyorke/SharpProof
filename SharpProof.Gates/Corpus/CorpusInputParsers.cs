using System.Collections.Immutable;
using System.Text.Json;

namespace SharpProof.Gates.Corpus;

internal static class CorpusInputParsers
{
    internal static ImmutableDictionary<string, CorpusSupport> ReadExistingSupport(
        OpenSourceCorpusDocument existing)
    {
        if (existing.Methods.IsDefault)
        {
            throw new InvalidDataException(
                "The existing OSS corpus manifest has no methods array.");
        }
        if (existing.Methods.Any(static method => method is null))
        {
            throw new InvalidDataException(
                "The existing OSS corpus manifest contains a null method entry.");
        }

        return existing.Methods.ToImmutableDictionary(
            static method => method.DeclarationSha256,
            static method => method.Support,
            StringComparer.Ordinal);
    }

    internal static ImmutableDictionary<string, ProvenToUnknownAllowance> ReadAllowances(
        JsonElement root)
    {
        if (RequiredProperty(root, "schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException(
                "Unsupported Proven->Unknown allowance schema.");
        }

        var result = ImmutableDictionary.CreateBuilder<
            string,
            ProvenToUnknownAllowance>(StringComparer.Ordinal);
        foreach (var element in RequiredProperty(root, "allowances")
                     .EnumerateArray())
        {
            var caseId = RequiredProperty(element, "caseId").GetString();
            var reason = RequiredProperty(element, "reason").GetString();
            if (string.IsNullOrWhiteSpace(caseId) ||
                string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidDataException(
                    "Every Proven->Unknown allowance needs a caseId and explanation.");
            }

            if (!result.TryAdd(
                    caseId,
                    new ProvenToUnknownAllowance(caseId, reason)))
            {
                throw new InvalidDataException(
                    $"Duplicate Proven->Unknown allowance: {caseId}");
            }
        }
        return result.ToImmutable();
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException(
                $"Corpus JSON is missing required property '{name}'.");
    }
}
