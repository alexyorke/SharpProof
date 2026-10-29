using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpProof.CompilerSupport;
// This capture runs only in the build-time compiler collector.
namespace SharpProof.CompilerCollector;

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
            int capturedTreeOrdinal,
            SourceText? capturedText,
            CancellationToken cancellationToken)
        {
            compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
            var seenPaths = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            Trees = [.. compilation.SyntaxTrees.Select((tree, index) =>
            {
                tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
                var snapshot = CaptureTree(
                    tree,
                    cancellationToken,
                    index == capturedTreeOrdinal ? capturedText : null);
                // Roslyn permits generated/in-memory trees without a path and
                // multiple trees sharing one path. Give each tree a stable
                // compilation-local identity instead of rejecting the input.
                if (string.IsNullOrEmpty(tree.FilePath))
                {
                    snapshot = snapshot with { Path = $"<compiler-generated:{index}>" };
                }
                // Different raw spellings can normalize to the same path.
                // A real filename can also occupy a generated collision suffix.
                while (!seenPaths.Add(snapshot.Path))
                {
                    snapshot = snapshot with { Path = $"{snapshot.Path}#{index}" };
                }

                return snapshot;
            })];
        }

        internal ImmutableList<CompilerSyntaxTreeSnapshot> Trees { get; }
    }

    private sealed class ResolverDirectiveCache
    {
        internal ResolverDirectiveCache(
            CSharpCompilation compilation,
            CancellationToken cancellationToken)
        {
            compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
            HasDirective = compilation.SyntaxTrees.Any(
                tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (HasResolverDirective(tree, cancellationToken)));
        }

        internal bool HasDirective { get; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CSharpCompilation, SyntaxTreeCache>
        SyntaxTreeCaches = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CSharpCompilation, ResolverDirectiveCache>
        ResolverDirectiveCaches = new();

    // GetMetadata returns a new copy on every call, but every copy reads through
    // the one MetadataReader of the image the compiler loaded. Compilations that
    // share a reference therefore share that reader, and later captures of an
    // unchanged file reuse its evidence instead of re-reading, re-matching and
    // re-hashing it. A different path, length or write time takes the full
    // capture again.
    private sealed class CapturedModuleFile
    {
        internal CapturedModuleFile(
            FileInfo file,
            bool isAssembly,
            CompilerReferenceModuleSnapshot snapshot,
            string? identity)
        {
            file = RepositoryParameterGuard.NotNull(file, nameof(file));
            snapshot = RepositoryParameterGuard.NotNull(snapshot, nameof(snapshot));
            FullPath = file.FullName;
            SizeBytes = file.Length;
            LastWriteTimeUtc = file.LastWriteTimeUtc;
            IsAssembly = isAssembly;
            Snapshot = snapshot;
            Identity = identity;
        }

        private string FullPath { get; }
        private long SizeBytes { get; }
        private DateTime LastWriteTimeUtc { get; }
        private bool IsAssembly { get; }
        internal CompilerReferenceModuleSnapshot Snapshot { get; }
        internal string? Identity { get; }

        internal bool Describes(FileInfo file, bool expectAssembly)
        {
            file = RepositoryParameterGuard.NotNull(file, nameof(file));
            return IsAssembly == expectAssembly &&
                file.Exists &&
                string.Equals(file.FullName, FullPath, StringComparison.Ordinal) &&
                file.Length == SizeBytes &&
                file.LastWriteTimeUtc == LastWriteTimeUtc;
        }
    }

    private sealed class CapturedModuleFileSlot
    {
        private volatile CapturedModuleFile? _latest;

        internal CapturedModuleFile? Latest
        {
            get => _latest;
            set => _latest = value;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetadataReader, CapturedModuleFileSlot>
        CapturedModuleFiles = new();

    internal static ImmutableList<CompilerSyntaxTreeSnapshot> CaptureTrees(
        CSharpCompilation compilation,
        CancellationToken cancellationToken,
        int capturedTreeOrdinal = -1,
        SourceText? capturedText = null)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        return SyntaxTreeCaches.GetValue(
            compilation,
            value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new SyntaxTreeCache(
                value,
                capturedTreeOrdinal,
                capturedText,
                cancellationToken))).Trees;
    }

    internal static bool TryGetCapturedTreeTextLength(
        CSharpCompilation compilation,
        int treeOrdinal,
        out int textLength)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        if (SyntaxTreeCaches.TryGetValue(compilation, out var cache) &&
            (uint)treeOrdinal < (uint)cache.Trees.Count)
        {
            textLength = cache.Trees[treeOrdinal].TextLength;
            return true;
        }

        textLength = 0;
        return false;
    }

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

        var normalizedProject = CompilerCaptureIdentity.NormalizePath(
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
            ResolverDirectiveCaches.GetValue(
                compilation,
                value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new ResolverDirectiveCache(value, cancellationToken))).HasDirective)
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
            CompilerVersion = CompilerCaptureIdentity.CaptureVersion(
                typeof(Compilation)),
            CompilerMvid = CompilerCaptureIdentity.CaptureMvid(
                typeof(Compilation)),
            CSharpCompilerVersion = CompilerCaptureIdentity.CaptureVersion(
                typeof(CSharpCompilation)),
            CSharpCompilerMvid = CompilerCaptureIdentity.CaptureMvid(
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
                Usings = options.Usings,
                ResolverPolicy = CompilerResolverPolicy.EvidenceOnly
            },
            SyntaxTrees = CaptureTrees(compilation, cancellationToken).ToImmutableArray(),
            References = CaptureReferences(
                compilation.References,
                ReferenceCaptureLimits.Default,
                cancellationToken),
            AdditionalFiles = [.. additionalFiles.Select(file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (CaptureAdditionalFile(
                file, normalizedProject, cancellationToken))).OrderBy(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file.Path), StringComparer.Ordinal)
                .ThenBy(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file.Sha256), StringComparer.Ordinal)]
        };
    }
    internal static CompilerSyntaxTreeSnapshot CaptureTree(
        SyntaxTree tree,
        CancellationToken cancellationToken,
        SourceText? capturedText = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parse = tree.Options as CSharpParseOptions ??
            throw new InvalidDataException("A compiler syntax tree has no C# parse options.");
        var text = capturedText ?? tree.GetText(cancellationToken);
        var characterOffsets = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var mapping in tree.GetLineMappings(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mapping.CharacterOffset is { } characterOffset)
            {
                characterOffsets[mapping.Span.Start.Line] = characterOffset;
            }
        }
        ImmutableArray<CompilerSourceLineMapEntry> lineMap = [.. text.Lines.Select((line, lineIndex) =>
        {
RepositoryParameterGuard.RequireInitialized(line.Equals(default(Microsoft.CodeAnalysis.Text.TextLine)), nameof(line), "Value must be initialized.");

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
            Path = CompilerCaptureIdentity.NormalizePath(
                string.IsNullOrEmpty(tree.FilePath)
                    ? "<compiler-generated>"
                    : tree.FilePath),
            Sha256 = ComputeTextSha256(text),
            Encoding = text.Encoding?.WebName ?? string.Empty,
            ChecksumAlgorithm = text.ChecksumAlgorithm.ToString(),
            RoslynChecksum = HashEncoding.ToLowerHex(text.GetChecksum()),
            LineMapSha256 = CompilerReportingIdentity.ComputeLineMapSha256(lineMap),
            TextLength = text.Length,
            LineMap = lineMap,
            LanguageVersion = parse.LanguageVersion.ToString(),
            DocumentationMode = parse.DocumentationMode.ToString(),
            Kind = parse.Kind.ToString(),
            PreprocessorSymbols = [.. parse.PreprocessorSymbolNames.OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)],
            EffectivePreprocessorSymbols = [
                .. CSharpPreprocessorSymbols.GetDefined(tree, cancellationToken)
                    .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)
            ],
            Features = [.. parse.Features.OrderBy(static value => value.Key, StringComparer.Ordinal)
                .Select(static value => new CompilerFeatureSnapshot { Key = value.Key, Value = value.Value })]
        };

    }

    private static string MappedPath(
        SyntaxTree tree,
        FileLinePositionSpan mapped /* SPINT001: all values valid - default or missing mapped paths fall back to the source tree path */)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        return CompilerSourceLocationProjection.MappedPath(tree, mapped);
    }
    internal static ImmutableArray<CompilerReferenceSnapshot> CaptureReferences(
        IEnumerable<MetadataReference> references,
        ReferenceCaptureLimits limits,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(limits.MaximumModuleBytes <= 0 || limits.MaximumClosureBytes < limits.MaximumModuleBytes || limits.MaximumModuleCount <= 0, nameof(limits), "Value must be initialized.");

        references = ArgumentNullGuard.NotNull(
            references,
            nameof(references));
        var budget = new ReferenceCaptureBudget(limits);
        var snapshots = ImmutableArray.CreateBuilder<CompilerReferenceSnapshot>();
        foreach (var reference in references)
        {
            var captured = CaptureReference(
                ArgumentNullGuard.NotNull(reference, nameof(reference)), budget, cancellationToken);
            snapshots.Add(captured.Snapshot);
            budget = captured.Budget;
        }
        return snapshots.ToImmutable();
    }

    private static (CompilerReferenceSnapshot Snapshot, ReferenceCaptureBudget Budget) CaptureReference(
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
        var backingModules = backingMetadata is AssemblyMetadata assembly
            ? assembly.GetModules()
            : ImmutableArray.Create((ModuleMetadata)backingMetadata);
        if (backingModules.Length > budget.MaximumModuleCount)
        {
            throw new InvalidDataException(
                "A compiler reference exceeds the module count limit.");
        }
        var moduleEntries = backingModules
            .Select(static module => module is null ? throw new System.ArgumentNullException(nameof(module)) : ((
                Module: module,
                Name: ReadModuleName(module.GetMetadataReader()))))
            .ToImmutableArray();
        if (moduleEntries.Length > 1)
        {
            moduleEntries = [
                moduleEntries[0],
                .. moduleEntries.Skip(1).OrderBy(
                    static module => module.Name,
                    StringComparer.Ordinal)
            ];
        }
        var modules = ImmutableArray.CreateBuilder<CompilerReferenceModuleSnapshot>(moduleEntries.Length);
        string? identity = null;
        for (var index = 0; index < moduleEntries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backingModule = moduleEntries[index].Module;
            var backingReader = backingModule.GetMetadataReader();
            var backingName = moduleEntries[index].Name;
            var modulePath = index == 0
                ? Path.GetFullPath(path)
                : ResolveSiblingModule(path, backingName);
            // Read the file's length and write time before its bytes, so a
            // write during the capture leaves a stale key that never matches.
            var file = new FileInfo(modulePath);
            file.Refresh();
            var slot = CapturedModuleFiles.GetValue(
                backingReader,
                static _ => new CapturedModuleFileSlot());
            if (slot.Latest is { } captured && captured.Describes(
                    file,
                    index == 0 && reference.Properties.Kind == MetadataImageKind.Assembly))
            {
                budget = budget.Consume(captured.Snapshot.SizeBytes);
                if (index == 0)
                {
                    identity = captured.Identity;
                }
                modules.Add(captured.Snapshot);
                continue;
            }
            using var stream = new FileStream(
                modulePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var sizeBytes = stream.Length;
            budget = budget.Consume(sizeBytes);
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
            var fileIdentity = CompilerReferenceModuleEvidence.Match(
                new CompilerReferenceModuleEvidence(backingReader, backingName, backingReader.GetModuleDefinition().Mvid),
                CompilerReferenceModuleEvidence.Capture(fileReader));
            var moduleIdentity = index == 0 ? Identity(fileReader) : null;
            if (index == 0)
            {
                identity = moduleIdentity;
            }
            var snapshot = new CompilerReferenceModuleSnapshot
            {
                Name = fileIdentity.Name,
                Mvid = fileIdentity.Mvid.ToString("D"),
                Path = CompilerCaptureIdentity.NormalizePath(modulePath),
                Sha256 = Hash(stream, cancellationToken),
                SizeBytes = sizeBytes
            };
            modules.Add(snapshot);
            if (file.Exists && file.Length == sizeBytes)
            {
                slot.Latest = new CapturedModuleFile(file, fileReader.IsAssembly, snapshot, moduleIdentity);
            }
        }
        return (new CompilerReferenceSnapshot
        {
            Kind = reference.Properties.Kind.ToString(),
            EmbedInteropTypes = reference.Properties.EmbedInteropTypes,
            HasRecursiveAliases = CompilerOptionWireMappings.ReadInternalBoolean(
                reference.Properties,
                "HasRecursiveAliases"),
            Aliases = [.. reference.Properties.Aliases.OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal)],
            Identity = identity ?? throw new InvalidDataException(
                "A compiler reference contains no modules."),
            Modules = modules.ToImmutable()
        }, budget);
    }

    private sealed record ReferenceCaptureBudget
    {
        private readonly ReferenceCaptureLimits _limits;
        private long ClosureBytes { get; init; }
        private int ModuleCount { get; init; }

        internal ReferenceCaptureBudget(ReferenceCaptureLimits limits)
        {
            RepositoryParameterGuard.RequireInitialized(limits.MaximumModuleBytes <= 0 || limits.MaximumClosureBytes < limits.MaximumModuleBytes || limits.MaximumModuleCount <= 0, nameof(limits), "Value must be initialized.");

            _limits = limits;
        }

        internal int MaximumModuleCount => _limits.MaximumModuleCount;

        internal ReferenceCaptureBudget Consume(long sizeBytes)
        {
            if (sizeBytes <= 0 || sizeBytes > _limits.MaximumModuleBytes)
            {
                throw new InvalidDataException(
                    "A compiler reference module exceeds the byte limit.");
            }
            if (ModuleCount >= _limits.MaximumModuleCount ||
                ClosureBytes > _limits.MaximumClosureBytes - sizeBytes)
            {
                throw new InvalidDataException(
                    "The compiler reference closure exceeds its resource limit.");
            }
            return this with { ModuleCount = ModuleCount + 1, ClosureBytes = ClosureBytes + sizeBytes };
        }
    }
    private static CompilerAdditionalFileSnapshot CaptureAdditionalFile(AdditionalText file, string projectDirectory,
        CancellationToken cancellationToken)
    {
        projectDirectory = RepositoryParameterGuard.NotNull(projectDirectory, nameof(projectDirectory));
        cancellationToken.ThrowIfCancellationRequested();
        file = ArgumentNullGuard.NotNull(file, nameof(file));
        var path = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(projectDirectory, file.Path);
        var text = GetStableAdditionalText(file, cancellationToken);
        return new CompilerAdditionalFileSnapshot
        {
            Path = CompilerCaptureIdentity.NormalizePath(path),
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

        return CompilerAdditionalTextStability.GetStableAdditionalText(
            file,
            cancellationToken);
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
        return HashEncoding.ComputeSha256Hex((Encoding.UTF8.GetBytes(value)).ToImmutableArray());
    }

    private static string Identity(MetadataReader reader)
    {
        reader = RepositoryParameterGuard.NotNull(reader, nameof(reader));
        return reader.IsAssembly
            ? reader.GetAssemblyDefinition().GetAssemblyName().FullName
            : reader.GetString(reader.GetModuleDefinition().Name);
    }

    internal static string ResolveSiblingModule(string manifestPath, string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        manifestPath = RepositoryParameterGuard.NotNull(manifestPath, nameof(manifestPath));
        return CompilerReferenceSiblingLocation.Resolve(manifestPath, name);
    }

    internal static string ReadModuleName(MetadataReader reader)
    {
        reader = RepositoryParameterGuard.NotNull(reader, nameof(reader));
        return reader.GetString(reader.GetModuleDefinition().Name);
    }

    internal static unsafe bool MetadataEquals(
        MetadataReader left,
        MetadataReader right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return left.MetadataLength == right.MetadataLength &&
            new ReadOnlySpan<byte>(left.MetadataPointer, left.MetadataLength)
                .SequenceEqual(new ReadOnlySpan<byte>(
                    right.MetadataPointer, right.MetadataLength));
    }

    private static bool HasResolverDirective(SyntaxTree tree, CancellationToken cancellationToken)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        return tree.GetRoot(cancellationToken).DescendantTrivia(descendIntoTrivia: true)
                .Any(static trivia => trivia.IsKind(SyntaxKind.LoadDirectiveTrivia) || trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia));
    }

    internal static string Hash(Stream stream, CancellationToken cancellationToken)
    {
        stream = RepositoryParameterGuard.NotNull(stream, nameof(stream));
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
        return HashEncoding.ToLowerHex(
            hash.Hash ?? throw new InvalidOperationException("The SHA-256 digest is unavailable."));
    }
}
