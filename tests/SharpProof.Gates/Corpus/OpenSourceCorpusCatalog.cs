using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Ir;

namespace SharpProof.Gates.Corpus;

internal static class OpenSourceCorpusCatalog
{
    internal const int MinimumMethodCount = 200;
    internal const int MaximumMethodCount = 500;
    internal const int MinimumSourceFileCount = 25;
    internal const int SchemaVersion = 3;
    internal static readonly ImmutableArray<string> AllowedLicenses = ["Apache-2.0", "MIT"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly ConditionalWeakTable<
        OpenSourceCorpusDocument,
        ValidationResult> ParsedFiles = new();

    internal static OpenSourceCorpusDocument Load(string repositoryRoot)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        var corpusDirectory = GetCorpusDirectory(repositoryRoot);
        CorpusFileTransaction.Recover(corpusDirectory);
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                $"The checked-in OSS corpus manifest is missing: {manifestPath}");
        }

        var document = JsonSerializer.Deserialize<OpenSourceCorpusDocument>(
                File.ReadAllText(manifestPath),
                JsonOptions) ??
            throw new InvalidDataException("The OSS corpus manifest is empty.");
        ParsedFiles.Add(document, Validate(document, corpusDirectory));
        return document;
    }

    internal static ImmutableDictionary<string, CompilationUnitSyntax>?
        GetParsedFiles(OpenSourceCorpusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ParsedFiles.TryGetValue(document, out var validation)
            ? validation.ParsedFiles
            : null;
    }

    internal static ImmutableDictionary<
        string,
        ImmutableDictionary<
            (int StartLine, int EndLine),
            ImmutableArray<MethodDeclarationSyntax>>>?
        GetDeclarationIndexes(OpenSourceCorpusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ParsedFiles.TryGetValue(document, out var validation)
            ? validation.DeclarationIndexes
            : null;
    }

    internal static int GetSourceFileCount(
        OpenSourceCorpusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ParsedFiles.TryGetValue(document, out var validation)
            ? validation.SourceFileCount
            : CountSourceFiles(document.Methods);
    }

    internal static ImmutableArray<CorpusCase> CreateCases(
        OpenSourceCorpusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. document.Methods.Select(static method =>
            new CorpusCase(
                $"{method.Id}.baseline",
                method.Id,
                CorpusVariant.Baseline,
                method.Mode,
                method.ExpectedVerdict,
                method.Support,
                string.Empty,
                CorpusOrigin.OpenSource,
                $"{method.SourceId}:{method.Path}:{method.StartLine}"))];
    }

    internal static string ComputeNormalizedSha256(string normalizedValue)
    {
        System.ArgumentNullException.ThrowIfNull(normalizedValue);
        return HashEncoding.ComputeSha256Hex((Encoding.UTF8.GetBytes(normalizedValue)).ToImmutableArray());
    }

    internal static string NormalizeLineEndings(string value)
    {
        System.ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    internal static string GetCorpusDirectory(string repositoryRoot)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        return Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "tests/SharpProof.Gates",
            "Corpus");
    }

    internal static string GetSourcesConfigPath(string repositoryRoot)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        return Path.Combine(GetCorpusDirectory(repositoryRoot), "sources.json");
    }

    // Every source's files parse with that source's preprocessor symbols.
    internal static CSharpParseOptions GetParseOptions(
        OpenSourceCorpusCompilationOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        return options.PreprocessorSymbols.IsDefaultOrEmpty
            ? AnalyzerGateHost.ParseOptions
            : AnalyzerGateHost.ParseOptions.WithPreprocessorSymbols(options.PreprocessorSymbols);
    }

    // Documents built by tests may omit their source entries; those files
    // compile with the default options.
    internal static OpenSourceCorpusCompilationOptions GetCompilationOptions(
        OpenSourceCorpusDocument document,
        string sourceId)
    {
        System.ArgumentNullException.ThrowIfNull(document);
        System.ArgumentNullException.ThrowIfNull(sourceId);
        return (document.Sources.IsDefault ? [] : document.Sources)
            .FirstOrDefault(source => source is not null && string.Equals(source.Id, sourceId, StringComparison.Ordinal))?
            .Compilation ?? OpenSourceCorpusCompilationOptions.Default;
    }

    internal static string GetSourceFileKey(string sourceId, string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(sourceId);
        return sourceId + "|" + path;
    }

    internal static int CountSourceFiles(
        IEnumerable<OpenSourceCorpusMethod> methods)
    {
        System.ArgumentNullException.ThrowIfNull(methods);
        return methods
            .Select(static method => GetSourceFileKey(
                method.SourceId,
                method.Path))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    internal static string GetDeclaration(MethodDeclarationSyntax method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        return NormalizeLineEndings(
                method.WithoutLeadingTrivia()
                    .WithoutTrailingTrivia()
                    .ToFullString())
            .Trim();
    }

    private static ValidationResult Validate(
        OpenSourceCorpusDocument document,
        string corpusDirectory)
    {
        if (document.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported OSS corpus schema {document.SchemaVersion}.");
        }

        if (document.Sources.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The OSS corpus must identify at least one upstream source.");
        }

        if (document.Files.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The OSS corpus must contain its pinned upstream source files.");
        }

        if (document.Methods.IsDefault)
        {
            throw new InvalidDataException(
                "The OSS corpus manifest must list its methods.");
        }

        var sourceIds = ValidateSourceIds(document.Sources);
        var sourcesById = document.Sources.ToImmutableDictionary(
            static source => source.Id,
            StringComparer.Ordinal);

        if (document.Methods.Length is < MinimumMethodCount or > MaximumMethodCount)
        {
            throw new InvalidDataException(
                $"The OSS corpus has {document.Methods.Length} methods; " +
                $"{MinimumMethodCount}-{MaximumMethodCount} are required.");
        }

        foreach (var source in document.Sources)
        {
            ValidateSource(source, corpusDirectory);
        }

        var fileBuilder = ImmutableDictionary.CreateBuilder<string, CompilationUnitSyntax>(StringComparer.Ordinal);
        foreach (var file in document.Files)
        {
            if (file is null)
            {
                throw new InvalidDataException(
                    "OSS corpus source files must not be null.");
            }

            if (!sourceIds.Contains(file.SourceId))
            {
                throw new InvalidDataException(
                    $"OSS corpus file {file.Path} refers to unknown source " +
                    $"{file.SourceId}.");
            }

            ValidateRelativePath(file.Path, $"source file {file.Path}");
            var key = GetSourceFileKey(file.SourceId, file.Path);
            if (fileBuilder.ContainsKey(key))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source file: {key}.");
            }

            if (file.Content is null)
            {
                throw new InvalidDataException(
                    $"OSS corpus source file has no content: {key}.");
            }

            var content = NormalizeLineEndings(file.Content);
            if (!string.Equals(
                    ComputeNormalizedSha256(content),
                    file.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus source file hash does not match: {key}.");
            }

            var root = CSharpSyntaxTree.ParseText(
                    content,
                    GetParseOptions(sourcesById[file.SourceId].Compilation),
                    file.Path)
                .GetCompilationUnitRoot();
            fileBuilder.Add(key, root);
        }

        var files = fileBuilder.ToImmutable();
        var declarationIndexes = files.ToImmutableDictionary(
            static pair => pair.Key,
            static pair => BuildDeclarationIndex(pair.Value),
            StringComparer.Ordinal);

        var locations = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var declarations = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Methods.Length; index++)
        {
            var method = document.Methods[index];
            if (method is null)
            {
                throw new InvalidDataException(
                    $"OSS corpus method at index {index} must not be null.");
            }

            var expectedId = $"OSS{index + 1:D4}";
            if (!string.Equals(method.Id, expectedId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus IDs must be contiguous and sorted; expected " +
                    $"{expectedId}, found {method.Id}.");
            }

            var fileKey = GetSourceFileKey(method.SourceId, method.Path);
            if (!files.ContainsKey(fileKey))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} refers to missing source " +
                    $"{fileKey}.");
            }

            if (method.StartLine <= 0 || method.EndLine < method.StartLine)
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} has an invalid line range.");
            }

            var location =
                $"{fileKey}|{method.StartLine}|{method.EndLine}";
            if (locations.Contains(location))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source location: {location}.");
            }

            locations.Add(location);
            var declaration = FindDeclaration(
                declarationIndexes[fileKey],
                method);
            var declarationHash = ComputeNormalizedSha256(
                GetDeclaration(declaration));
            if (!string.Equals(
                    declarationHash,
                    method.DeclarationSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} declaration hash does not match.");
            }

            if (declarations.Contains(declarationHash))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} duplicates another declaration.");
            }

            declarations.Add(declarationHash);
            if (!string.Equals(
                    declaration.Identifier.ValueText,
                    method.MethodName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} name does not match its source.");
            }

            if (!string.Equals(method.Mode, "effects", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} must run in effects mode.");
            }

            if (method.Support is not (
                    CorpusSupport.Supported or
                    CorpusSupport.IntentionallyUnsupported))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} requires an explicit " +
                    "support classification.");
            }
        }

        var sourceFileCount = CountSourceFiles(document.Methods);
        if (sourceFileCount < MinimumSourceFileCount)
        {
            throw new InvalidDataException(
                $"The OSS corpus spans only {sourceFileCount} source files; " +
                $"{MinimumSourceFileCount} are required to prevent one-file padding.");
        }

        return new ValidationResult(
            files,
            declarationIndexes,
            sourceFileCount);
    }

    private sealed class ValidationResult(
        ImmutableDictionary<string, CompilationUnitSyntax> parsedFiles,
        ImmutableDictionary<
            string,
            ImmutableDictionary<
                (int StartLine, int EndLine),
                ImmutableArray<MethodDeclarationSyntax>>> declarationIndexes,
        int sourceFileCount)
    {
        internal ImmutableDictionary<string, CompilationUnitSyntax> ParsedFiles { get; } =
            (parsedFiles ?? throw new System.ArgumentNullException(nameof(parsedFiles)));
        internal ImmutableDictionary<
            string,
            ImmutableDictionary<
                (int StartLine, int EndLine),
                ImmutableArray<MethodDeclarationSyntax>>> DeclarationIndexes
        { get; } =
            (declarationIndexes ?? throw new System.ArgumentNullException(nameof(declarationIndexes)));
        internal int SourceFileCount { get; } = sourceFileCount;
    }

    internal static ImmutableHashSet<string> ValidateSourceIds(
        IEnumerable<OpenSourceCorpusSource> sources)
    {
        System.ArgumentNullException.ThrowIfNull(sources);
        var sourceIds = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new InvalidDataException(
                    "OSS corpus sources must not be null.");
            }

            if (string.IsNullOrWhiteSpace(source.Id))
            {
                throw new InvalidDataException(
                    "OSS corpus source IDs must not be empty.");
            }

            if (sourceIds.Contains(source.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source ID: {source.Id}.");
            }
            sourceIds.Add(source.Id);
        }

        return sourceIds.ToImmutable();
    }

    internal static ImmutableDictionary<
        (int StartLine, int EndLine),
        ImmutableArray<MethodDeclarationSyntax>> BuildDeclarationIndex(
        CompilationUnitSyntax root)
    {
        System.ArgumentNullException.ThrowIfNull(root);
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .GroupBy(static candidate =>
            {
                System.ArgumentNullException.ThrowIfNull(candidate);
                var lineSpan = candidate.SyntaxTree.GetLineSpan(candidate.Span);
                return (
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1);
            })
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());
    }

    internal static MethodDeclarationSyntax FindDeclaration(
        ImmutableDictionary<(int StartLine, int EndLine),
            ImmutableArray<MethodDeclarationSyntax>> declarationIndex,
        OpenSourceCorpusMethod method)
    {
        System.ArgumentNullException.ThrowIfNull(declarationIndex);
        System.ArgumentNullException.ThrowIfNull(method);
        var key = (StartLine: method.StartLine, EndLine: method.EndLine);
        var matches = declarationIndex.TryGetValue(key, out var declarations)
            ? declarations
            : ImmutableArray<MethodDeclarationSyntax>.Empty;
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"OSS corpus method {method.Id} resolves to {matches.Length} " +
                $"declarations at {method.Path}:{method.StartLine}-{method.EndLine}.");
        }

        return matches[0];
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    private static void ValidateSource(
        OpenSourceCorpusSource source,
        string corpusDirectory)
    {
        if (!Uri.TryCreate(
                source.Repository,
                UriKind.Absolute,
                out var repository) ||
            repository.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} has an invalid repository URL.");
        }

        if (!IsLowerHex(source.Commit, 40))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} must pin a full Git commit.");
        }

        if (!AllowedLicenses.Contains(source.LicenseSpdx, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} uses unsupported license " +
                $"{source.LicenseSpdx}; the corpus accepts " +
                $"{string.Join(" and ", AllowedLicenses)} only.");
        }

        ValidateCopiedFile(corpusDirectory, source.Id, "license", source.LicenseFile, source.LicenseSha256);
        if (source.NoticeFiles.IsDefault || source.NoticeFiles.Any(static notice => notice is null))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} must list its notice files.");
        }

        foreach (var notice in source.NoticeFiles)
        {
            ValidateCopiedFile(corpusDirectory, source.Id, "notice", notice.File, notice.Sha256);
        }

        ValidateCompilationOptions(source.Id, source.Compilation);
    }

    internal static void ValidateCompilationOptions(
        string sourceId,
        OpenSourceCorpusCompilationOptions? options)
    {
        System.ArgumentNullException.ThrowIfNull(sourceId);
        if (options is null ||
            options.GlobalUsings.IsDefault ||
            options.PreprocessorSymbols.IsDefault ||
            options.GlobalUsings.Any(static name => string.IsNullOrWhiteSpace(name) ||
                !name.Split('.').All(SyntaxFacts.IsValidIdentifier)) ||
            options.PreprocessorSymbols.Any(static symbol => !SyntaxFacts.IsValidIdentifier(symbol)))
        {
            throw new InvalidDataException(
                $"OSS corpus source {sourceId} has invalid compilation options.");
        }
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    private static void ValidateCopiedFile(
        string corpusDirectory,
        string sourceId,
        string kind,
        string relativePath,
        string expectedSha256)
    {
        ValidateRelativePath(relativePath, $"source {sourceId} {kind}");
        var path = Path.GetFullPath(
            Path.Combine(corpusDirectory, relativePath));
        EnsureContained(corpusDirectory, path);
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"OSS corpus source {sourceId} {kind} file is missing.");
        }

        var actualHash = HashEncoding.ComputeSha256Hex((File.ReadAllBytes(path)).ToImmutableArray());
        if (!string.Equals(
                actualHash,
                expectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"OSS corpus source {sourceId} {kind} hash does not match.");
        }
    }

    private static void ValidateRelativePath(string path, string description)
    {
        System.ArgumentNullException.ThrowIfNull(description);
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Split('/', '\\').Any(static part => part == "..") ||
            path.Contains('|', StringComparison.Ordinal) ||
            path.Any(static character => character is '\r' or '\n' ||
                char.IsControl(character)))
        {
            throw new InvalidDataException(
                $"OSS corpus {description} path must be relative and contained.");
        }
    }

    internal static void EnsureContained(string root, string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(root);
        var lexicalRoot = Path.GetFullPath(root);
        var lexicalPath = Path.GetFullPath(path);
        var lexicalRelative = Path.GetRelativePath(lexicalRoot, lexicalPath);
        if (!IsContainedRelative(lexicalRelative))
        {
            throw new InvalidDataException(
                $"Generated OSS corpus path escaped its directory: {path}");
        }

        var resolvedRoot = ResolvePath(lexicalRoot);
        var resolvedPath = ResolvePath(lexicalPath);
        var resolvedRelative = Path.GetRelativePath(resolvedRoot, resolvedPath);
        if (!IsContainedRelative(resolvedRelative))
        {
            throw new InvalidDataException(
                $"Generated OSS corpus path follows a link outside its directory: {path}");
        }
    }

    private static bool IsContainedRelative(string relative)
    {
        System.ArgumentNullException.ThrowIfNull(relative);
        return !Path.IsPathRooted(relative) &&
            !relative.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Any(static part => part == "..");
    }

    internal static string ResolvePath(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath) ?? string.Empty;
        var relative = Path.GetRelativePath(current, fullPath);
        foreach (var part in relative.Split(
                     Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            var candidate = Path.Combine(current, part);
            var link = ResolveLink(candidate);
            if (link != null)
            {
                current = link;
                continue;
            }
            current = candidate;
        }

        return Path.GetFullPath(current);
    }

    private static string? ResolveLink(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        foreach (FileSystemInfo info in ImmutableArray.Create<FileSystemInfo>(new FileInfo(path), new DirectoryInfo(path)))
        {
            try
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    return target.FullName;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static bool IsLowerHex(string value, int length)
    {
        System.ArgumentNullException.ThrowIfNull(value);
        return value is not null && value.Length == length &&
        value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
