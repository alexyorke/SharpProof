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
            static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.DeclarationSha256),
            static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Support),
            StringComparer.Ordinal);
    }

    internal static CorpusUnknownReasonRatchet ReadUnknownReasonRatchet(JsonElement root)
    {
        if (RequiredProperty(root, "schemaVersion").GetInt32() != 2)
        {
            throw new InvalidDataException(
                "Unsupported corpus Unknown-reason ratchet schema.");
        }

        var minimumSupportedCases =
            RequiredProperty(root, "minimumSupportedCases").GetInt32();
        var minimumSupportedOpenSourceMethods =
            RequiredProperty(root, "minimumSupportedOpenSourceMethods").GetInt32();
        var maximumTotalUnknown =
            RequiredProperty(root, "maximumTotalUnknown").GetInt32();
        if (minimumSupportedCases < 0 ||
            minimumSupportedOpenSourceMethods < 0 ||
            maximumTotalUnknown < 0)
        {
            throw new InvalidDataException(
                "Corpus support minima and Unknown maxima cannot be negative.");
        }

        var maximumByReason =
            ImmutableDictionary.CreateBuilder<string, int>(
                StringComparer.Ordinal);
        foreach (var property in RequiredProperty(root, "maximumByReason")
                     .EnumerateObject())
        {
            var maximum = property.Value.GetInt32();
            if (string.IsNullOrWhiteSpace(property.Name) || maximum < 0)
            {
                throw new InvalidDataException(
                    "Corpus Unknown-reason maxima need a name and " +
                    "a non-negative count.");
            }

            if (!maximumByReason.TryAdd(property.Name, maximum))
            {
                throw new InvalidDataException(
                    $"Duplicate corpus Unknown reason: {property.Name}.");
            }
        }
        return new CorpusUnknownReasonRatchet(
            minimumSupportedCases,
            minimumSupportedOpenSourceMethods,
            maximumTotalUnknown,
            maximumByReason.ToImmutable());
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
