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
    ImmutableArray<CorpusFileUpdate> Updates)
{
    public OpenSourceCorpusDocument Document { get; init; } = Document ?? throw new System.ArgumentNullException(nameof(Document));
    public ImmutableArray<CorpusFileUpdate> Updates { get; init; } = Updates.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Updates)) : Updates;
}

// Imports one reviewed upstream source from Corpus/sources.json into the
// checked-in manifest. Other sources, and the IDs of methods that stay
// selected, are preserved; new methods are appended.
internal static class OpenSourceCorpusImporter
{
    internal const string SourceEnvironmentVariable =
        "SHARPPROOF_OSS_CORPUS_SOURCE";
    internal const string DraftEnvironmentVariable =
        "SHARPPROOF_OSS_CORPUS_DRAFT";
    internal const int SourcesSchemaVersion = 1;
    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task<OpenSourceCorpusImportPlan?>
        PrepareIfRequestedAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        var upstreamRoot = Environment.GetEnvironmentVariable(
            SourceEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(upstreamRoot))
        {
            return null;
        }

        var draft = Environment.GetEnvironmentVariable(DraftEnvironmentVariable);
        return await PrepareAsync(
                repositoryRoot,
                upstreamRoot,
                cancellationToken,
                draft: string.Equals(draft, "1", StringComparison.Ordinal) ||
                    string.Equals(draft, "true", StringComparison.OrdinalIgnoreCase))
            .ConfigureAwait(false);
    }

    // A draft import writes newly selected methods with Unspecified support
    // and their observed verdicts so a reviewer can label each one; the
    // catalog rejects the manifest until every label is set.
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    internal static async Task<OpenSourceCorpusImportPlan> PrepareAsync(
        string repositoryRoot,
        string upstreamRoot,
        CancellationToken cancellationToken,
        bool draft = false)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        System.ArgumentNullException.ThrowIfNull(upstreamRoot);
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
        var configuration = LoadSources(repositoryRoot);
        var source = configuration.Sources.FirstOrDefault(candidate => string.Equals(
                NormalizeRepositoryUrl(remote),
                candidate.Repository,
                StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException(
                $"The upstream checkout has unexpected origin '{remote}'; " +
                "expected one of the repositories in sources.json: " +
                string.Join(", ", configuration.Sources.Select(static candidate => candidate.Repository)) + ".");
        if (!string.Equals(commit, source.Commit, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Upstream HEAD {commit} is not the commit pinned for " +
                $"{source.Id} in sources.json ({source.Commit}).");
        }

        await VerifyCommitBelongsToApprovedRemoteAsync(
                source.Repository,
                commit,
                cancellationToken)
            .ConfigureAwait(false);

        var corpusDirectory =
            OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var updates = ImmutableArray.CreateBuilder<CorpusFileUpdate>();
        var license = await CopyReviewedFileAsync(
                resolvedUpstreamRoot, corpusDirectory, source, source.License, updates, cancellationToken)
            .ConfigureAwait(false);
        var notices = ImmutableArray.CreateBuilder<OpenSourceCorpusNoticeFile>();
        foreach (var notice in source.Notices)
        {
            notices.Add(await CopyReviewedFileAsync(
                    resolvedUpstreamRoot, corpusDirectory, source, notice, updates, cancellationToken)
                .ConfigureAwait(false));
        }

        var (files, candidates) = await DiscoverSourcesAsync(
                resolvedUpstreamRoot,
                source,
                cancellationToken)
            .ConfigureAwait(false);
        var existing = LoadExisting(corpusDirectory);
        var manifestSource = new OpenSourceCorpusSource(
            source.Id,
            source.Repository,
            commit,
            source.LicenseSpdx,
            license.File,
            license.Sha256)
        {
            NoticeFiles = notices.ToImmutable(),
            Compilation = source.Compilation
        };
        var merged = Merge(existing, manifestSource, files, candidates, source.MethodQuota);

        // Observe only the imported source; other sources keep their
        // reviewed expectations untouched.
        var imported = merged.Methods
            .Where(method => string.Equals(method.SourceId, source.Id, StringComparison.Ordinal))
            .ToImmutableArray();
        var observations = await OpenSourceCorpusRunner.ObserveAsync(
                new OpenSourceCorpusDocument(
                    OpenSourceCorpusCatalog.SchemaVersion,
                    [manifestSource],
                    files,
                    imported),
                cancellationToken)
            .ConfigureAwait(false);
        var verdicts = observations.ToImmutableDictionary(
            static observation => observation.CaseId,
            static observation => observation.Verdict,
            StringComparer.Ordinal);
        var document = merged with
        {
            Methods = [.. merged.Methods.Select(method =>
                !string.Equals(method.SourceId, source.Id, StringComparison.Ordinal)
                    ? method
                    : method with
                    {
                        ExpectedVerdict = verdicts.TryGetValue($"{method.Id}.baseline", out var verdict)
                            ? verdict
                            : throw new InvalidDataException(
                                $"No baseline observation was produced for {method.Id}.")
                    })]
        };

        var unreviewedMethods = imported
            .Where(static method =>
                method.Support == CorpusSupport.Unspecified)
            .Select(static method => method.Id)
            .ToImmutableArray();
        if (!draft && unreviewedMethods.Length != 0)
        {
            throw new InvalidDataException(
                "The imported corpus contains methods without a reviewed " +
                "support classification. Run a draft import, set each generated " +
                $"support field, and import again: {string.Join(", ", unreviewedMethods)}");
        }

        var manifest = JsonSerializer.Serialize(document, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        updates.Add(new CorpusFileUpdate(
            Path.Combine(corpusDirectory, "oss-methods.json"),
            manifest));
        return new OpenSourceCorpusImportPlan(document, updates.ToImmutable());
    }

    internal static OpenSourceCorpusSourcesDocument LoadSources(string repositoryRoot)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        var path = OpenSourceCorpusCatalog.GetSourcesConfigPath(repositoryRoot);
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"The OSS corpus source configuration is missing: {path}");
        }

        var document = JsonSerializer.Deserialize<OpenSourceCorpusSourcesDocument>(
                File.ReadAllText(path),
                ConfigJsonOptions) ??
            throw new InvalidDataException("The OSS corpus source configuration is empty.");
        ValidateSources(document);
        return document;
    }

    internal static void ValidateSources(OpenSourceCorpusSourcesDocument document)
    {
        System.ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != SourcesSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported OSS corpus source configuration schema {document.SchemaVersion}.");
        }

        if (document.Sources.IsDefaultOrEmpty || document.Sources.Any(static source => source is null))
        {
            throw new InvalidDataException(
                "The OSS corpus source configuration must list its sources.");
        }

        var ids = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var source in document.Sources)
        {
            if (source.Id.Length == 0 ||
                !source.Id.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ||
                !ids.Add(source.Id))
            {
                throw new InvalidDataException(
                    $"OSS corpus source ID '{source.Id}' must be unique lowercase letters, digits, and hyphens.");
            }

            if (!Uri.TryCreate(source.Repository, UriKind.Absolute, out var repository) ||
                repository.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(NormalizeRepositoryUrl(source.Repository), source.Repository, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus source {source.Id} has an invalid repository URL.");
            }

            if (source.Commit.Length != 40 ||
                source.Commit.Any(static character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            {
                throw new InvalidDataException(
                    $"OSS corpus source {source.Id} must pin a full Git commit.");
            }

            if (!OpenSourceCorpusCatalog.AllowedLicenses.Contains(source.LicenseSpdx, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus source {source.Id} uses unsupported license " +
                    $"{source.LicenseSpdx}; the corpus accepts " +
                    $"{string.Join(" and ", OpenSourceCorpusCatalog.AllowedLicenses)} only.");
            }

            if (source.Notices.IsDefault || source.Roots.IsDefaultOrEmpty || source.Excludes.IsDefault ||
                source.Notices.Prepend(source.License).Any(static file => file is null || !IsUpstreamPath(file.Path) ||
                    file.Sha256.Length != 64) ||
                source.Roots.Concat(source.Excludes).Any(static path => !IsUpstreamPath(path.TrimEnd('/'))))
            {
                throw new InvalidDataException(
                    $"OSS corpus source {source.Id} has invalid license, notice, root, or exclude paths.");
            }

            if (source.MethodQuota <= 0)
            {
                throw new InvalidDataException(
                    $"OSS corpus source {source.Id} must select at least one method.");
            }

            OpenSourceCorpusCatalog.ValidateCompilationOptions(source.Id, source.Compilation);
        }

        var total = document.Sources.Sum(static source => source.MethodQuota);
        if (total > OpenSourceCorpusCatalog.MaximumMethodCount)
        {
            throw new InvalidDataException(
                $"OSS corpus source quotas total {total} methods; at most " +
                $"{OpenSourceCorpusCatalog.MaximumMethodCount} are allowed.");
        }
    }

    private static bool IsUpstreamPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            !Path.IsPathRooted(path) &&
            !path.Contains('\\', StringComparison.Ordinal) &&
            path.Split('/').All(static part => part.Length != 0 && part is not ("." or "..")) &&
            !path.Any(char.IsControl);
    }

    // The copy lives at third-party/<source-id>/<upstream path>.
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    private static async Task<OpenSourceCorpusNoticeFile> CopyReviewedFileAsync(
        string upstreamRoot,
        string corpusDirectory,
        OpenSourceCorpusSourceConfig source,
        OpenSourceCorpusUpstreamFile reviewed,
        ImmutableArray<CorpusFileUpdate>.Builder updates,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(updates);
        var text = Encoding.UTF8.GetString(
            (await ReadGitBlobAsync(
                    upstreamRoot,
                    source.Commit,
                    reviewed.Path,
                    cancellationToken)
                .ConfigureAwait(false)).AsSpan());
        var content = ValidateReviewedUpstreamFile(source.Id, reviewed, text);
        var relative = $"third-party/{source.Id}/{reviewed.Path}";
        var target = Path.GetFullPath(Path.Combine(corpusDirectory, relative));
        OpenSourceCorpusCatalog.EnsureContained(corpusDirectory, target);
        updates.Add(new CorpusFileUpdate(target, content));
        return new OpenSourceCorpusNoticeFile(relative, reviewed.Sha256);
    }

    // Returns the normalized copy whose hash the reviewer recorded.
    internal static string ValidateReviewedUpstreamFile(
        string sourceId,
        OpenSourceCorpusUpstreamFile reviewed,
        string text)
    {
        System.ArgumentNullException.ThrowIfNull(sourceId);
        System.ArgumentNullException.ThrowIfNull(reviewed);
        System.ArgumentNullException.ThrowIfNull(text);
        var normalized = OpenSourceCorpusCatalog.NormalizeLineEndings(text).TrimStart(ByteOrderMark);
        var content = normalized.EndsWith('\n') ? normalized : normalized + "\n";
        if (!string.Equals(
                OpenSourceCorpusCatalog.ComputeNormalizedSha256(content),
                reviewed.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The upstream {reviewed.Path} of {sourceId} no longer has its reviewed text.");
        }

        return content;
    }

    private static OpenSourceCorpusDocument? LoadExisting(string corpusDirectory)
    {
        System.ArgumentNullException.ThrowIfNull(corpusDirectory);
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var existing = JsonSerializer.Deserialize<OpenSourceCorpusDocument>(
            File.ReadAllText(manifestPath),
            JsonOptions) ?? throw new InvalidDataException(
            "The existing OSS corpus manifest is empty.");
        _ = CorpusInputParsers.ReadExistingSupport(existing);
        return existing;
    }

    // Replaces one source's entry and files. Its methods that are still
    // candidates keep their order, support, and (absent removals) IDs; new
    // selections fill the quota and are appended.
    internal static OpenSourceCorpusDocument Merge(
        OpenSourceCorpusDocument? existing,
        OpenSourceCorpusSource source,
        ImmutableArray<OpenSourceCorpusFile> files,
        ImmutableArray<ImportCandidate> candidates,
        int quota)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        RepositoryParameterGuard.RequireInitialized(files.IsDefault, nameof(files), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        var existingSources = existing is null || existing.Sources.IsDefault ? [] : existing.Sources;
        var existingFiles = existing is null || existing.Files.IsDefault ? [] : existing.Files;
        var existingMethods = existing is null || existing.Methods.IsDefault ? [] : existing.Methods;
        bool IsImported(string sourceId)
        {
            System.ArgumentNullException.ThrowIfNull(sourceId);
            return string.Equals(sourceId, source.Id, StringComparison.Ordinal);
        }

        var otherHashes = existingMethods
            .Where(method => !IsImported(method.SourceId))
            .Select(static method => method.DeclarationSha256)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var available = candidates
            .Where(candidate => !otherHashes.Contains(candidate.DeclarationSha256))
            .ToImmutableArray();
        var byHash = available.ToImmutableDictionary(
            static candidate => candidate.DeclarationSha256,
            StringComparer.Ordinal);
        var kept = existingMethods
            .Where(method => IsImported(method.SourceId) && byHash.ContainsKey(method.DeclarationSha256))
            .OrderBy(static method => method.Id, StringComparer.Ordinal)
            .Take(quota)
            .ToImmutableArray();
        var keptHashes = kept.Select(static method => method.DeclarationSha256)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var fill = SelectRoundRobin(
            [.. available.Where(candidate => !keptHashes.Contains(candidate.DeclarationSha256))],
            quota - kept.Length);
        var selectedPaths = kept.Select(method => byHash[method.DeclarationSha256])
            .Concat(fill)
            .ToImmutableArray();
        if (selectedPaths.Length != quota)
        {
            throw new InvalidDataException(
                $"Only {selectedPaths.Length} distinct upstream methods were found; " +
                $"{quota} are required.");
        }

        RequireFileSpread(selectedPaths);
        var keptByHash = kept.ToImmutableDictionary(
            static method => method.DeclarationSha256,
            StringComparer.Ordinal);
        var ordered = existingMethods
            .OrderBy(static method => method.Id, StringComparer.Ordinal)
            .Where(method => !IsImported(method.SourceId) || keptByHash.ContainsKey(method.DeclarationSha256))
            .Select(method => IsImported(method.SourceId)
                ? byHash[method.DeclarationSha256].ToMethod(
                    method.Id, method.ExpectedVerdict, method.Support)
                : method)
            .Concat(fill.Select(static candidate => candidate.ToMethod(
                string.Empty, CorpusVerdict.SilentUnknown, CorpusSupport.Unspecified)))
            .Select(static (method, index) => method with { Id = $"OSS{index + 1:D4}" })
            .ToImmutableArray();

        var retainedSources = existingSources.Where(static candidate => candidate is not null).ToImmutableArray();
        var sources = retainedSources.Any(candidate => IsImported(candidate.Id))
            ? retainedSources.Select(candidate => IsImported(candidate.Id) ? source : candidate)
            : retainedSources.Append(source);
        var sourceOrder = sources.Select(static candidate => candidate.Id).ToImmutableArray();
        return new OpenSourceCorpusDocument(
            OpenSourceCorpusCatalog.SchemaVersion,
            [.. sources],
            [.. existingFiles.Where(file => !IsImported(file.SourceId))
                .Concat(files)
                .OrderBy(file => sourceOrder.IndexOf(file.SourceId))
                .ThenBy(static file => file.Path, StringComparer.Ordinal)],
            ordered);
    }

    // Lists every tracked C# file under the roots (less excludes) at the
    // pinned commit, and the methods the gate can instrument.
    internal static async Task<(
        ImmutableArray<OpenSourceCorpusFile> Files,
        ImmutableArray<ImportCandidate> Candidates)> DiscoverSourcesAsync(
        string upstreamRoot,
        OpenSourceCorpusSourceConfig source,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(upstreamRoot);
        foreach (var root in source.Roots)
        {
            var sourceRoot = Path.Combine(upstreamRoot, root);
            if (!Directory.Exists(sourceRoot))
            {
                throw new InvalidDataException(
                    $"Expected upstream source directory is missing: {sourceRoot}");
            }
        }

        var parseOptions = OpenSourceCorpusCatalog.GetParseOptions(source.Compilation);
        var files = ImmutableArray.CreateBuilder<OpenSourceCorpusFile>();
        var candidates = ImmutableArray.CreateBuilder<ImportCandidate>();
        var declarationHashes = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var trackedPaths = await ReadGitAsync(
                upstreamRoot,
                ["ls-tree", "-r", "--name-only", source.Commit, "--", .. source.Roots],
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var relativePath in trackedPaths
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal))
                     .Where(path => !IsExcluded(source.Excludes, path))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            // A byte-order mark is encoding, not source text.
            var content = OpenSourceCorpusCatalog.NormalizeLineEndings(
                Encoding.UTF8.GetString(
                    (await ReadGitBlobAsync(
                            upstreamRoot,
                            source.Commit,
                            relativePath,
                            cancellationToken)
                        .ConfigureAwait(false)).AsSpan())).TrimStart(ByteOrderMark);
            files.Add(
                new OpenSourceCorpusFile(
                    source.Id,
                    relativePath,
                    OpenSourceCorpusCatalog.ComputeNormalizedSha256(content),
                    content));
            var tree = CSharpSyntaxTree.ParseText(
                content,
                parseOptions,
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
                        method.Identifier.ValueText)
                    { SourceId = source.Id });
            }
        }
        return (files.ToImmutable(), candidates.ToImmutable());
    }

    // An exclude names a file or, with a trailing slash, a directory.
    private static bool IsExcluded(ImmutableArray<string> excludes, string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        return excludes.Any(exclude => exclude.EndsWith('/')
            ? path.StartsWith(exclude, StringComparison.Ordinal)
            : string.Equals(path, exclude, StringComparison.Ordinal));
    }

    internal static bool IsCandidate(MethodDeclarationSyntax method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
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

    internal static ImmutableArray<ImportCandidate> SelectDiverseCandidates(
        ImmutableArray<ImportCandidate> candidates,
        int count)
    {
        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        ValidateCapacity(count);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1512:Use ArgumentOutOfRangeException throw helper",
            Justification = "Preserve the former builder's capacity exception without an ActualValue.")]
        static void ValidateCapacity(int capacity)
        {
            if (capacity < 0)
            { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        }
        var selected = SelectRoundRobin(candidates, count);
        if (selected.Length != count)
        {
            throw new InvalidDataException(
                $"Only {selected.Length} distinct upstream methods were found; " +
                $"{count} are required.");
        }

        RequireFileSpread(selected);
        return selected;
    }

    // Round-robin across source files, in path order, so no file dominates.
    private static ImmutableArray<ImportCandidate> SelectRoundRobin(
        ImmutableArray<ImportCandidate> candidates,
        int count)
    {
        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        var byFile = candidates
            .GroupBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => group.ToImmutableArray())
            .ToImmutableArray();
        var selected = ImmutableArray.CreateBuilder<ImportCandidate>(Math.Max(count, 0));
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

        return selected.ToImmutable();
    }

    private static void RequireFileSpread(ImmutableArray<ImportCandidate> selected)
    {
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
    }

    internal static async Task<string> ReadGitAsync(
        string workingDirectory,
        ImmutableArray<string> arguments,
        CancellationToken cancellationToken,
        string gitExecutable = "git")
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(gitExecutable);
        System.ArgumentNullException.ThrowIfNull(workingDirectory);
        var startInfo = GateProcess.CreateCaptured(
            gitExecutable,
            workingDirectory);
        var capturedArguments = arguments;
        foreach (var argument in capturedArguments)
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
                $"git {string.Join(" ", capturedArguments)} failed: {result.Error.Trim()}");
        }

        return result.Output.Trim();
    }

    internal static async Task<ImmutableArray<byte>> ReadGitBlobAsync(
        string workingDirectory,
        string commit,
        string relativePath,
        CancellationToken cancellationToken,
        string gitExecutable = "git")
    {
        System.ArgumentNullException.ThrowIfNull(commit);
        System.ArgumentNullException.ThrowIfNull(gitExecutable);
        System.ArgumentNullException.ThrowIfNull(relativePath);
        System.ArgumentNullException.ThrowIfNull(workingDirectory);
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
        var waitCompleted = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .ConfigureAwait(false);
            waitCompleted = true;
        }
        finally
        {
            if (!waitCompleted)
            {
                GateProcess.KillTree(process);

                // Reap the killed process even when the caller canceled.
                var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(GateProcess.CleanupTimeoutSeconds));
                try
                {
#pragma warning disable SPINT003
                    await process.WaitForExitAsync(cleanup.Token)
                        .ConfigureAwait(false);
#pragma warning restore SPINT003
                }
                finally
                {
                    cleanup.Dispose();
                }
            }
        }

        await outputTask.ConfigureAwait(false);
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git cat-file blob {commit}:{relativePath} failed: {error}");
        }

        return ImmutableArray.Create<byte>(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
    }

    internal static string NormalizeRepositoryUrl(string value)
    {
        System.ArgumentNullException.ThrowIfNull(value);
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
        string repositoryUrl,
        string commit,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryUrl);
        System.ArgumentNullException.ThrowIfNull(commit);
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
                        repositoryUrl,
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
                    $"approved repository {repositoryUrl}.",
                    exception);
            }
        }
        finally
        {
            verificationRoot.Delete(recursive: true);
        }
    }

    internal sealed record ImportCandidate(
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
            System.ArgumentNullException.ThrowIfNull(id);
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
        public string Path { get; init; } = Path ?? throw new System.ArgumentNullException(nameof(Path));
        public string SourceId { get; init; } = string.Empty;
        public string DeclarationSha256 { get; init; } = DeclarationSha256 ?? throw new System.ArgumentNullException(nameof(DeclarationSha256));
        public string MethodName { get; init; } = MethodName ?? throw new System.ArgumentNullException(nameof(MethodName));
    }
}
