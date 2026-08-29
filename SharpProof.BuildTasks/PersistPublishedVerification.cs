using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "MSBuild loads task types by their public names.")]
public sealed class PersistPublishedVerification : Microsoft.Build.Utilities.Task
{
    [Required]
    public string MetadataPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    [Required]
    public string RequestPath { get; set; } = string.Empty;

    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public string? SarifPath { get; set; }

    public override bool Execute()
    {
        try
        {
            ContainerContract.ValidateRequired();
            var projectDirectory = Path.GetFullPath(ProjectDirectory);
            var paths = Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                .Select(path => Path.GetFullPath(Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectDirectory, path)))
                .Select(LinuxPathIdentity.RequireLocalPath)
                .ToArray();
            PublicationTopologyStore.Write(MetadataPath, paths);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            VerifierBuildDiagnosticCodes.LogError(
                Log,
                VerifierBuildDiagnosticCodes.PublishedEvidence,
                "SharpProof could not persist the publication topology: {0}",
                exception.Message);
            return false;
        }
    }

    private static IEnumerable<string> Present(params string?[] paths)
    {
        return paths.Where(static path => !string.IsNullOrWhiteSpace(path))!;
    }
}
