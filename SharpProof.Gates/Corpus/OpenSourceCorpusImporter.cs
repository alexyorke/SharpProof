using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Gates.Corpus;

internal static class OpenSourceCorpusImporter
{
    internal const string SourceEnvironmentVariable =
        "SHARPPROOF_OSS_CORPUS_SOURCE";

    private const string SourceId = "aalhour-c-sharp-algorithms";
    private const string RepositoryUrl =
        "https://github.com/aalhour/C-Sharp-Algorithms";
    private const string LicenseRelativePath =
        "third-party/aalhour-C-Sharp-Algorithms-LICENSE.txt";
    private const int TargetMethodCount = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task ImportIfRequestedAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var upstreamRoot = Environment.GetEnvironmentVariable(
            SourceEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(upstreamRoot))
        {
            return;
        }

        await ImportAsync(
                repositoryRoot,
                upstreamRoot,
                cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    internal static async Task ImportAsync(
        string repositoryRoot,
        string upstreamRoot,
        CancellationToken cancellationToken)
    {
        var resolvedUpstreamRoot = Path.GetFullPath(upstreamRoot);
        var commit = await ReadGitAsync(
                resolvedUpstreamRoot,
                ["rev-parse", "HEAD"],
                cancellationToken)
            .ConfigureAwait(false);
        if (commit.Length != 40 ||
            commit.Any(static character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                $"Upstream HEAD is not a full Git commit: {commit}");
        }

        var status = await ReadGitAsync(
                resolvedUpstreamRoot,
                ["status", "--porcelain"],
                cancellationToken)
            .ConfigureAwait(false);
        if (status.Length != 0)
        {
            throw new InvalidDataException(
                "The OSS corpus importer requires a clean upstream checkout.");
        }

        var remote = await ReadGitAsync(
                resolvedUpstreamRoot,
                ["config", "--get", "remote.origin.url"],
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                NormalizeRepositoryUrl(remote),
                RepositoryUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The upstream checkout has unexpected origin '{remote}'; " +
                $"expected {RepositoryUrl}.");
        }

        var licenseSourcePath = Path.Combine(resolvedUpstreamRoot, "LICENSE");
        if (!File.Exists(licenseSourcePath))
        {
            throw new InvalidDataException(
                $"The upstream MIT license is missing: {licenseSourcePath}");
        }

        var licenseText = OpenSourceCorpusCatalog.NormalizeLineEndings(
            await File.ReadAllTextAsync(
                    licenseSourcePath,
                    cancellationToken)
                .ConfigureAwait(false));
        if (!licenseText.StartsWith(
                "The MIT License (MIT)",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The upstream license no longer has the reviewed MIT form.");
        }

        var (files, candidates) = DiscoverSources(resolvedUpstreamRoot);
        var selected = SelectDiverseCandidates(candidates, TargetMethodCount);

        var corpusDirectory =
            OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var existingSupport = LoadExistingSupport(corpusDirectory);
        var licenseTargetPath = Path.GetFullPath(
            Path.Combine(corpusDirectory, LicenseRelativePath));
        EnsureContained(corpusDirectory, licenseTargetPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(licenseTargetPath) ??
            throw new InvalidDataException(
                "Could not resolve the imported license directory."));
        await File.WriteAllTextAsync(
                licenseTargetPath,
                licenseText.EndsWith('\n') ? licenseText : licenseText + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
        var licenseHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    await File.ReadAllBytesAsync(
                            licenseTargetPath,
                            cancellationToken)
                        .ConfigureAwait(false)))
            .ToLowerInvariant();
        var source = new OpenSourceCorpusSource(
            SourceId,
            RepositoryUrl,
            commit,
            "MIT",
            LicenseRelativePath,
            licenseHash);
        var provisionalMethods = selected
            .Select((candidate, index) =>
                candidate.ToMethod(
                    $"OSS{index + 1:D4}",
                    CorpusVerdict.SilentUnknown,
                    existingSupport.TryGetValue(
                        candidate.DeclarationSha256,
                        out var support)
                        ? support
                        : CorpusSupport.Unspecified))
            .ToImmutableArray();
        var provisionalDocument = new OpenSourceCorpusDocument(
            2,
            [source],
            files,
            provisionalMethods);
        var observations = await OpenSourceCorpusRunner.ObserveAsync(
                provisionalDocument,
                cancellationToken)
            .ConfigureAwait(false);
        var verdicts = observations.ToImmutableDictionary(
            static observation => observation.CaseId,
            static observation => observation.Verdict,
            StringComparer.Ordinal);
        var methods = provisionalMethods
            .Select(method => method with
            {
                ExpectedVerdict = verdicts[$"{method.Id}.baseline"]
            })
            .ToImmutableArray();
        var document = provisionalDocument with
        {
            Methods = methods
        };

        var manifest = JsonSerializer.Serialize(document, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        await File.WriteAllTextAsync(
                manifestPath,
                manifest,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
        var unreviewedMethods = methods
            .Where(static method =>
                method.Support == CorpusSupport.Unspecified)
            .Select(static method => method.Id)
            .ToArray();
        if (unreviewedMethods.Length != 0)
        {
            throw new InvalidDataException(
                "The imported corpus contains methods without a reviewed " +
                "support classification. Set each generated support field " +
                $"before updating the snapshot: {string.Join(", ", unreviewedMethods)}");
        }
        _ = OpenSourceCorpusCatalog.Load(repositoryRoot);
    }

    private static ImmutableDictionary<string, CorpusSupport>
        LoadExistingSupport(string corpusDirectory)
    {
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        if (!File.Exists(manifestPath))
        {
            return ImmutableDictionary<string, CorpusSupport>.Empty;
        }

        var existing = JsonSerializer.Deserialize<OpenSourceCorpusDocument>(
            File.ReadAllText(manifestPath),
            JsonOptions) ?? throw new InvalidDataException(
            "The existing OSS corpus manifest is empty.");
        return existing.Methods.ToImmutableDictionary(
            static method => method.DeclarationSha256,
            static method => method.Support,
            StringComparer.Ordinal);
    }

    private static (
        ImmutableArray<OpenSourceCorpusFile> Files,
        ImmutableArray<ImportCandidate> Candidates)
        DiscoverSources(string upstreamRoot)
    {
        var sourceRoots = new[] {
            Path.Combine(upstreamRoot, "Algorithms"),
            Path.Combine(upstreamRoot, "DataStructures")
        };
        foreach (var sourceRoot in sourceRoots)
        {
            if (!Directory.Exists(sourceRoot))
            {
                throw new InvalidDataException(
                    $"Expected upstream source directory is missing: {sourceRoot}");
            }
        }

        var files = ImmutableArray.CreateBuilder<OpenSourceCorpusFile>();
        var candidates = ImmutableArray.CreateBuilder<ImportCandidate>();
        var declarationHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in sourceRoots
                     .SelectMany(static root =>
                         Directory.EnumerateFiles(
                             root,
                             "*.cs",
                             SearchOption.AllDirectories))
                     .Where(static path =>
                         !path.Contains(
                             $"{Path.DirectorySeparatorChar}bin" +
                             Path.DirectorySeparatorChar,
                             StringComparison.OrdinalIgnoreCase) &&
                         !path.Contains(
                             $"{Path.DirectorySeparatorChar}obj" +
                             Path.DirectorySeparatorChar,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var content = OpenSourceCorpusCatalog.NormalizeLineEndings(
                File.ReadAllText(path));
            var relativePath = Path.GetRelativePath(upstreamRoot, path)
                .Replace('\\', '/');
            files.Add(
                new OpenSourceCorpusFile(
                    SourceId,
                    relativePath,
                    OpenSourceCorpusCatalog.ComputeSha256(content),
                    content));
            var tree = CompilerConstructionBoundary.ParseCSharpText(
                content,
                AnalyzerGateHost.ParseOptions,
                relativePath);
            foreach (var method in tree.GetCompilationUnitRoot()
                         .DescendantNodes()
                         .OfType<MethodDeclarationSyntax>()
                         .OrderBy(static method => method.SpanStart))
            {
                if (!IsCandidate(method))
                {
                    continue;
                }

                var declaration =
                    OpenSourceCorpusCatalog.GetDeclaration(method);
                var hash =
                    OpenSourceCorpusCatalog.ComputeSha256(declaration);
                if (!declarationHashes.Add(hash))
                {
                    continue;
                }

                var lineSpan = tree.GetLineSpan(method.Span);
                candidates.Add(
                    new ImportCandidate(
                        relativePath,
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.EndLinePosition.Line + 1,
                        hash,
                        method.Identifier.ValueText));
            }
        }
        return (files.ToImmutable(), candidates.ToImmutable());
    }

    private static bool IsCandidate(MethodDeclarationSyntax method)
    {
        if (method.Body == null && method.ExpressionBody == null)
        {
            return false;
        }

        return !method.Modifiers.Any(static modifier =>
                   modifier.IsKind(SyntaxKind.UnsafeKeyword) ||
                   modifier.IsKind(SyntaxKind.ExternKeyword) ||
                   modifier.IsKind(SyntaxKind.AbstractKeyword) ||
                   modifier.IsKind(SyntaxKind.PartialKeyword)) &&
               !method.ContainsDirectives;
    }

    private static ImmutableArray<ImportCandidate> SelectDiverseCandidates(
        ImmutableArray<ImportCandidate> candidates,
        int count)
    {
        var byFile = candidates
            .GroupBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => group.ToImmutableArray())
            .ToImmutableArray();
        var selected = ImmutableArray.CreateBuilder<ImportCandidate>(count);
        for (var offset = 0;
             selected.Count < count &&
             byFile.Any(group => group.Length > offset);
             offset++)
        {
            foreach (var group in byFile)
            {
                if (group.Length <= offset)
                {
                    continue;
                }

                selected.Add(group[offset]);
                if (selected.Count == count)
                {
                    break;
                }
            }
        }
        if (selected.Count != count)
        {
            throw new InvalidDataException(
                $"Only {selected.Count} distinct upstream methods were found; " +
                $"{count} are required.");
        }

        var fileCount = selected
            .Select(static candidate => candidate.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (fileCount < OpenSourceCorpusCatalog.MinimumSourceFileCount)
        {
            throw new InvalidDataException(
                $"The selected corpus spans {fileCount} files; " +
                $"{OpenSourceCorpusCatalog.MinimumSourceFileCount} are required.");
        }

        return selected.ToImmutable();
    }

    private static async Task<string> ReadGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start Git.");
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException) { }
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
        }
        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed: {error}");
        }

        return output;
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        if (Path.IsPathRooted(relative) ||
            relative.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Any(static part => part == ".."))
        {
            throw new InvalidDataException(
                $"Generated OSS corpus path escaped its directory: {path}");
        }
    }

    private static string NormalizeRepositoryUrl(string value)
    {
        return value.Trim()
            .TrimEnd('/')
            .Replace(
                "git@github.com:",
                "https://github.com/",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                ".git",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ImportCandidate(
        string Path,
        int StartLine,
        int EndLine,
        string DeclarationSha256,
        string MethodName)
    {
        internal OpenSourceCorpusMethod ToMethod(
            string id,
            CorpusVerdict verdict,
            CorpusSupport support)
        {
            return new(
                id,
                SourceId,
                Path,
                StartLine,
                EndLine,
                DeclarationSha256,
                MethodName,
                "effects",
                verdict,
                support);
        }
    }
}
