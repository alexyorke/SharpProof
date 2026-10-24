using System.Reflection;
using System.Text;

namespace SharpProof.Tool;

internal static class SetupInitializer
{
    internal static async Task<int> RunAsync(string projectPath, string verification, bool apply, string? consumerImage,
        TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(verification);
        ArgumentNullException.ThrowIfNull(output);
        var result = await InitializeAsync(projectPath, verification, apply, consumerImage, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(result.Message.AsMemory(), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<SetupInitializationResult> InitializeAsync(string projectPath, string verification, bool apply,
        string? consumerImage, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(verification);
        cancellationToken.ThrowIfCancellationRequested();
        if (verification is not ("portable" or "container"))
        { throw new ArgumentException("--verification must be portable or container."); }
        if (verification == "portable" && consumerImage != null)
        { throw new ArgumentException("--consumer-image applies only to container initialization."); }
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        { throw new ArgumentException("--project must identify an existing C# project."); }
        var directory = Path.GetDirectoryName(projectPath)!;
        var plan = new SetupInitializerFiles(cancellationToken);
        var version = PackageVersion;
        SetupInitializerProject.Plan(plan, projectPath, verification, version);
        SetupInitializerConfiguration.Plan(plan, directory, version);
        if (verification == "container")
        { SetupInitializerContainer.Plan(plan, directory, consumerImage); }
        if (apply)
        { await plan.ApplyAsync(cancellationToken).ConfigureAwait(false); }
        var files = plan.Describe(apply);
        var message = new StringBuilder();
        foreach (var file in files)
        {
            var status = !file.Changed ? "UNCHANGED" : apply ? "APPLIED" : "PREVIEW";
            message.Append(status).Append(' ').AppendLine(Path.GetRelativePath(directory, file.Path));
        }
        message.AppendLine(apply ? "Setup files applied; no packages were restored and no build or project code was executed." :
            "Preview only; use --apply to write these changes. No packages were restored and no build or project code was executed.");
        message.AppendLine("Configure private-feed authentication through your normal NuGet credential mechanism; generated configuration contains no credentials.");
        return new SetupInitializationResult(files, message.ToString());
    }

    internal static bool IsPinnedConsumerImage(string image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return SetupDoctor.IsPinnedConsumerImage(image);
    }

    internal static string PackageVersion
    {
        get
        {
            var value = typeof(SetupInitializer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ResolvePackageVersion(value);
        }
    }
    internal static string ResolvePackageVersion(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        { throw new InvalidOperationException("The installed SharpProof tool has no usable package version."); }
        var parts = informationalVersion.Split('+');
        var version = parts[0];
        var prerelease = version.IndexOf('-', StringComparison.Ordinal);
        var core = prerelease < 0 ? version : version[..prerelease];
        if (!Version.TryParse(core, out _) || version.Any(static character =>
            !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-')) ||
            prerelease >= 0 && !ValidIdentifiers(version[(prerelease + 1)..]) ||
            parts.Length > 2 || parts.Length == 2 && !ValidIdentifiers(parts[1]))
        { throw new InvalidOperationException("The installed SharpProof tool has no usable package version."); }
        return version;
    }

    private static bool ValidIdentifiers(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0 && value.Split('.').All(static identifier =>
        {
            ArgumentNullException.ThrowIfNull(identifier);
            return identifier.Length > 0 && identifier.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-');
        });
    }

}

internal sealed class SetupInitializationFile
{
    internal SetupInitializationFile(string path, bool changed, bool written)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        Changed = changed;
        Written = written;
    }

    internal string Path { get; }
    internal bool Changed { get; }
    internal bool Written { get; }
}

internal sealed class SetupInitializationResult
{
    internal SetupInitializationResult(IReadOnlyList<SetupInitializationFile> files, string message)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(message);
        Files = files;
        Message = message;
    }

    internal IReadOnlyList<SetupInitializationFile> Files { get; }
    internal string Message { get; }

}
