using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record LibraryBuildReport(
    string SourceId,
    int Methods,
    int BuildExitCode,
    bool TimedOut,
    double BuildSeconds,
    long? ManifestBytes,
    long ManifestLimitBytes,
    string? RunStatus,
    ImmutableSortedDictionary<string, int> Outcomes,
    ImmutableSortedDictionary<string, int> UnknownReasons,
    ImmutableSortedDictionary<string, int> Diagnostics,
    bool Crashed)
{
    public string SourceId { get; init; } = SourceId ?? throw new ArgumentNullException(nameof(SourceId));
    public ImmutableSortedDictionary<string, int> Outcomes { get; init; } = Outcomes ?? throw new ArgumentNullException(nameof(Outcomes));
    public ImmutableSortedDictionary<string, int> UnknownReasons { get; init; } = UnknownReasons ?? throw new ArgumentNullException(nameof(UnknownReasons));
    public ImmutableSortedDictionary<string, int> Diagnostics { get; init; } = Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics));
}

// Builds one pinned source as a user would: its whole pinned tree, with
// [EnforcePure] on every method the importer could select, as a project that
// references the SharpProof package from a local feed, verified in advisory
// mode. The report records what such a build costs and finds. It is
// advisory: only a build that crashes, or runs out of time, fails it.
internal static partial class LibraryBuild
{
    internal const long ManifestLimitBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(45);

    [GeneratedRegex(@"\b(?<severity>warning|error) (?<id>SP\d{4})\b(?<message>[^\r\n]*)")]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"\berror (?<id>[A-Z]+\d{4})\b(?<message>[^\r\n]*)")]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"^SharpProof\.(?<version>\d[^/\\]*)\.nupkg$")]
    private static partial Regex PackagePattern();

    internal static async Task<LibraryBuildReport> RunAsync(string root, string sourceId, string feed, string output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(output);
        root = Path.GetFullPath(root);
        feed = Path.GetFullPath(feed);
        output = Path.GetFullPath(output);
        var version = Directory.EnumerateFiles(feed, "SharpProof.*.nupkg")
            .Select(path => PackagePattern().Match(Path.GetFileName(path)))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["version"].Value)
            .SingleOrDefault() ?? throw new InvalidDataException("The feed has no single SharpProof package: " + feed);
        Directory.CreateDirectory(output);
        // Restored packages and build outputs stay in a private workspace;
        // only reports and the worker result are exported.
        var work = Path.Combine(root, "artifacts", "tmp", "library-" + sourceId);
        if (Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }
        var result = Path.Combine(output, "result.json");
        var manifest = Path.Combine(work, "compiler-manifest.json");
        // A run that stops before verification must not report an earlier run's result.
        File.Delete(result);
        var project = Path.Combine(work, "project");
        Directory.CreateDirectory(project);
        // Stand alone like a user's project: do not inherit the repository's
        // build or central package configuration.
        foreach (var name in (string[])["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"])
        {
            await File.WriteAllTextAsync(Path.Combine(work, name), name == "Directory.Packages.props"
                ? "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>\n"
                : "<Project />\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }
        var methods = WriteProject(root, sourceId, project, version, result, manifest, cancellationToken);
        var config = WriteNuGetConfig(work, feed);
        var packages = Path.Combine(work, "packages");
        var projectFile = Path.Combine(project, "Library.csproj");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var log = new StringBuilder();
        var exitCode = -1;
        var timedOut = false;
        var wall = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            exitCode = await DotNetAsync(work, log, timeout.Token, "restore", projectFile, "--configfile", config,
                "--packages", packages, "--nologo").ConfigureAwait(false);
            wall.Restart();
            if (exitCode == 0)
            {
                exitCode = await DotNetAsync(work, log, timeout.Token, "build", projectFile, "--configuration", "Release",
                    "--no-restore", "--nologo", "-p:UseSharedCompilation=false").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }
        var seconds = wall.Elapsed.TotalSeconds;
        await File.WriteAllTextAsync(Path.Combine(output, "build.log"), log.ToString(), cancellationToken).ConfigureAwait(false);

        var response = File.Exists(result) ? WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(result, cancellationToken).ConfigureAwait(false)) : null;
        var diagnostics = DiagnosticPattern().Matches(log.ToString())
            .Select(static match => match.Groups["severity"].Value + " " + match.Groups["id"].Value)
            .GroupBy(static key => key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var results = response?.ClaimResults ?? [];
        // A build that fails only on refuted claims (SP0051) or on the
        // compiler manifest size limit (SP0049 with that message) is a
        // finding. Any other error, including an SP0049 for an unexpected
        // collector exception, is a crash.

        var report = new LibraryBuildReport(
            sourceId,
            methods,
            exitCode,
            timedOut,
            seconds,
            File.Exists(manifest) ? new FileInfo(manifest).Length : null,
            ManifestLimitBytes,
            response?.RunStatus.ToString(),
            results.GroupBy(static claim => claim.Outcome.ToString(), StringComparer.Ordinal)
                .ToImmutableSortedDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            results.Where(static claim => claim.Outcome == WorkerClaimOutcome.Unknown)
                .GroupBy(static claim => claim.Reason.ToString(), StringComparer.Ordinal)
                .ToImmutableSortedDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            diagnostics,
            IsCrash(exitCode, timedOut, log.ToString()));
        await File.WriteAllTextAsync(Path.Combine(output, "library-" + sourceId + ".json"),
            JsonSerializer.Serialize(report, SharpProofJsonDefaults.Indented), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, "library-" + sourceId + ".md"), Summarize(report), cancellationToken)
            .ConfigureAwait(false);
        return report;
    }

    internal static bool IsCrash(int exitCode, bool timedOut, string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (timedOut)
        {
            return true;
        }
        if (exitCode == 0)
        {
            return false;
        }
        var errors = ErrorPattern().Matches(log).ToImmutableArray();
        return errors.IsEmpty || !errors.All(static error => error.Groups["id"].Value == "SP0051" ||
            error.Groups["id"].Value == "SP0049" && error.Groups["message"].Value.Contains("byte limit", StringComparison.Ordinal));
    }

    private static int WriteProject(string root, string sourceId, string project, string version, string result, string manifest,
        CancellationToken cancellationToken)
    {
        var document = OpenSourceCorpusCatalog.Load(root);
        var whole = NativeEffectOracleGate.ExpandToWholeSource(document, sourceId, cancellationToken);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(whole, cancellationToken, purity: true);
        var ordinal = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = tree.FilePath.Replace('\\', '/');
            relative = relative.StartsWith(sourceId + "/", StringComparison.Ordinal)
                ? relative[(sourceId.Length + 1)..]
                : "fixture/" + (ordinal++).ToString(CultureInfo.InvariantCulture) + "-" + Path.GetFileName(relative);
            var path = Path.GetFullPath(Path.Combine(project, relative));
            if (!path.StartsWith(Path.GetFullPath(project) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A pinned source path leaves the project: " + tree.FilePath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, tree.GetText(cancellationToken).ToString(), new UTF8Encoding(false));
        }
        var options = OpenSourceCorpusCatalog.GetCompilationOptions(document, sourceId);
        static string Escape(string value)
        { return System.Security.SecurityElement.Escape(value)!; }
        var text = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>SharpProofLibrary.{Escape(sourceId)}</AssemblyName>
                <Nullable>{(options.Nullable ? "enable" : "disable")}</Nullable>
                <CheckForOverflowUnderflow>{(options.CheckOverflow ? "true" : "false")}</CheckForOverflowUnderflow>
                <ImplicitUsings>disable</ImplicitUsings>
                <DisableImplicitFrameworkDefines>true</DisableImplicitFrameworkDefines>
                <DefineConstants>{Escape(string.Join(";", options.PreprocessorSymbols))}</DefineConstants>
                <LangVersion>latest</LangVersion>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>
                <SharpProofProfile>advisory</SharpProofProfile>
                <SharpProofVerify>true</SharpProofVerify>
                <SharpProofVerifyCacheEnabled>false</SharpProofVerifyCacheEnabled>
                <SharpProofVerifyResultFile>{Escape(result)}</SharpProofVerifyResultFile>
                <SharpProofCompilerManifestFile>{Escape(manifest)}</SharpProofCompilerManifestFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="SharpProof" Version="{Escape(version)}" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(project, "Library.csproj"), text, new UTF8Encoding(false));
        return whole.Methods.Length;
    }

    private static string WriteNuGetConfig(string work, string feed)
    {
        var path = Path.Combine(work, "NuGet.config");
        var text = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="SharpProofLocal" value="{System.Security.SecurityElement.Escape(Path.GetFullPath(feed))}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="SharpProofLocal"><package pattern="SharpProof*" /></packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="NETStandard.*" />
                  <package pattern="runtime.*" />
                  <package pattern="System.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static async Task<int> DotNetAsync(string workingDirectory, StringBuilder log, CancellationToken cancellationToken,
        params string[] arguments)
    {
        var start = GateProcess.CreateCaptured("dotnet", workingDirectory);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var result = await GateProcess.RunCapturedAsync(start, cancellationToken).ConfigureAwait(false);
        log.Append("$ dotnet ").AppendJoin(' ', arguments).Append('\n').Append(result.Output).Append(result.Error).Append('\n');
        return result.ExitCode;
    }

    internal static string Summarize(LibraryBuildReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append("## Whole-library build: `").Append(report.SourceId).Append("`\n\n");
        text.Append("Advisory. Every candidate method carries `[EnforcePure]`; the project references the packed `SharpProof` package.\n\n");
        text.Append("| Measure | Value |\n| --- | --- |\n");
        Row("Methods annotated", report.Methods.ToString(CultureInfo.InvariantCulture));
        Row("Build exit code", report.BuildExitCode.ToString(CultureInfo.InvariantCulture) + (report.TimedOut ? " (timed out)" : ""));
        Row("Build wall time", report.BuildSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
        Row("Compiler manifest", report.ManifestBytes is { } bytes
            ? bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes (" +
                (100.0 * bytes / report.ManifestLimitBytes).ToString("F0", CultureInfo.InvariantCulture) + "% of the limit)"
            : "not written");
        Row("Worker run", report.RunStatus ?? "no result");
        Row("Claims", string.Join(", ", report.Outcomes.Select(static pair => pair.Key + " " + pair.Value.ToString(CultureInfo.InvariantCulture))));
        Row("Unknown reasons", string.Join(", ", report.UnknownReasons.OrderByDescending(static pair => pair.Value)
            .Select(static pair => pair.Key + " " + pair.Value.ToString(CultureInfo.InvariantCulture))));
        Row("SharpProof diagnostics", string.Join(", ", report.Diagnostics.Select(static pair => pair.Key + " " + pair.Value.ToString(CultureInfo.InvariantCulture))));
        Row("Verdict", report.Crashed ? "the build crashed or timed out" : "completed");
        return text.ToString();

        void Row(string name, string value)
        { text.Append("| ").Append(name).Append(" | ").Append(value.Length == 0 ? "none" : value.Replace("|", "\\|", StringComparison.Ordinal)).Append(" |\n"); }
    }
}
