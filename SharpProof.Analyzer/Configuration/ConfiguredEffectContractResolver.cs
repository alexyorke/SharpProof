using System.Security.Cryptography;
using System.Text;
using SharpProof.Attributes;
namespace SharpProof.Analyzer.Configuration;
internal sealed class ConfiguredEffectContractResolver(AnalyzerConfigOptions options) {
    private const string Prefix = "sharpproof_effect_contract.";
    internal MethodEffects? Resolve(IMethodSymbol method) {
        var canonicalKey = RoslynStructuralMethodIdentity.GetCanonicalKey(method);
        var optionKey = Prefix + Sha256(canonicalKey);
        if (!options.TryGetValue(optionKey, out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("key", out var keyElement) ||
                !string.Equals(keyElement.GetString(), canonicalKey, StringComparison.Ordinal) ||
                !string.Equals(optionKey, Prefix + Sha256(keyElement.GetString() ?? string.Empty), StringComparison.Ordinal))
                return Unknown("effect_contract_hash_or_key_mismatch");
            if (!TryReadFlags(root, "effects", SharpProofEffect.None, out SharpProofEffect effects))
                return Unknown("invalid_configured_effect_flags");
            if (!TryReadFlags(root, "capabilities", SharpProofCapability.None, out SharpProofCapability capabilities))
                return Unknown("invalid_configured_capability_flags");
            if (root.TryGetProperty("complete", out var configuredComplete) &&
                configuredComplete.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Unknown("invalid_configured_effect_contract_complete");
            if (root.TryGetProperty("deterministic", out var configuredDeterministic) &&
                configuredDeterministic.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Unknown("invalid_configured_effect_contract_deterministic");
            var complete = root.TryGetProperty("complete", out var completeElement) &&
                           completeElement.ValueKind == JsonValueKind.True;
            var deterministic = !root.TryGetProperty("deterministic", out var deterministicElement) ||
                                deterministicElement.ValueKind != JsonValueKind.False;
            if (!deterministic) effects |= SharpProofEffect.UsesNondeterminism;
            if (!complete) effects |= SharpProofEffect.Unknown;
            var exceptions = ImmutableArray.CreateBuilder<string>();
            if (root.TryGetProperty("exceptions", out var exceptionElement)) {
                if (exceptionElement.ValueKind != JsonValueKind.Array)
                    return Unknown("invalid_configured_effect_contract_exceptions");
                foreach (var item in exceptionElement.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                        return Unknown("invalid_configured_effect_contract_exception");
                    exceptions.Add(item.GetString()!);
                }
            }
            if (exceptions.Count != 0) effects |= SharpProofEffect.Throws;
            return new MethodEffects(
                effects,
                capabilities,
                [.. exceptions.Select(static type => MethodExceptionFact.Boundary(
                    type,
                    MethodExceptionSource.Contract,
                    "configured_effect_contract"))],
                [],
                complete
                    ? []
                    : [Reason("partial_configured_effect_contract")]);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
            return Unknown("malformed_configured_effect_contract");
        }
    }
    private static bool TryReadFlags<T>(JsonElement root, string property, T fallback, out T value) where T : struct, Enum {
        if (!root.TryGetProperty(property, out var element)) {
            value = fallback;
            return true;
        }
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
            value = (T)Enum.ToObject(typeof(T), number);
        else if (element.ValueKind == JsonValueKind.String &&
                 Enum.TryParse<T>(element.GetString()?.Replace('|', ','), true, out var parsed))
            value = parsed;
        else {
            value = fallback;
            return false;
        }
        ulong knownBits = 0;
        foreach (var defined in Enum.GetValues(typeof(T)))
            knownBits |= Convert.ToUInt64(defined, CultureInfo.InvariantCulture);
        var actualBits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        return (actualBits & ~knownBits) == 0;
    }
    private static string Sha256(string value) {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
    private static MethodEffects Unknown(string reason) => new(
        SharpProofEffect.Unknown,
        SharpProofCapability.None,
        [MethodExceptionFact.Boundary(
            "System.Exception",
            MethodExceptionSource.Contract,
            reason,
            SharpProofVerdict.Unknown)],
        [],
        [Reason(reason)]);
    private static SharpProofUnknownReason Reason(string reason) => new("SP-EFFECT-CONTRACT", "Configuration", reason, false, true);
}
