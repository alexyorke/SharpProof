using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerContractsExportVersions
{
    internal const string Schema = "SharpProof.ContractsExport";
    internal const string StampedSchema = "SharpProof.ContractsExport.Stamped";
    internal const int Current = 1;
    internal const int MaximumCallables = 65536;
    internal const int MaximumClauses = 4096;
    internal const string ReferenceRole = "reference";
    internal const string ImplementationRole = "implementation";
}

// The Requires a project exports for its callers in other projects of the
// same build. The compiler collector writes it unstamped; an MSBuild step
// after CoreCompile wraps it with the produced images, so a caller accepts it
// only for the exact reference image it compiled against.
internal sealed record CompilerContractsExport
{
    [JsonRequired]
    public string Schema { get; init; } = string.Empty;
    [JsonRequired]
    public int Version { get; init; }
    [JsonRequired]
    public string AssemblyName { get; init; } = string.Empty;
    [JsonRequired]
    public string AssemblyIdentity { get; init; } = string.Empty;
    // Lowercase hexadecimal, empty for an unsigned assembly.
    [JsonRequired]
    public string PublicKeyToken { get; init; } = string.Empty;
    [JsonRequired]
    public string TargetFramework { get; init; } = string.Empty;
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerExportedCallable> Callables { get; init; } = [];
}

// One public or internal callable with source Requires. A callable whose
// clauses read anything other than its parameters is listed as incomplete,
// without clauses: callers count its calls as unchecked.
internal sealed record CompilerExportedCallable
{
    [JsonRequired]
    public string DocumentationId { get; init; } = string.Empty;
    // SHA-256 over the declaration and its contract source.
    [JsonRequired]
    public string DeclarationSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public bool Complete { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompilerTotalCallableArtifact? Requires { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<WorkerSourceLocation> ClauseLocations { get; init; } = [];
}

internal sealed record CompilerContractsExportImage
{
    [JsonRequired]
    public string Role { get; init; } = string.Empty;
    [JsonRequired]
    public string Mvid { get; init; } = string.Empty;
    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record CompilerStampedContractsExport
{
    [JsonRequired]
    public string Schema { get; init; } = string.Empty;
    [JsonRequired]
    public int Version { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerContractsExportImage> Images { get; init; } = [];
    [JsonRequired]
    public CompilerContractsExport Export { get; init; } = new();
}

// An accepted export, as the calling compilation captured it: the hash of the
// additional file it read and the images the stamp names.
internal sealed record CompilerContractExportSnapshot
{
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string AssemblyIdentity { get; init; } = string.Empty;
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<CompilerContractsExportImage> Images { get; init; } = [];
}

// The callee clause an exported row checks: which export, which reference
// image the call bound, which callable and clause, and where that clause is
// declared in the exporting project.
internal sealed record CompilerExportedClauseOrigin
{
    [JsonRequired]
    public string ExportSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public string AssemblyIdentity { get; init; } = string.Empty;
    [JsonRequired]
    public string ImageSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public string ModuleMvid { get; init; } = string.Empty;
    [JsonRequired]
    public string DocumentationId { get; init; } = string.Empty;
    [JsonRequired]
    public string DeclarationSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public int ClauseOrdinal { get; init; }
    [JsonRequired]
    public WorkerSourceLocation Location { get; init; } = new();

    // The simple name of the exporting assembly, for a message.
    internal string AssemblyName
    {
        get
        {
            var comma = AssemblyIdentity.IndexOf(',');
            return comma < 0 ? AssemblyIdentity : AssemblyIdentity.Substring(0, comma);
        }
    }
}

internal static class CompilerContractsExportJson
{
    internal static string Serialize(CompilerContractsExport export)
    {
        export = RepositoryParameterGuard.NotNull(export, nameof(export));
        return JsonSerializer.Serialize(export, WorkerProtocolJson.SharedOptions) + "\n";
    }

    // Null when the text is not a well-formed stamped export of this version.
    internal static CompilerStampedContractsExport? TryDeserializeStamped(string json)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        if (Encoding.UTF8.GetByteCount(json) > WorkerProtocolJson.MaximumJsonBytes)
        { return null; }
        try
        {
            var stamped = JsonSerializer.Deserialize<CompilerStampedContractsExport>(json, WorkerProtocolJson.SharedOptions);
            return stamped != null && IsValid(stamped) ? stamped : null;
        }
        catch (JsonException)
        { return null; }
        catch (NotSupportedException)
        { return null; }
    }

    private static bool IsValid(CompilerStampedContractsExport stamped)
    {
        var export = stamped.Export;
        return stamped.Schema == CompilerContractsExportVersions.StampedSchema &&
            stamped.Version == CompilerContractsExportVersions.Current &&
            !stamped.Images.IsDefault && stamped.Images.Length is >= 1 and <= 2 &&
            stamped.Images.All(static image => image != null &&
                image.Role is CompilerContractsExportVersions.ReferenceRole or CompilerContractsExportVersions.ImplementationRole &&
                IsSha256(image.Sha256) && Guid.TryParseExact(image.Mvid, "D", out var mvid) &&
                mvid.ToString("D") == image.Mvid) &&
            stamped.Images.Select(static image => image.Role).Distinct(StringComparer.Ordinal).Count() == stamped.Images.Length &&
            export is
            {
                Schema: CompilerContractsExportVersions.Schema,
                Version: CompilerContractsExportVersions.Current
            } &&
            !string.IsNullOrWhiteSpace(export.AssemblyName) && export.AssemblyName.Length <= 1024 &&
            !string.IsNullOrWhiteSpace(export.AssemblyIdentity) && export.AssemblyIdentity.Length <= 4096 &&
            export.PublicKeyToken is { Length: 0 or 16 } && export.PublicKeyToken.All(IsLowerHex) &&
            export.TargetFramework != null && export.TargetFramework.Length <= 256 &&
            !export.Callables.IsDefault && export.Callables.Length <= CompilerContractsExportVersions.MaximumCallables &&
            export.Callables.All(static callable => callable != null &&
                !string.IsNullOrWhiteSpace(callable.DocumentationId) && callable.DocumentationId.Length <= 4096 &&
                callable.DocumentationId.StartsWith("M:", StringComparison.Ordinal) &&
                IsSha256(callable.DeclarationSha256) && !callable.ClauseLocations.IsDefault &&
                callable.ClauseLocations.Length <= CompilerContractsExportVersions.MaximumClauses &&
                callable.ClauseLocations.All(static location => location != null && WorkerProtocolMetadata.IsSourceLocationValid(location)) &&
                (callable.Complete
                    ? callable.Requires is { } requires && !requires.Clauses.IsDefault &&
                        requires.Clauses.Length == callable.ClauseLocations.Length && requires.Clauses.Length != 0
                    : callable.Requires == null && callable.ClauseLocations.IsEmpty)) &&
            export.Callables.Select(static callable => callable.DocumentationId).Distinct(StringComparer.Ordinal).Count() ==
                export.Callables.Length;
    }

    internal static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(IsLowerHex);
    }

    private static bool IsLowerHex(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f';
    }

    // The evidence an exported row's marker name commits to.
    internal static bool TryGetOriginDigest(CompilerExportedClauseOrigin? origin, out string digest)
    {
        digest = string.Empty;
        if (!IsValidOrigin(origin))
        { return false; }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("exported");
            writer.Write(origin!.ExportSha256);
            writer.Write(origin.AssemblyIdentity);
            writer.Write(origin.ImageSha256);
            writer.Write(origin.ModuleMvid);
            writer.Write(origin.DocumentationId);
            writer.Write(origin.DeclarationSha256);
            writer.Write(origin.ClauseOrdinal);
            writer.Write(origin.Location.Path);
            writer.Write(origin.Location.Start);
            writer.Write(origin.Location.Length);
            writer.Write(origin.Location.Line);
            writer.Write(origin.Location.Column);
        }
        stream.Position = 0;
        using var sha = System.Security.Cryptography.SHA256.Create();
        digest = string.Concat(sha.ComputeHash(stream).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return true;
    }

    internal static bool IsValidOrigin(CompilerExportedClauseOrigin? origin)
    {
        return origin != null && IsSha256(origin.ExportSha256) && IsSha256(origin.ImageSha256) &&
            IsSha256(origin.DeclarationSha256) &&
            !string.IsNullOrWhiteSpace(origin.AssemblyIdentity) && origin.AssemblyIdentity.Length <= 4096 &&
            Guid.TryParseExact(origin.ModuleMvid, "D", out var mvid) && mvid.ToString("D") == origin.ModuleMvid &&
            !string.IsNullOrWhiteSpace(origin.DocumentationId) && origin.DocumentationId.Length <= 4096 &&
            origin.ClauseOrdinal is >= 0 and < CompilerContractsExportVersions.MaximumClauses &&
            origin.Location != null && WorkerProtocolMetadata.IsSourceLocationValid(origin.Location) &&
            origin.Location.Path.Length <= 4096;
    }

    // The calling compilation recorded the export this row cites, the export
    // was stamped with the image the row names, and the compilation resolved
    // that image as a reference of the exporting assembly.
    internal static bool OwnsOrigin(CompilerExportedClauseOrigin origin,
        ImmutableArray<CompilerContractExportSnapshot>? exports, ImmutableArray<CompilerReferenceSnapshot>? references)
    {
        origin = RepositoryParameterGuard.NotNull(origin, nameof(origin));
        return exports is { IsDefault: false } captured && references is { IsDefault: false } resolved &&
            captured.Any(export => export != null && export.Sha256 == origin.ExportSha256 &&
                export.AssemblyIdentity == origin.AssemblyIdentity && !export.Images.IsDefault &&
                export.Images.Any(image => image != null && image.Sha256 == origin.ImageSha256 && image.Mvid == origin.ModuleMvid)) &&
            resolved.Any(reference => reference != null && reference.Identity == origin.AssemblyIdentity && !reference.Modules.IsDefault &&
                reference.Modules.Any(module => module != null && module.Sha256 == origin.ImageSha256 && module.Mvid == origin.ModuleMvid &&
                    module.SizeBytes is > 0 and <= CompilerReferenceLimits.MaximumModuleBytes));
    }
}
