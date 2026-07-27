using System.Text;
namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact compiler capture preserves the fixed production-size ceiling.
#pragma warning disable RS1035 // Build-only compiler evidence must hash final reference images.

internal static class CompilerCompilationCapture {
    internal static CompilerCompilationSnapshot Capture(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, ImmutableArray<AdditionalText> additionalFiles, CancellationToken cancellationToken) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(targetFramework)) throw new ArgumentException(
            "The project directory and target framework are required.");
        var normalizedProject = NormalizePath(projectDirectory); var options = compilation.Options;
        if (additionalFiles.IsDefault) additionalFiles = [];
        var supersedes = InternalBoolean(options, "ReferencesSupersedeLowerVersions");
        if (supersedes || options.MetadataReferenceResolver?.ResolveMissingAssemblies == true ||
            compilation.SyntaxTrees.Any(tree => HasResolverDirective(tree, cancellationToken))) throw new InvalidOperationException(
            "Reference supersession and resolver directives are unsupported.");
        return new CompilerCompilationSnapshot {
            ProjectDirectory = normalizedProject,
            AssemblyName = compilation.AssemblyName ?? throw new InvalidOperationException("The assembly name is unavailable."),
            AssemblyIdentity = compilation.Assembly.Identity.ToString(), TargetFramework = targetFramework,
            CompilerVersion = Version(typeof(Compilation)), CompilerMvid = Mvid(typeof(Compilation)),
            CSharpCompilerVersion = Version(typeof(CSharpCompilation)), CSharpCompilerMvid = Mvid(typeof(CSharpCompilation)),
            Options = new CompilerCompilationOptionsSnapshot {
                OutputKind = options.OutputKind.ToString(), OptimizationLevel = options.OptimizationLevel.ToString(),
                Platform = options.Platform.ToString(), NullableContext = options.NullableContextOptions.ToString(),
                MetadataImportOptions = options.MetadataImportOptions.ToString(), CheckOverflow = options.CheckOverflow,
                AllowUnsafe = options.AllowUnsafe, Deterministic = options.Deterministic,
                ReferencesSupersedeLowerVersions = supersedes, AssemblyIdentityComparer = Comparer(options.AssemblyIdentityComparer),
                Usings = [.. options.Usings], ResolverPolicy = "EvidenceOnly"
            },
            SyntaxTrees = [.. compilation.SyntaxTrees.Select(tree => CaptureTree(tree, cancellationToken))],
            References = [.. compilation.References.Select(reference => CaptureReference(compilation, reference, cancellationToken))],
            AdditionalFiles = [.. additionalFiles.Select(file => CaptureAdditionalFile(
                file, normalizedProject, cancellationToken)).OrderBy(static file => file.Path, StringComparer.Ordinal)
                .ThenBy(static file => file.Sha256, StringComparer.Ordinal)]
        };
    }
    private static CompilerSyntaxTreeSnapshot CaptureTree(SyntaxTree tree, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested(); var parse = (CSharpParseOptions)tree.Options;
        return new CompilerSyntaxTreeSnapshot {
            Path = tree.FilePath ?? string.Empty,
            Sha256 = Hash(Encoding.UTF8.GetBytes(tree.GetText(cancellationToken).ToString())),
            LanguageVersion = parse.LanguageVersion.ToString(), DocumentationMode = parse.DocumentationMode.ToString(),
            Kind = parse.Kind.ToString(),
            PreprocessorSymbols = [.. parse.PreprocessorSymbolNames.OrderBy(static value => value, StringComparer.Ordinal)],
            Features = [.. parse.Features.OrderBy(static value => value.Key, StringComparer.Ordinal)
                .Select(static value => new CompilerFeatureSnapshot { Key = value.Key, Value = value.Value })]
        };
    }
    private static CompilerReferenceSnapshot CaptureReference(CSharpCompilation compilation, MetadataReference reference,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var portable = reference as PortableExecutableReference ??
            throw new InvalidOperationException("The final compilation contains a non-file reference.");
        var path = portable.FilePath ?? portable.Display ??
            throw new InvalidOperationException("The final compilation contains an unnamed reference.");
        return new CompilerReferenceSnapshot {
            Path = NormalizePath(path), Kind = reference.Properties.Kind.ToString(),
            EmbedInteropTypes = reference.Properties.EmbedInteropTypes,
            Aliases = [.. reference.Properties.Aliases.OrderBy(static value => value, StringComparer.Ordinal)],
            Identity = Identity(compilation, reference), Sha256 = Hash(ReadImage(path, cancellationToken))
        };
    }
    private static CompilerAdditionalFileSnapshot CaptureAdditionalFile(AdditionalText file, string projectDirectory,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(projectDirectory, file.Path);
        var text = file.GetText(cancellationToken) ??
            throw new InvalidOperationException("An additional file has no compiler text.");
        return new CompilerAdditionalFileSnapshot {
            Path = NormalizePath(path), Sha256 = Hash(Encoding.UTF8.GetBytes(text.ToString()))
        };
    }
    private static string Identity(CSharpCompilation compilation, MetadataReference reference) =>
        compilation.GetAssemblyOrModuleSymbol(reference) switch {
            IAssemblySymbol assembly => assembly.Identity.ToString(), IModuleSymbol module => module.Name,
            _ => ((PortableExecutableReference)reference).GetMetadata() switch {
                AssemblyMetadata assembly => assembly.GetModules()[0].GetMetadataReader().GetAssemblyDefinition().GetAssemblyName().FullName,
                ModuleMetadata module => module.Name,
                _ => throw new InvalidDataException("A compiler reference identity is unavailable.")
            }
        };
    private static bool HasResolverDirective(SyntaxTree tree, CancellationToken cancellationToken) =>
        tree.GetRoot(cancellationToken).DescendantTrivia(descendIntoTrivia: true)
            .Any(static trivia => trivia.IsKind(SyntaxKind.LoadDirectiveTrivia) || trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia));
    private static string Comparer(AssemblyIdentityComparer value) => ReferenceEquals(value, AssemblyIdentityComparer.Default) ? "Default" :
        ReferenceEquals(value, DesktopAssemblyIdentityComparer.Default) ? "Desktop" :
        throw new InvalidOperationException("A custom assembly identity comparer is unsupported.");
    private static bool InternalBoolean(CSharpCompilationOptions options, string name) => (bool)(typeof(CompilationOptions).GetField(
        $"<{name}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(options) ??
        throw new InvalidOperationException($"The compiler option '{name}' is unavailable."));
    private static string Version(Type type) => type.Assembly.GetName().Version?.ToString() ??
        throw new InvalidOperationException("The compiler version is unavailable.");
    private static string Mvid(Type type) => type.Module.ModuleVersionId.ToString("D");
    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/');
    private static byte[] ReadImage(string path, CancellationToken cancellationToken) {
        using var input = File.OpenRead(path);
        using var output = new MemoryStream(input.Length <= int.MaxValue ? (int)input.Length : 0);
        var buffer = new byte[81920]; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
            cancellationToken.ThrowIfCancellationRequested(); output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static string Hash(byte[] bytes) {
        using var hash = System.Security.Cryptography.SHA256.Create();
        return string.Concat(hash.ComputeHash(bytes).Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
