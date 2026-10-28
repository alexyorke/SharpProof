using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SharpProof.Tool;

internal static class SetupDoctor
{
    private static readonly string[] ComposeNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];
    private static readonly SearchValues<char> DigestCharacters = SearchValues.Create("0123456789abcdef");

    internal delegate Task<(int ExitCode, string Output)> ProcessRunner(
        string command, string directory, CancellationToken cancellationToken, params string[] arguments);

    internal static Task<int> RunAsync(string projectPath, string? verification, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentNullException.ThrowIfNull(output);
        var contract = Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER_CONTRACT");
        var markerAvailable = OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER") == "1" && File.Exists(contract);
        return RunAsync(projectPath, verification, output, ToolProcess.RunAsync, markerAvailable, cancellationToken);
    }

    internal static async Task<int> RunAsync(string projectPath, string? verification, TextWriter output,
        ProcessRunner runProcess, bool containerMarkerAvailable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(runProcess);
        if (verification != null && verification is not ("portable" or "container"))
        {
            throw new ArgumentException("--verification must be portable or container.");
        }

        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--project must identify an existing C# project.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(projectPath)!;
        var failed = false;
        void Check(bool ok, string name, string remedy)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(remedy);
            output.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? string.Empty : ": " + remedy));
            failed |= !ok;
        }

        try
        {
            var evaluation = await runProcess("dotnet", directory, cancellationToken, "msbuild", projectPath, "-nologo",
                "-getProperty:NETCoreSdkVersion,SharpProofProfile,SharpProofFeatures,SharpProofVerify,SharpProofVerifyPolicy,SharpProofAssumptionPolicy,DefineConstants,ProjectAssetsFile,TargetFramework,TargetFrameworks",
                "-getItem:PackageReference").ConfigureAwait(false);
            if (evaluation.ExitCode != 0)
            {
                Check(false, "Project evaluation", "The project could not be evaluated. Check its SDK and imports; no restore/build was run.");
                return 1;
            }

            using var evaluated = JsonDocument.Parse(evaluation.Output);
            var properties = evaluated.RootElement.GetProperty("Properties");
            string Property(string name)
            {
                ArgumentException.ThrowIfNullOrEmpty(name);
                return properties.GetProperty(name).GetString() ?? string.Empty;
            }
            var container = verification == "container";
            var verify = Property("SharpProofVerify");
            var verifyEnabled = verify.Equals("true", StringComparison.OrdinalIgnoreCase);
            var profile = Property("SharpProofProfile").Trim();
            var profileOff = profile.Equals("off", StringComparison.OrdinalIgnoreCase);
            Check(profile.Equals("advisory", StringComparison.OrdinalIgnoreCase) ||
                profile.Equals("strict", StringComparison.OrdinalIgnoreCase) || profileOff,
                "Profile", "Use advisory, strict, or off; restore packages so their defaults are imported.");
            var features = Property("SharpProofFeatures").Trim();
            Check(features.Equals("effects", StringComparison.OrdinalIgnoreCase) || features.Equals("contracts", StringComparison.OrdinalIgnoreCase) ||
                features.Equals("all", StringComparison.OrdinalIgnoreCase), "Features", "Use effects, contracts, or all.");
            Check(verifyEnabled || verify.Equals("false", StringComparison.OrdinalIgnoreCase), "Verification setting", "Set SharpProofVerify to true or false.");
            Check(!profile.Equals("strict", StringComparison.OrdinalIgnoreCase) || verifyEnabled,
                "Strict verification", "Strict requires SharpProofVerify=true.");
            Check(!container || verifyEnabled && !profileOff, "Verification activation", "Enable strict verification or advisory verification before requesting container proof.");
            var minimumSdk = verifyEnabled && !profileOff || container ? new Version(10, 0, 100) : new Version(9, 0, 300);
            Check(profileOff && !container || Version.TryParse(Property("NETCoreSdkVersion").Split('-')[0], out var version) && version >= minimumSdk,
                "Compiler SDK", "Use .NET SDK " + minimumSdk + " or newer for the evaluated configuration; check global.json.");
            if (verifyEnabled && !profileOff)
            {
                var policy = Property("SharpProofVerifyPolicy").Trim();
                Check(policy.Equals("advisory", StringComparison.OrdinalIgnoreCase) || policy.Equals("warn-on-unknown", StringComparison.OrdinalIgnoreCase) ||
                    policy.Equals("require-proven", StringComparison.OrdinalIgnoreCase), "Verification policy", "Use advisory, warn-on-unknown, or require-proven.");
                var assumptionPolicy = Property("SharpProofAssumptionPolicy").Trim();
                Check(assumptionPolicy.Equals("allow", StringComparison.OrdinalIgnoreCase) || assumptionPolicy.Equals("warn", StringComparison.OrdinalIgnoreCase) ||
                    assumptionPolicy.Equals("error", StringComparison.OrdinalIgnoreCase), "Assumption policy", "Use allow, warn, or error.");
            }
            Check(!Property("DefineConstants").Split([';', ',']).Any(static value =>
            {
                global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
                return value.Trim().Equals("SHARPPROOF_CONTRACTS", StringComparison.Ordinal);
            }), "Static contract configuration", "Remove SHARPPROOF_CONTRACTS; contracts do not provide runtime enforcement.");
            var references = evaluated.RootElement.GetProperty("Items").GetProperty("PackageReference").EnumerateArray().Select(static item =>
            {
                global::SharpProof.Internal.InputValidation.Require(item, nameof(item));
                return item.GetProperty("Identity").GetString();
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check(references.Contains("SharpProof") || references.Contains("SharpProof.Verifier"), "SharpProof package",
                "Reference SharpProof; matching Verifier and Attributes dependencies are restored transitively.");

            string? resolvedVersion = null;
            var assetsPath = Property("ProjectAssetsFile");
            if (!Path.IsPathRooted(assetsPath))
            {
                assetsPath = Path.Combine(directory, assetsPath);
            }

            if (!File.Exists(assetsPath))
            {
                Check(false, "Restored package evidence", "Run dotnet restore using your configured feed, then run doctor again.");
            }
            else
            {
                using var assets = JsonDocument.Parse(EvidenceSet.ReadBounded(assetsPath, 32 * 1024 * 1024, cancellationToken));
                var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var packageVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
                {
                    var parts = library.Name.Split('/');
                    if (parts.Length == 2 && (parts[0].Equals("SharpProof", StringComparison.OrdinalIgnoreCase) ||
                        parts[0].Equals("SharpProof.Attributes", StringComparison.OrdinalIgnoreCase) ||
                        parts[0].Equals("SharpProof.Verifier", StringComparison.OrdinalIgnoreCase)) &&
                        library.Value.GetProperty("type").GetString() == "package")
                    {
                        packageIds.Add(parts[0]);
                        packageVersions.Add(parts[1]);
                    }
                }
                var compatible = packageIds.Contains("SharpProof.Attributes") && packageIds.Contains("SharpProof.Verifier") &&
                    (!references.Contains("SharpProof") || packageIds.Contains("SharpProof")) && packageVersions.Count == 1 &&
                    !packageVersions.Contains(string.Empty);
                Check(compatible, "Resolved package compatibility", "Restore matching SharpProof, Attributes, and Verifier versions.");
                if (compatible)
                { resolvedVersion = packageVersions.Single(); }
                await output.WriteLineAsync(("WARN Existing restore assets do not establish freshness; restore again after dependency changes.").AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            if (Property("TargetFrameworks").Length > 0)
            {
                await output.WriteLineAsync(("WARN Multitarget project: repeat qualification for every target framework.").AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            if (container)
            {
                if (containerMarkerAvailable)
                {
                    await output.WriteLineAsync(("WARN Container marker available; its contents and native payload must be validated during build.").AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var docker = await runProcess("docker", directory, cancellationToken, "info", "--format", "{{.OSType}}").ConfigureAwait(false);
                    Check(docker.ExitCode == 0 && docker.Output.Trim() == "linux", "Docker Linux engine", "Start Docker with Linux containers; the configured image must support linux/amd64.");
                    var compose = await runProcess("docker", FindComposeDirectory(directory, cancellationToken), cancellationToken, "compose", "config", "--format", "json").ConfigureAwait(false);
                    if (compose.ExitCode != 0)
                    {
                        Check(false, "Consumer container configuration", "Generate/use the container starter's compose.yaml.");
                    }
                    else
                    {
                        using var config = JsonDocument.Parse(compose.Output);
                        var service = config.RootElement.GetProperty("services").GetProperty("verify");
                        var image = service.GetProperty("image").GetString() ?? string.Empty;
                        var pinned = IsPinnedConsumerImage(image) && service.GetProperty("platform").GetString() == "linux/amd64";
                        Check(pinned, "Pinned canonical image", "Use a digest-pinned linux/amd64 consumer image from the canonical repository.");
                        if (pinned)
                        {
                            var inspect = await runProcess("docker", directory, cancellationToken, "image", "inspect", image).ConfigureAwait(false);
                            Check(inspect.ExitCode == 0, "Local consumer image", "Run docker compose pull verify, then run doctor again. Doctor does not pull images.");
                            if (inspect.ExitCode == 0)
                            {
                                using var inspected = JsonDocument.Parse(inspect.Output);
                                var imageInfo = inspected.RootElement[0];
                                var labels = imageInfo.GetProperty("Config").GetProperty("Labels");
                                Check(resolvedVersion != null && imageInfo.GetProperty("Os").GetString() == "linux" &&
                                    imageInfo.GetProperty("Architecture").GetString() == "amd64" && labels.ValueKind == JsonValueKind.Object &&
                                    labels.TryGetProperty("io.sharpproof.consumer.version", out var versionLabel) && versionLabel.GetString() == resolvedVersion,
                                    "Image/package compatibility", "Use the canonical linux/amd64 consumer image for the resolved package version.");
                            }
                        }
                    }
                }
            }
            else if (verifyEnabled && !profileOff)
            {
                await output.WriteLineAsync(("WARN Native host and payload compatibility require validation during build; use --verification container to inspect a consumer image.").AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await output.WriteLineAsync(("Setup inspection did not restore packages, build the project, or execute application code.").AsMemory(), cancellationToken).ConfigureAwait(false);
            return failed ? 1 : 0;
        }
        catch (Exception exception) when (exception is Win32Exception or JsonException or KeyNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            await output.WriteLineAsync(("FAIL Setup inspection: required executable or evaluated metadata is unavailable. Check dotnet, Docker, and project configuration.").AsMemory(), cancellationToken).ConfigureAwait(false);
            return 1;
        }
    }

    internal static string FindComposeDirectory(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        directory = Path.GetFullPath(directory);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? candidate = null;
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.FullName != directory && string.Equals(current.FullName, home, StringComparison.OrdinalIgnoreCase))
            { return directory; }
            var composeFiles = ComposeNames.Count(name =>
            {
                ArgumentNullException.ThrowIfNull(name);
                return File.Exists(Path.Combine(current.FullName, name));
            });
            if (composeFiles > 1)
            { throw new InvalidOperationException("Multiple compose files make workspace inspection ambiguous."); }
            if (composeFiles == 1)
            {
                candidate ??= current.FullName;
                if (current.FullName == directory)
                { return directory; }
            }
            var git = Path.Combine(current.FullName, ".git");
            var workspace = File.Exists(Path.Combine(current.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(current.FullName, ".config", "dotnet-tools.json"));
            if (File.Exists(git) || Directory.Exists(git) || workspace ||
                Directory.EnumerateFiles(current.FullName, "*.sln").Any() || Directory.EnumerateFiles(current.FullName, "*.slnx").Any())
            { return candidate ?? directory; }
        }
        return directory;
    }
    internal static bool IsPinnedConsumerImage(string image)
    {
        ArgumentNullException.ThrowIfNull(image);
        const string marker = "@sha256:";
        var separator = image.IndexOf(marker, StringComparison.Ordinal);
        return separator >= 0 && image[..separator] is "ghcr.io/alexyorke/sharpproof" or "ghcr.io/alexyorke/sharpproof-preview" &&
            image.Length == separator + marker.Length + 64 && image.AsSpan(separator + marker.Length).IndexOfAnyExcept(DigestCharacters) < 0;
    }
}
