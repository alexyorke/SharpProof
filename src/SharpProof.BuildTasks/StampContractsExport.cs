using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace SharpProof.BuildTasks;

// Binds the compiler collector's contract export to the images this
// compilation produced. A caller accepts the stamped export only for the
// exact reference image it compiled against, so an export that survives a
// later compilation of other sources no longer matches anything.
public sealed class StampContractsExport : CancelableBuildTask
{
    // CompilerContractsExportVersions in SharpProof.CompilerArtifact.
    private const string ExportSchema = "SharpProof.ContractsExport";
    private const string StampedSchema = "SharpProof.ContractsExport.Stamped";
    private const int Version = 1;
    private const int MaximumBytes = 16 * 1024 * 1024;

    [Required]
    public string UnstampedPath { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string ImplementationAssembly { get; set; } = string.Empty;

    public string? ReferenceAssembly { get; set; }

    // True only when the collector completed this very compilation. Otherwise
    // the previous export is removed: it describes sources that may be gone.
    public bool CaptureCompleted { get; set; }

    public string? ProjectDirectory { get; set; }

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        try
        {
            var output = ResolveProjectRelativePath(ProjectDirectory, OutputPath);
            var unstamped = ResolveProjectRelativePath(ProjectDirectory, UnstampedPath);
            if (File.Exists(output))
            { File.Delete(output); }
            if (!CaptureCompleted || !File.Exists(unstamped))
            { return true; }
            var info = new FileInfo(unstamped);
            if (info.Length is <= 0 or > MaximumBytes)
            {
                Log.LogWarning("SharpProof contract export '{0}' exceeds its size limit and was not stamped.", unstamped);
                return true;
            }
            var export = File.ReadAllBytes(unstamped);
            using (var document = JsonDocument.Parse(export))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("schema", out var schema) || schema.GetString() != ExportSchema ||
                    !document.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != Version)
                {
                    Log.LogWarning("SharpProof contract export '{0}' has an unknown schema and was not stamped.", unstamped);
                    return true;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var images = new List<(string Role, string Mvid, string Sha256)>
            { Image("implementation", ResolveProjectRelativePath(ProjectDirectory, ImplementationAssembly)) };
            if (!string.IsNullOrWhiteSpace(ReferenceAssembly) &&
                ResolveProjectRelativePath(ProjectDirectory, ReferenceAssembly) is var reference && File.Exists(reference))
            { images.Add(Image("reference", reference)); }
            cancellationToken.ThrowIfCancellationRequested();
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", StampedSchema);
                writer.WriteNumber("version", Version);
                writer.WriteStartArray("images");
                foreach (var (role, mvid, sha256) in images)
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", role);
                    writer.WriteString("mvid", mvid);
                    writer.WriteString("sha256", sha256);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WritePropertyName("export");
                writer.WriteRawValue(export);
                writer.WriteEndObject();
            }
            buffer.WriteByte((byte)'\n');
            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            try
            {
                File.WriteAllBytes(temporary, buffer.ToArray());
                File.Move(temporary, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                { File.Delete(temporary); }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or JsonException or
            BadImageFormatException or InvalidOperationException)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    // The module version id and SHA-256 of one produced image.
    private static (string Role, string Mvid, string Sha256) Image(string role, string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        if (!pe.HasMetadata)
        { throw new BadImageFormatException("The produced assembly has no metadata.", path); }
        var reader = pe.GetMetadataReader();
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D");
        return (role, mvid, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
