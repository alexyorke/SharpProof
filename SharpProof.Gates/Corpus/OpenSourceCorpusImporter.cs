using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Ir;

namespace SharpProof.Gates.Corpus;

internal sealed record OpenSourceCorpusImportPlan(
    OpenSourceCorpusDocument Document,
    CorpusFileUpdate[] Updates);

internal static class OpenSourceCorpusImporter
{
    internal const string SourceEnvironmentVariable =
        "SHARPPROOF_OSS_CORPUS_SOURCE";

    private const string SourceId = "aalhour-c-sharp-algorithms";
    private const string RepositoryUrl =
        "https://github.com/aalhour/C-Sharp-Algorithms";
    private const string LicenseRelativePath =
        "third-party/aalhour-C-Sharp-Algorithms-LICENSE.txt";
    private const int TargetMethodCount =
        OpenSourceCorpusCatalog.MinimumMethodCount;
    private const string ReviewedMitLicense = """
The MIT License (MIT)

Copyright (c) 2015 Ahmad Alhour

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task<OpenSourceCorpusImportPlan?>
        PrepareIfRequestedAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var upstreamRoot = Environment.GetEnvironmentVariable(
            SourceEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(upstreamRoot))
        {
            return null;
        }

        return await PrepareAsync(
                repositoryRoot,
                upstreamRoot,
                cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    internal static async Task<OpenSourceCorpusImportPlan> PrepareAsync(
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

        await VerifyCommitBelongsToApprovedRemoteAsync(
                commit,
                cancellationToken)
            .ConfigureAwait(false);

        var licenseText = OpenSourceCorpusCatalog.NormalizeLineEndings(
            Encoding.UTF8.GetString(
                await ReadGitBlobAsync(
                        resolvedUpstreamRoot,
                        commit,
                        "LICENSE",
                        cancellationToken)
                    .ConfigureAwait(false)));
        ValidateReviewedMitLicense(licenseText);

        var (files, candidates) = await DiscoverSourcesAsync(
                resolvedUpstreamRoot,
                commit,
                cancellationToken)
            .ConfigureAwait(false);
        var selected = SelectDiverseCandidates(candidates, TargetMethodCount);

        var corpusDirectory =
            OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var existingSupport = LoadExistingSupport(corpusDirectory);
        var licenseTargetPath = Path.GetFullPath(
            Path.Combine(corpusDirectory, LicenseRelativePath));
        OpenSourceCorpusCatalog.EnsureContained(
            corpusDirectory,
            licenseTargetPath);
        var licenseContent = licenseText.EndsWith('\n')
            ? licenseText
            : licenseText + "\n";
        var licenseHash = HashEncoding.ComputeSha256Hex(
            Encoding.UTF8.GetBytes(licenseContent));
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
        return new OpenSourceCorpusImportPlan(
            document,
            [
                new CorpusFileUpdate(licenseTargetPath, licenseContent),
                new CorpusFileUpdate(manifestPath, manifest)
            ]);
    }

    internal static void ValidateReviewedMitLicense(string licenseText)
    {
        if (!string.Equals(
                OpenSourceCorpusCatalog.NormalizeLineEndings(licenseText).TrimEnd('\n'),
                ReviewedMitLicense.TrimEnd('\n'),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The upstream license no longer has the reviewed MIT form.");
        }
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

    private static async Task<(
        ImmutableArray<OpenSourceCorpusFile> Files,
        ImmutableArray<ImportCandidate> Candidates)> DiscoverSourcesAsync(
        string upstreamRoot,
        string commit,
        CancellationToken cancellationToken)
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
        var trackedPaths = await ReadGitAsync(
                upstreamRoot,
                ["ls-tree", "-r", "--name-only", commit, "--", "Algorithms", "DataStructures"],
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var relativePath in trackedPaths
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var content = OpenSourceCorpusCatalog.NormalizeLineEndings(
                Encoding.UTF8.GetString(
                    await ReadGitBlobAsync(
                            upstreamRoot,
                            commit,
                            relativePath,
                            cancellationToken)
                        .ConfigureAwait(false)));
            files.Add(
                new OpenSourceCorpusFile(
                    SourceId,
                    relativePath,
                    OpenSourceCorpusCatalog.ComputeNormalizedSha256(content),
                    content));
            var tree = CSharpSyntaxTree.ParseText(
                content,
                AnalyzerGateHost.ParseOptions,
                relativePath,
                cancellationToken: cancellationToken);
            foreach (var method in tree.GetCompilationUnitRoot(cancellationToken)
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
                    OpenSourceCorpusCatalog.ComputeNormalizedSha256(declaration);
                if (!declarationHashes.Add(hash))
                {
                    continue;
                }

                var lineSpan = tree.GetLineSpan(method.Span, cancellationToken);
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
        for (var offset = 0; selected.Count < count; offset++)
        {
            var addedThisRound = false;
            foreach (var group in byFile)
            {
                if (group.Length <= offset)
                {
                    continue;
                }

                selected.Add(group[offset]);
                addedThisRound = true;
                if (selected.Count == count)
                {
                    break;
                }
            }
            if (!addedThisRound)
            {
                break;
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

    internal static async Task<string> ReadGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string gitExecutable = "git")
    {
        var startInfo = GateProcess.CreateCaptured(
            gitExecutable,
            workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await GateProcess.RunCapturedAsync(
                startInfo,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed: {result.Error.Trim()}");
        }

        return result.Output.Trim();
    }

    private static async Task<byte[]> ReadGitBlobAsync(
        string workingDirectory,
        string commit,
        string relativePath,
        CancellationToken cancellationToken,
        string gitExecutable = "git")
    {
        var startInfo = GateProcess.CreateCaptured(
            gitExecutable,
            workingDirectory);
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("blob");
        startInfo.ArgumentList.Add($"{commit}:{relativePath}");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start Git.");
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(
            output,
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            GateProcess.KillTree(process);

            await process.WaitForExitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        await outputTask.ConfigureAwait(false);
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git cat-file blob {commit}:{relativePath} failed: {error}");
        }

        return output.ToArray();
    }

    internal static string NormalizeRepositoryUrl(string value)
    {
        var normalized = value.Trim()
            .TrimEnd('/')
            .Replace(
                "git@github.com:",
                "https://github.com/",
                StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        return normalized.EndsWith(
                ".git",
                StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static async Task VerifyCommitBelongsToApprovedRemoteAsync(
        string commit,
        CancellationToken cancellationToken)
    {
        var verificationRoot = Directory.CreateTempSubdirectory(
            "SharpProof-OSS-origin-");
        try
        {
            await ReadGitAsync(
                    verificationRoot.FullName,
                    ["init", "--bare", "--quiet"],
                    cancellationToken)
                .ConfigureAwait(false);
            await ReadGitAsync(
                    verificationRoot.FullName,
                    [
                        "fetch",
                        "--no-tags",
                        "--quiet",
                        RepositoryUrl,
                        "+refs/heads/*:refs/sharpproof-approved/*"
                    ],
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await ReadGitAsync(
                        verificationRoot.FullName,
                        ["cat-file", "-e", $"{commit}^{{commit}}"],
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    $"Upstream HEAD {commit} is not a commit from the " +
                    $"approved repository {RepositoryUrl}.",
                    exception);
            }
        }
        finally
        {
            verificationRoot.Delete(recursive: true);
        }
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
