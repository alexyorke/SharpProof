using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SharpProof.Frontend;

internal sealed class ContractApiPayloadPolicy
{
    private const string AttributesAssemblyMvidMetadataKey = ContractApiMetadata.AttributesAssemblyMvidMetadataKey;
    private readonly ImmutableArray<byte> _payloadSha256;
    private readonly Guid _moduleVersionId;

    private ContractApiPayloadPolicy(Assembly owner)
    {
        _payloadSha256 = ReadExpectedPayloadSha256(owner);
        _moduleVersionId = ReadExpectedModuleVersionId(owner);
        ExpectedAssemblyVersion = owner.GetName().Version ??
            throw new InvalidOperationException("SharpProof.Frontend has no assembly version.");
    }

    internal Version ExpectedAssemblyVersion { get; }

    internal static ContractApiPayloadPolicy FromRuntimeOwner(Assembly owner)
    {
        if (owner is null)
        {
            throw new System.ArgumentNullException(nameof(owner));
        }
        return new(owner);
    }

    internal bool HasTrustedPayload(
        Compilation compilation, IAssemblySymbol assembly, out string? unreadableReason)
    {
        if (assembly is null)
        {
            throw new System.ArgumentNullException(nameof(assembly));
        }
        if (compilation is null)
        {
            throw new System.ArgumentNullException(nameof(compilation));
        }
        unreadableReason = null;
        if (_payloadSha256.IsDefaultOrEmpty)
        {
            return false;
        }

        var matches = new List<PortableExecutableReference>();
        foreach (var reference in compilation.References)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    assembly,
                    compilation.GetAssemblyOrModuleSymbol(reference)))
            {
                continue;
            }

            if (reference is PortableExecutableReference portable)
            {
                matches.Add(portable);
            }
        }
        if (matches.Count == 0)
        {
            return false;
        }

        var trusted = true;
        foreach (var match in matches)
        {
            var current = match.FilePath is { Length: > 0 } path
                ? MatchesBoundFilePayload(path, match, out var currentReason)
                : MatchesImagePayload(match, out currentReason);
            trusted &= current;
            unreadableReason ??= currentReason;
        }

        return trusted;
    }

    /// <summary>
    /// Distinguishes a payload that does not match from a payload that could not
    /// be read at all. Both disable contract analysis, but only the second is an
    /// environment fault the user can act on, and reporting it is the difference
    /// between an explained failure and the analyzer silently doing nothing.
    /// </summary>
    internal bool MatchesBoundFilePayload(string path, PortableExecutableReference reference, out string? unreadableReason)
    {
        unreadableReason = null;
        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!reader.HasMetadata)
            {
                return false;
            }
            var metadataReader = reader.GetMetadataReader();
            if (metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid) !=
                GetBoundModuleVersionId(reference))
            {
                return false;
            }
            stream.Position = 0;
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(stream).SequenceEqual(
                _payloadSha256);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                BadImageFormatException or
                IOException or
                InvalidOperationException or
                NotSupportedException or
                UnauthorizedAccessException or
                SecurityException or
                CryptographicException)
        {
            unreadableReason = exception.Message;
            return false;
        }
    }

    private static Guid GetBoundModuleVersionId(PortableExecutableReference reference)
    {
        if (reference is null)
        {
            throw new System.ArgumentNullException(nameof(reference));
        }
        return reference.GetMetadata() is AssemblyMetadata metadata &&
            metadata.GetModules() is { Length: 1 } modules
                ? modules[0].GetModuleVersionId()
                : Guid.Empty;
    }

    internal bool MatchesImagePayload(
        PortableExecutableReference reference,
        out string? unreadableReason)
    {
        unreadableReason = null;
        try
        {
            if (_moduleVersionId == Guid.Empty ||
                reference.GetMetadata() is not AssemblyMetadata metadata)
            {
                return false;
            }

            var modules = metadata.GetModules();
            return modules.Length == 1 &&
                modules[0].GetModuleVersionId() ==
                    _moduleVersionId;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or
                IOException or
                InvalidOperationException or
                NotSupportedException)
        {
            unreadableReason = exception.Message;
            return false;
        }
    }

    private static ImmutableArray<byte>
        ReadExpectedPayloadSha256(Assembly owner)
    {
        var values = ReadAssemblyMetadataValues(
            owner, ContractApiMetadata.AttributesPayloadSha256MetadataKey);
        if (values.Length != 1 ||
            values[0] is not
            {
                Length: 64
            } value)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<byte>(32);
        for (var index = 0; index < value.Length; index += 2)
        {
            if (!byte.TryParse(
                    value.Substring(index, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return [];
            }

            result.Add(parsed);
        }

        return result.MoveToImmutable();
    }

    private static Guid ReadExpectedModuleVersionId(Assembly owner)
    {
        if (owner is null)
        {
            throw new System.ArgumentNullException(nameof(owner));
        }
        var values = ReadAssemblyMetadataValues(
            owner, AttributesAssemblyMvidMetadataKey);
        return values.Length == 1 &&
            Guid.TryParseExact(values[0], "D", out var result)
                ? result
                : Guid.Empty;
    }

    private static ImmutableArray<string> ReadAssemblyMetadataValues(
        Assembly owner, string key)
    {
        if (key is null)
        {
            throw new System.ArgumentNullException(nameof(key));
        }
        if (owner is null)
        {
            throw new System.ArgumentNullException(nameof(owner));
        }
        return owner.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (string.Equals(
                attribute.Key,
                key,
                StringComparison.Ordinal)))
            .Select(static attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.Value))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
    }

}
