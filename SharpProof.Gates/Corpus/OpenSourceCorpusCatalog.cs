using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static OpenSourceCorpusDocument Load(string repositoryRoot)
    {
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
        Validate(document, corpusDirectory);
        return document;
    }

    internal static ImmutableArray<CorpusCase> CreateCases(
        string repositoryRoot)
    {
        return [.. Load(repositoryRoot).Methods.Select(static method =>
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
        return HashEncoding.ComputeSha256Hex(Encoding.UTF8.GetBytes(normalizedValue));
    }

    internal static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    internal static string GetCorpusDirectory(string repositoryRoot)
    {
        return Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "SharpProof.Gates",
            "Corpus");
    }

    internal static string GetSourceFileKey(string sourceId, string path)
    {
        return sourceId + "|" + path;
    }

    internal static int CountSourceFiles(
        IEnumerable<OpenSourceCorpusMethod> methods)
    {
        return methods
            .Select(static method => GetSourceFileKey(
                method.SourceId,
                method.Path))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    internal static string GetDeclaration(MethodDeclarationSyntax method)
    {
        return NormalizeLineEndings(
                method.WithoutLeadingTrivia()
                    .WithoutTrailingTrivia()
                    .ToFullString())
            .Trim();
    }

    private static void Validate(
        OpenSourceCorpusDocument document,
        string corpusDirectory)
    {
        if (document.SchemaVersion != 2)
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

        var sourceIds = ValidateSourceIds(document.Sources);

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

        var files = new Dictionary<
            string,
            (OpenSourceCorpusFile File, CompilationUnitSyntax Root)>(
            StringComparer.Ordinal);
        foreach (var file in document.Files)
        {
            if (!sourceIds.Contains(file.SourceId))
            {
                throw new InvalidDataException(
                    $"OSS corpus file {file.Path} refers to unknown source " +
                    $"{file.SourceId}.");
            }

            ValidateRelativePath(file.Path, $"source file {file.Path}");
            var key = GetSourceFileKey(file.SourceId, file.Path);
            if (files.ContainsKey(key))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source file: {key}.");
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
                    AnalyzerGateHost.ParseOptions,
                    file.Path)
                .GetCompilationUnitRoot();
            files.Add(key, (file, root));
        }

        var declarationIndexes = files.ToDictionary(
            static pair => pair.Key,
            static pair => BuildDeclarationIndex(pair.Value.Root),
            StringComparer.Ordinal);

        var locations = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Methods.Length; index++)
        {
            var method = document.Methods[index];
            var expectedId = $"OSS{index + 1:D4}";
            if (!string.Equals(method.Id, expectedId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"OSS corpus IDs must be contiguous and sorted; expected " +
                    $"{expectedId}, found {method.Id}.");
            }

            var fileKey = GetSourceFileKey(method.SourceId, method.Path);
            if (!files.TryGetValue(fileKey, out var sourceFile))
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
            if (!locations.Add(location))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source location: {location}.");
            }

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

            if (!declarations.Add(declarationHash))
            {
                throw new InvalidDataException(
                    $"OSS corpus method {method.Id} duplicates another declaration.");
            }

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
    }

    internal static HashSet<string> ValidateSourceIds(
        IEnumerable<OpenSourceCorpusSource> sources)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                throw new InvalidDataException(
                    "OSS corpus source IDs must not be empty.");
            }

            if (!sourceIds.Add(source.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source ID: {source.Id}.");
            }
        }

        return sourceIds;
    }

    internal static ImmutableDictionary<
        (int StartLine, int EndLine),
        ImmutableArray<MethodDeclarationSyntax>> BuildDeclarationIndex(
        CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .GroupBy(static candidate =>
            {
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

        if (!string.Equals(source.LicenseSpdx, "MIT", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} uses unsupported license " +
                $"{source.LicenseSpdx}; this importer currently accepts MIT only.");
        }

        ValidateRelativePath(source.LicenseFile, $"source {source.Id} license");
        var licensePath = Path.GetFullPath(
            Path.Combine(corpusDirectory, source.LicenseFile));
        EnsureContained(corpusDirectory, licensePath);
        if (!File.Exists(licensePath))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} license file is missing.");
        }

        var actualHash = HashEncoding.ComputeSha256Hex(File.ReadAllBytes(licensePath));
        if (!string.Equals(
                actualHash,
                source.LicenseSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} license hash does not match.");
        }
    }

    private static void ValidateRelativePath(string path, string description)
    {
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
        var lexicalRoot = Path.GetFullPath(root);
        var lexicalPath = Path.GetFullPath(path);
        var lexicalRelative = Path.GetRelativePath(lexicalRoot, lexicalPath);
        if (Path.IsPathRooted(lexicalRelative) ||
            lexicalRelative.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Any(static part => part == ".."))
        {
            throw new InvalidDataException(
                $"Generated OSS corpus path escaped its directory: {path}");
        }

        var resolvedRoot = ResolvePath(lexicalRoot);
        var resolvedPath = ResolvePath(lexicalPath);
        var resolvedRelative = Path.GetRelativePath(resolvedRoot, resolvedPath);
        if (Path.IsPathRooted(resolvedRelative) ||
            resolvedRelative.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Any(static part => part == ".."))
        {
            throw new InvalidDataException(
                $"Generated OSS corpus path follows a link outside its directory: {path}");
        }
    }

    private static string ResolvePath(string path)
    {
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
        foreach (FileSystemInfo info in new FileSystemInfo[] { new FileInfo(path), new DirectoryInfo(path) })
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
        return value.Length == length &&
        value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
