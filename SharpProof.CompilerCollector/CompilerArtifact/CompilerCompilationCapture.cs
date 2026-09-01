using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// This capture runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;
#pragma warning disable RS1035 // Build-only compiler evidence must hash final reference images.

internal interface ICompilerAdditionalTextSnapshot
{
    SourceText CapturedText { get; }
}

internal static class CompilerCompilationCapture
{
    private sealed class SyntaxTreeCache
    {
        internal SyntaxTreeCache(
            CSharpCompilation compilation,
            CancellationToken cancellationToken)
        {
            Trees = [.. compilation.SyntaxTrees.Select((tree, index) =>
            {
                var snapshot = CaptureTree(tree, cancellationToken);
                // Roslyn permits generated/in-memory trees without a path and
                // multiple trees sharing one path. Give each tree a stable
                // compilation-local identity instead of rejecting the input.
                if (string.IsNullOrEmpty(tree.FilePath))
                {
                    snapshot.Path = $"<compiler-generated:{index}>";
                }
                else if (compilation.SyntaxTrees.Take(index).Any(
                             prior => string.Equals(prior.FilePath, tree.FilePath, StringComparison.Ordinal)))
                {
                    snapshot.Path = $"{snapshot.Path}#{index}";
                }

                return snapshot;
            })];
        }

        internal CompilerSyntaxTreeSnapshot[] Trees { get; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CSharpCompilation, SyntaxTreeCache>
        SyntaxTreeCaches = new();

    internal static CompilerSyntaxTreeSnapshot[] CaptureTrees(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        return SyntaxTreeCaches.GetValue(
            compilation,
            value => new SyntaxTreeCache(value, cancellationToken)).Trees;
    }

    private const string CommandLineAdditionalTextTypeName =
        "Microsoft.CodeAnalysis.AdditionalTextFile";

    internal readonly struct ReferenceCaptureLimits
    {
        internal ReferenceCaptureLimits(
            long maximumModuleBytes,
            long maximumClosureBytes,
            int maximumModuleCount)
        {
            if (maximumModuleBytes <= 0 ||
                maximumClosureBytes < maximumModuleBytes ||
                maximumModuleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumModuleBytes));
            }
            MaximumModuleBytes = maximumModuleBytes;
            MaximumClosureBytes = maximumClosureBytes;
            MaximumModuleCount = maximumModuleCount;
        }

        internal long MaximumModuleBytes { get; }
        internal long MaximumClosureBytes { get; }
        internal int MaximumModuleCount { get; }

        internal static ReferenceCaptureLimits Default => new(
            CompilerReferenceLimits.MaximumModuleBytes,
            CompilerReferenceLimits.MaximumClosureBytes,
            CompilerReferenceLimits.MaximumModuleCount);
    }

    internal static CompilerCompilationSnapshot Capture(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, ImmutableArray<AdditionalText> additionalFiles, CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new ArgumentException(
            "The project directory and target framework are required.");
        }

        var normalizedProject = CompilerCaptureAuthority.NormalizePath(
            projectDirectory);
        var options = compilation.Options;
        if (additionalFiles.IsDefault)
        {
            additionalFiles = [];
        }

        var supersedes = CompilerOptionWireMappings.ReadInternalBoolean(
            options,
            "ReferencesSupersedeLowerVersions");
        if (supersedes || options.MetadataReferenceResolver?.ResolveMissingAssemblies == true ||
            compilation.SyntaxTrees.Any(tree => HasResolverDirective(tree, cancellationToken)))
        {
            throw new InvalidOperationException(
            "Reference supersession and resolver directives are unsupported.");
        }

        return new CompilerCompilationSnapshot
        {
            ProjectDirectory = normalizedProject,
            AssemblyName = compilation.AssemblyName ?? throw new InvalidOperationException("The assembly name is unavailable."),
            AssemblyIdentity = compilation.Assembly.Identity.ToString(),
            TargetFramework = targetFramework,
            CompilerVersion = CompilerCaptureAuthority.CaptureVersion(
                typeof(Compilation)),
            CompilerMvid = CompilerCaptureAuthority.CaptureMvid(
                typeof(Compilation)),
            CSharpCompilerVersion = CompilerCaptureAuthority.CaptureVersion(
                typeof(CSharpCompilation)),
            CSharpCompilerMvid = CompilerCaptureAuthority.CaptureMvid(
                typeof(CSharpCompilation)),
            Options = new CompilerCompilationOptionsSnapshot
            {
                OutputKind = CompilerOptionWireMappings.Map(options.OutputKind),
                MainTypeName = options.MainTypeName ?? string.Empty,
                OptimizationLevel = CompilerOptionWireMappings.Map(options.OptimizationLevel),
                Platform = CompilerOptionWireMappings.Map(options.Platform),
                NullableContext = CompilerOptionWireMappings.Map(options.NullableContextOptions),
                MetadataImportOptions = CompilerOptionWireMappings.Map(options.MetadataImportOptions),
                WarningLevel = options.WarningLevel,
                GeneralDiagnosticOption = CompilerOptionWireMappings.Map(
                    options.GeneralDiagnosticOption),
                SpecificDiagnosticOptions = [.. options.SpecificDiagnosticOptions
                    .OrderBy(static option => option.Key, StringComparer.Ordinal)
                    .Select(static option => new CompilerDiagnosticOptionSnapshot
                    {
                        Id = option.Key,
                        ReportDiagnostic = CompilerOptionWireMappings.Map(option.Value)
                    })],
                ReportSuppressedDiagnostics = options.ReportSuppressedDiagnostics,
                CheckOverflow = options.CheckOverflow,
                AllowUnsafe = options.AllowUnsafe,
                Deterministic = options.Deterministic,
                ReferencesSupersedeLowerVersions = supersedes,
                AssemblyIdentityComparer = CompilerOptionWireMappings.Map(options.AssemblyIdentityComparer),
                Usings = options.Usings.ToArray(),
                ResolverPolicy = CompilerResolverPolicy.EvidenceOnly
            },
            SyntaxTrees = CaptureTrees(compilation, cancellationToken),
            References = CaptureReferences(
                compilation.References,
                ReferenceCaptureLimits.Default,
                cancellationToken),
            AdditionalFiles = [.. additionalFiles.Select(file => CaptureAdditionalFile(
                file, normalizedProject, cancellationToken)).OrderBy(static file => file.Path, StringComparer.Ordinal)
                .ThenBy(static file => file.Sha256, StringComparer.Ordinal)]
        };
    }
    internal static CompilerSyntaxTreeSnapshot CaptureTree(
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parse = (CSharpParseOptions)tree.Options;
        var text = tree.GetText(cancellationToken);
        var characterOffsets = new Dictionary<int, int>();
        foreach (var mapping in tree.GetLineMappings(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mapping.CharacterOffset is { } characterOffset)
            {
                characterOffsets[mapping.Span.Start.Line] = characterOffset;
            }
        }
        CompilerSourceLineMapEntry[] lineMap = [.. text.Lines.Select((line, lineIndex) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapped = tree.GetMappedLineSpan(
                new TextSpan(line.Start, 0));
            return new CompilerSourceLineMapEntry
            {
                SourceStart = line.Start,
                SourceLength = line.Span.Length,
                MappedPath = MappedPath(tree, mapped),
                MappedLine = mapped.StartLinePosition.Line,
                MappedColumn = mapped.StartLinePosition.Character,
                CharacterOffset = characterOffsets.TryGetValue(
                    lineIndex, out var characterOffset)
                    ? characterOffset
                    : 0
            };
        })];
        return new CompilerSyntaxTreeSnapshot
        {
            Path = CompilerCaptureAuthority.NormalizePath(
                string.IsNullOrEmpty(tree.FilePath)
                    ? "<compiler-generated>"
                    : tree.FilePath),
            Sha256 = ComputeTextSha256(text),
            Encoding = text.Encoding?.WebName ?? string.Empty,
            ChecksumAlgorithm = text.ChecksumAlgorithm.ToString(),
            RoslynChecksum = LowerHex(BitConverter.ToString(text.GetChecksum().ToArray())
                .Replace("-", string.Empty)),
            LineMapSha256 = CompilationFingerprint.ComputeLineMapSha256(lineMap),
            TextLength = text.Length,
            LineMap = lineMap,
            LanguageVersion = parse.LanguageVersion.ToString(),
            DocumentationMode = parse.DocumentationMode.ToString(),
            Kind = parse.Kind.ToString(),
            PreprocessorSymbols = [.. parse.PreprocessorSymbolNames.OrderBy(static value => value, StringComparer.Ordinal)],
            EffectivePreprocessorSymbols = [
                .. CSharpPreprocessorSymbols.GetDefined(tree, cancellationToken)
                    .OrderBy(static value => value, StringComparer.Ordinal)
            ],
            Features = [.. parse.Features.OrderBy(static value => value.Key, StringComparer.Ordinal)
                .Select(static value => new CompilerFeatureSnapshot { Key = value.Key, Value = value.Value })]
        };

        static string LowerHex(string value)
        {
            var chars = value.ToCharArray();
            for (var index = 0; index < chars.Length; index++)
            {
                if (chars[index] is >= 'A' and <= 'F')
                {
                    chars[index] = (char)(chars[index] + ('a' - 'A'));
                }
            }
            return new string(chars);
        }
    }

    private static string MappedPath(
        SyntaxTree tree,
        FileLinePositionSpan mapped)
    {
        var path = mapped.Path;
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        path = tree.FilePath;
        return string.IsNullOrEmpty(path) ? "<compiler-generated>" : path;
    }
    internal static CompilerReferenceSnapshot[] CaptureReferences(
        IEnumerable<MetadataReference> references,
        ReferenceCaptureLimits limits,
        CancellationToken cancellationToken)
    {
        references = ArgumentNullGuard.NotNull(
            references,
            nameof(references));
        var budget = new ReferenceCaptureBudget(limits);
        return [.. references.Select(reference => CaptureReference(
            reference,
            budget,
            cancellationToken))];
    }

    private static CompilerReferenceSnapshot CaptureReference(
        MetadataReference reference,
        ReferenceCaptureBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var portable = reference as PortableExecutableReference ??
            throw new InvalidOperationException("The final compilation contains a non-file reference.");
        var path = portable.FilePath ?? throw new InvalidOperationException(
            "The final compilation contains a non-file reference.");
        var backingMetadata = portable.GetMetadata();
        if (reference.Properties.Kind == MetadataImageKind.Assembly &&
                backingMetadata is not AssemblyMetadata ||
            reference.Properties.Kind == MetadataImageKind.Module &&
                backingMetadata is not ModuleMetadata)
        {
            throw new InvalidDataException(
                "A compiler reference kind does not match its metadata.");
        }
        var backingModules = backingMetadata switch
        {
            AssemblyMetadata assembly => assembly.GetModules(),
            ModuleMetadata module => ImmutableArray.Create(module),
            _ => throw new InvalidDataException(
                "A compiler reference identity is unavailable.")
        };
        if (backingModules.Length > budget.MaximumModuleCount)
        {
            throw new InvalidDataException(
                "A compiler reference exceeds the module count limit.");
        }
        if (backingModules.Length > 1)
        {
            backingModules = [
                backingModules[0],
                .. backingModules.Skip(1).OrderBy(
                    static module => ReadModuleName(module.GetMetadataReader()),
                    StringComparer.Ordinal)
            ];
        }
        var modules = ImmutableArray.CreateBuilder<CompilerReferenceModuleSnapshot>(
            backingModules.Length);
        string? identity = null;
        for (var index = 0; index < backingModules.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backingReader = backingModules[index].GetMetadataReader();
            var backingName = ReadModuleName(backingReader);
            var modulePath = index == 0
                ? Path.GetFullPath(path)
                : ResolveSiblingModule(path, backingName);
            using var stream = new FileStream(
                modulePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var sizeBytes = stream.Length;
            budget.Consume(sizeBytes);
            using var image = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!image.HasMetadata)
            {
                throw new InvalidDataException(
                    "A compiler reference does not contain metadata.");
            }
            var fileReader = image.GetMetadataReader();
            if (index == 0 && fileReader.IsAssembly !=
                    (reference.Properties.Kind == MetadataImageKind.Assembly) ||
                index != 0 && fileReader.IsAssembly)
            {
                throw new InvalidDataException(
                    "A compiler reference kind does not match its file metadata.");
            }
            var fileName = ReadModuleName(fileReader);
            var fileMvid = ReadMvid(fileReader);
            if (!string.Equals(backingName, fileName, StringComparison.Ordinal) ||
                ReadMvid(backingReader) != fileMvid ||
                !MetadataEquals(backingReader, fileReader))
            {
                throw new InvalidDataException(
                    "A compiler reference path does not match its loaded metadata.");
            }
            if (index == 0)
            {
                identity = Identity(fileReader);
            }
            modules.Add(new CompilerReferenceModuleSnapshot
            {
                Name = fileName,
                Mvid = fileMvid.ToString("D"),
                Path = CompilerCaptureAuthority.NormalizePath(modulePath),
                Sha256 = Hash(stream, cancellationToken),
                SizeBytes = sizeBytes
            });
        }
        return new CompilerReferenceSnapshot
        {
            Kind = reference.Properties.Kind.ToString(),
            EmbedInteropTypes = reference.Properties.EmbedInteropTypes,
            HasRecursiveAliases = CompilerOptionWireMappings.ReadInternalBoolean(
                reference.Properties,
                "HasRecursiveAliases"),
            Aliases = [.. reference.Properties.Aliases.OrderBy(static value => value, StringComparer.Ordinal)],
            Identity = identity ?? throw new InvalidDataException(
                "A compiler reference contains no modules."),
            Modules = modules.ToArray()
        };
    }

    private sealed class ReferenceCaptureBudget
    {
        private readonly ReferenceCaptureLimits _limits;
        private long _closureBytes;
        private int _moduleCount;

        internal ReferenceCaptureBudget(ReferenceCaptureLimits limits)
        {
            _limits = limits;
        }

        internal int MaximumModuleCount => _limits.MaximumModuleCount;

        internal void Consume(long sizeBytes)
        {
            if (sizeBytes <= 0 || sizeBytes > _limits.MaximumModuleBytes)
            {
                throw new InvalidDataException(
                    "A compiler reference module exceeds the byte limit.");
            }
            if (_moduleCount >= _limits.MaximumModuleCount ||
                _closureBytes > _limits.MaximumClosureBytes - sizeBytes)
            {
                throw new InvalidDataException(
                    "The compiler reference closure exceeds its resource limit.");
            }
            _moduleCount++;
            _closureBytes += sizeBytes;
        }
    }
    private static CompilerAdditionalFileSnapshot CaptureAdditionalFile(AdditionalText file, string projectDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(projectDirectory, file.Path);
        var text = GetStableAdditionalText(file, cancellationToken);
        return new CompilerAdditionalFileSnapshot
        {
            Path = CompilerCaptureAuthority.NormalizePath(path),
            Sha256 = ComputeTextSha256(text)
        };
    }

    private static SourceText GetStableAdditionalText(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        // Analyzer tests provide an already captured immutable value. The
        // supported command-line compiler uses AdditionalTextFile, whose
        // Lazy<SourceText> is shared by generators and analyzers. Do not
        // authenticate arbitrary providers after generation has completed:
        // a later GetText call need not return the value a generator consumed.
        if (file is ICompilerAdditionalTextSnapshot snapshot)
        {
            return snapshot.CapturedText;
        }

        var providerType = file.GetType();
        if (providerType.Assembly != typeof(AdditionalText).Assembly ||
            !string.Equals(
                providerType.FullName,
                CommandLineAdditionalTextTypeName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An additional file does not expose a stable compiler input snapshot.");
        }

        return file.GetText(cancellationToken) ??
            throw new InvalidOperationException(
                "An additional file has no compiler text.");
    }

    internal static string ComputeTextSha256(SourceText text)
    {
        text = ArgumentNullGuard.NotNull(text, nameof(text));
        var value = text.ToString();
        if (!Utf16WellFormedness.IsWellFormed(value))
        {
            throw new InvalidDataException(
                "Compiler text contains ill-formed UTF-16.");
        }
        return Hash(Encoding.UTF8.GetBytes(value));
    }

    private static string Identity(MetadataReader reader)
    {
        return reader.IsAssembly
            ? reader.GetAssemblyDefinition().GetAssemblyName().FullName
            : reader.GetString(reader.GetModuleDefinition().Name);
    }

    internal static string ResolveSiblingModule(string manifestPath, string name)
    {
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            !HasSafeModuleFileName(name))
        {
            throw new InvalidDataException(
                "A linked compiler module must be a safe sibling.");
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ??
            throw new InvalidDataException(
                "A compiler reference directory is unavailable.");
        var path = Path.GetFullPath(Path.Combine(directory, name));
        if (!string.Equals(
                Path.GetDirectoryName(path),
                directory,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A linked compiler module must be a safe sibling.");
        }
        return path;
    }

    private static bool HasSafeModuleFileName(string name)
    {
        return !string.IsNullOrEmpty(name) &&
            name is not "." and not ".." &&
            name.IndexOf('\0') < 0 &&
            name.IndexOf('/') < 0;
    }

    internal static string ReadModuleName(MetadataReader reader)
    {
        return reader.GetString(reader.GetModuleDefinition().Name);
    }

    internal static unsafe bool MetadataEquals(
        MetadataReader left,
        MetadataReader right)
    {
        return left.MetadataLength == right.MetadataLength &&
            new ReadOnlySpan<byte>(left.MetadataPointer, left.MetadataLength)
                .SequenceEqual(new ReadOnlySpan<byte>(
                    right.MetadataPointer, right.MetadataLength));
    }

    private static Guid ReadMvid(MetadataReader reader)
    {
        var handle = reader.GetModuleDefinition().Mvid;
        if (handle.IsNil)
        {
            throw new InvalidDataException(
                "A compiler reference module has no MVID.");
        }
        return reader.GetGuid(handle);
    }

    private static bool HasResolverDirective(SyntaxTree tree, CancellationToken cancellationToken)
    {
        return tree.GetRoot(cancellationToken).DescendantTrivia(descendIntoTrivia: true)
                .Any(static trivia => trivia.IsKind(SyntaxKind.LoadDirectiveTrivia) || trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia));
    }

    internal static string Hash(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var hash = System.Security.Cryptography.SHA256.Create();
        var buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.TransformBlock(buffer, 0, count, buffer, 0);
        }
        hash.TransformFinalBlock([], 0, 0);
        return HashEncoding.ToLowerHex(hash.Hash!);
    }
    private static string Hash(byte[] bytes)
    {
        return HashEncoding.ComputeSha256Hex(bytes);
    }
}
