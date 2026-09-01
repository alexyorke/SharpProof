using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Checked-in corpus manifests publish SHA-256 values in lowercase hexadecimal.")]
    internal static string ComputeSha256(string value)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(NormalizeLineEndings(value))))
            .ToLowerInvariant();
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

    internal static int CountSourceFiles(
        IEnumerable<OpenSourceCorpusMethod> methods)
    {
        return methods
            .Select(static method => method.SourceId + "|" + method.Path)
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

        ValidateSourceIds(document.Sources);

        if (document.Methods.Length is < MinimumMethodCount or > MaximumMethodCount)
        {
            throw new InvalidDataException(
                $"The OSS corpus has {document.Methods.Length} methods; " +
                $"{MinimumMethodCount}-{MaximumMethodCount} are required.");
        }

        var sources = document.Sources.ToImmutableDictionary(
            static source => source.Id,
            StringComparer.Ordinal);
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
            if (!sources.ContainsKey(file.SourceId))
            {
                throw new InvalidDataException(
                    $"OSS corpus file {file.Path} refers to unknown source " +
                    $"{file.SourceId}.");
            }

            ValidateRelativePath(file.Path, $"source file {file.Path}");
            var key = $"{file.SourceId}|{file.Path}";
            if (files.ContainsKey(key))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus source file: {key}.");
            }

            var content = NormalizeLineEndings(file.Content);
            if (!string.Equals(
                    ComputeSha256(content),
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

        var ids = new HashSet<string>(StringComparer.Ordinal);
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

            if (!ids.Add(method.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate OSS corpus method ID: {method.Id}.");
            }

            var fileKey = $"{method.SourceId}|{method.Path}";
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

            var declaration = FindDeclaration(sourceFile.Root, method);
            var declarationHash = ComputeSha256(GetDeclaration(declaration));
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

        var sourceFileCount = document.Methods
            .Select(static method => $"{method.SourceId}|{method.Path}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (sourceFileCount < MinimumSourceFileCount)
        {
            throw new InvalidDataException(
                $"The OSS corpus spans only {sourceFileCount} source files; " +
                $"{MinimumSourceFileCount} are required to prevent one-file padding.");
        }
    }

    internal static void ValidateSourceIds(
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
    }

    internal static MethodDeclarationSyntax FindDeclaration(
        CompilationUnitSyntax root,
        OpenSourceCorpusMethod method)
    {
        var matches = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(candidate =>
            {
                var lineSpan = candidate.SyntaxTree.GetLineSpan(candidate.Span);
                return lineSpan.StartLinePosition.Line + 1 == method.StartLine &&
                       lineSpan.EndLinePosition.Line + 1 == method.EndLine;
            })
            .ToImmutableArray();
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
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            throw new InvalidDataException("An OSS corpus source has no ID.");
        }

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
        var relative = Path.GetRelativePath(corpusDirectory, licensePath);
        ValidateRelativePath(relative, $"source {source.Id} resolved license");
        if (!File.Exists(licensePath))
        {
            throw new InvalidDataException(
                $"OSS corpus source {source.Id} license file is missing.");
        }

        var actualHash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(licensePath)))
            .ToLowerInvariant();
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

    private static bool IsLowerHex(string value, int length)
    {
        return value.Length == length &&
        value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
