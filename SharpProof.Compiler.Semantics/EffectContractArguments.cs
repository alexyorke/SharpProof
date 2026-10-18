namespace SharpProof.Compiler.Semantics;

internal static class EffectContractArguments
{
    internal static (EffectContractCapabilityKind Value, bool IsValid) DecodeCapabilities(
        ImmutableArray<AttributeData> attributes, Action<AttributeData, string, string> reportInvalid)
    {
        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        reportInvalid = ArgumentNullGuard.NotNull(reportInvalid, nameof(reportInvalid));
        var value = EffectContractCapabilityKind.None;
        foreach (var attribute in attributes)
        {
            if (attribute.ConstructorArguments.Length == 1 &&
                EffectContractMetadata.TryConvertInt64(attribute.ConstructorArguments[0].Value, out var raw) &&
                raw >= 0 && (raw & ~(long)EffectContractMetadata.AllCapabilities) == 0)
            {
                value |= (EffectContractCapabilityKind)raw;
                continue;
            }
            reportInvalid(attribute, "[AllowedCapabilities]", "expected a defined SharpProofCapability flags value");
            return (EffectContractCapabilityKind.None, false);
        }
        return (value, true);
    }

    internal static (ImmutableArray<INamedTypeSymbol> Types, bool IsValid) DecodeAllowedExceptions(
        ImmutableArray<AttributeData> attributes, Compilation compilation,
        Action<AttributeData, string, string> reportInvalid)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        reportInvalid = ArgumentNullGuard.NotNull(reportInvalid, nameof(reportInvalid));
        var exceptionType = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var valid = true;
        foreach (var attribute in attributes)
        {
            var arguments = attribute.ConstructorArguments;
            var values = arguments.Length == 1 && arguments[0].Kind == TypedConstantKind.Array
                ? arguments[0].Values : default;
            if (exceptionType != null && !values.IsDefault &&
                values.All(argument => argument.Value is INamedTypeSymbol type && !type.IsUnboundGenericType &&
                    EffectTypeFacts.IsDerivedFrom(type, exceptionType)))
            {
                types.AddRange(values.Select(static argument => (argument.Kind == TypedConstantKind.Array) ? throw new ArgumentException("Value must be initialized.", nameof(argument)) : (INamedTypeSymbol)argument.Value!));
                continue;
            }
            reportInvalid(attribute, "[AllowedExceptions]", "expected only closed System.Exception-derived types");
            valid = false;
        }
        return valid ? (types.ToImmutable(), true) : ([], false);
    }
}
