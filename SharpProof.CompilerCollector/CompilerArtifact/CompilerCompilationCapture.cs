using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// This capture runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;
#pragma warning disable RS1035 // Build-only compiler evidence must hash final reference images.

internal static class CompilerCompilationCapture
{
    internal static CompilerCompilationSnapshot Capture(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, ImmutableArray<AdditionalText> additionalFiles, CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new ArgumentException(
            "The project directory and target framework are required.");
        }

        var normalizedProject = NormalizePath(projectDirectory);
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
            CompilerVersion = Version(typeof(Compilation)),
            CompilerMvid = Mvid(typeof(Compilation)),
            CSharpCompilerVersion = Version(typeof(CSharpCompilation)),
            CSharpCompilerMvid = Mvid(typeof(CSharpCompilation)),
            Options = new CompilerCompilationOptionsSnapshot
            {
                OutputKind = CompilerOptionWireMappings.Map(options.OutputKind),
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
                CheckOverflow = options.CheckOverflow,
                AllowUnsafe = options.AllowUnsafe,
                Deterministic = options.Deterministic,
                ReferencesSupersedeLowerVersions = supersedes,
                AssemblyIdentityComparer = CompilerOptionWireMappings.Map(options.AssemblyIdentityComparer),
                Usings = options.Usings.ToArray(),
                ResolverPolicy = CompilerResolverPolicy.EvidenceOnly
            },
            SyntaxTrees = [.. compilation.SyntaxTrees.Select(tree => CaptureTree(tree, cancellationToken))],
            References = [.. compilation.References.Select(reference => CaptureReference(reference, cancellationToken))],
            AdditionalFiles = [.. additionalFiles.Select(file => CaptureAdditionalFile(
                file, normalizedProject, cancellationToken)).OrderBy(static file => file.Path, StringComparer.Ordinal)
                .ThenBy(static file => file.Sha256, StringComparer.Ordinal)]
        };
    }
    private static CompilerSyntaxTreeSnapshot CaptureTree(SyntaxTree tree, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parse = (CSharpParseOptions)tree.Options;
        var text = tree.GetText(cancellationToken);
        return new CompilerSyntaxTreeSnapshot
        {
            Path = tree.FilePath ?? string.Empty,
            Sha256 = ComputeTextSha256(text),
            TextLength = text.Length,
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
    }
    private static CompilerReferenceSnapshot CaptureReference(MetadataReference reference,
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
                Path = NormalizePath(modulePath),
                Sha256 = Hash(stream, cancellationToken)
            });
        }
        return new CompilerReferenceSnapshot
        {
            Kind = reference.Properties.Kind.ToString(),
            EmbedInteropTypes = reference.Properties.EmbedInteropTypes,
            Aliases = [.. reference.Properties.Aliases.OrderBy(static value => value, StringComparer.Ordinal)],
            Identity = identity ?? throw new InvalidDataException(
                "A compiler reference contains no modules."),
            Modules = modules.ToArray()
        };
    }
    private static CompilerAdditionalFileSnapshot CaptureAdditionalFile(AdditionalText file, string projectDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(projectDirectory, file.Path);
        var text = file.GetText(cancellationToken) ??
            throw new InvalidOperationException("An additional file has no compiler text.");
        return new CompilerAdditionalFileSnapshot
        {
            Path = NormalizePath(path),
            Sha256 = ComputeTextSha256(text)
        };
    }

    internal static string ComputeTextSha256(SourceText text)
    {
        text = ArgumentNullGuard.NotNull(text, nameof(text));

        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static string Identity(MetadataReader reader)
    {
        return reader.IsAssembly
            ? reader.GetAssemblyDefinition().GetAssemblyName().FullName
            : reader.GetString(reader.GetModuleDefinition().Name);
    }

    internal static string ResolveSiblingModule(string manifestPath, string name)
    {
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
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
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A linked compiler module must be a safe sibling.");
        }
        return path;
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

    private static string Version(Type type)
    {
        return type.Assembly.GetName().Version?.ToString() ??
            throw new InvalidOperationException("The compiler version is unavailable.");
    }

    private static string Mvid(Type type)
    {
        return type.Module.ModuleVersionId.ToString("D");
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? fullPath.Replace('\\', '/')
            : fullPath;
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
        return string.Concat(hash.Hash!.Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }
    private static string Hash(byte[] bytes)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        return string.Concat(hash.ComputeHash(bytes).Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
