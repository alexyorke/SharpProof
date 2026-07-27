using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.Text;
namespace SharpProof.CompilerArtifact;
internal sealed class CompilerCompilationSnapshot {
    public string ProjectDirectory { get; set; } = string.Empty; public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyIdentity { get; set; } = string.Empty; public string TargetFramework { get; set; } = string.Empty;
    public string CompilerVersion { get; set; } = string.Empty; public string CompilerMvid { get; set; } = string.Empty;
    public string CSharpCompilerVersion { get; set; } = string.Empty; public string CSharpCompilerMvid { get; set; } = string.Empty;
    public CompilerCompilationOptionsSnapshot Options { get; set; } = new();
    public CompilerSyntaxTreeSnapshot[] SyntaxTrees { get; set; } = []; public CompilerReferenceSnapshot[] References { get; set; } = [];
}
internal sealed class CompilerCompilationOptionsSnapshot {
    public string OutputKind { get; set; } = string.Empty; public string OptimizationLevel { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty; public string NullableContext { get; set; } = string.Empty;
    public string MetadataImportOptions { get; set; } = string.Empty;
    public bool CheckOverflow { get; set; }
    public bool AllowUnsafe { get; set; }
    public bool Deterministic { get; set; }
    public bool ReferencesSupersedeLowerVersions { get; set; }
    public string AssemblyIdentityComparer { get; set; } = string.Empty;
    public string[] Usings { get; set; } = [];
    public string ResolverPolicy { get; set; } = string.Empty;
}
internal sealed class CompilerSyntaxTreeSnapshot {
    public string Path { get; set; } = string.Empty; public string Text { get; set; } = string.Empty;
    public string LanguageVersion { get; set; } = string.Empty; public string DocumentationMode { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string[] PreprocessorSymbols { get; set; } = []; public CompilerFeatureSnapshot[] Features { get; set; } = [];
}
internal sealed class CompilerReferenceSnapshot {
    public string Path { get; set; } = string.Empty; public string Kind { get; set; } = string.Empty;
    public bool EmbedInteropTypes { get; set; }
    public string[] Aliases { get; set; } = [];
    public string Identity { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty;
}
internal sealed class CompilerFeatureSnapshot {
    public string Key { get; set; } = string.Empty; public string Value { get; set; } = string.Empty;
}
internal static class CompilationFingerprint {
    internal static string CurrentCompilerVersion => Version(typeof(Compilation)); internal static string CurrentCompilerMvid => Mvid(typeof(Compilation));
    internal static string CurrentCSharpCompilerVersion => Version(typeof(CSharpCompilation));
    internal static string CurrentCSharpCompilerMvid => Mvid(typeof(CSharpCompilation));

    internal static CompilerCompilationSnapshot Capture(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, CancellationToken cancellationToken) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(targetFramework)) throw new ArgumentException(
            "The project directory and target framework are required.");
        var options = compilation.Options;
        var supersedes = InternalBoolean(options, "ReferencesSupersedeLowerVersions");
        if (supersedes || options.MetadataReferenceResolver?.ResolveMissingAssemblies == true ||
            compilation.SyntaxTrees.Any(tree => HasResolverDirective(tree, cancellationToken))) throw new InvalidOperationException(
            "Reference supersession and resolver directives are unsupported.");
        var snapshot = new CompilerCompilationSnapshot();
        snapshot.ProjectDirectory = NormalizePath(projectDirectory);
        snapshot.AssemblyName = compilation.AssemblyName ?? throw new InvalidOperationException("The assembly name is unavailable.");
        snapshot.AssemblyIdentity = compilation.Assembly.Identity.ToString(); snapshot.TargetFramework = targetFramework;
        snapshot.CompilerVersion = CurrentCompilerVersion; snapshot.CompilerMvid = CurrentCompilerMvid;
        snapshot.CSharpCompilerVersion = CurrentCSharpCompilerVersion; snapshot.CSharpCompilerMvid = CurrentCSharpCompilerMvid;
        var saved = snapshot.Options;
        saved.OutputKind = options.OutputKind.ToString(); saved.OptimizationLevel = options.OptimizationLevel.ToString();
        saved.Platform = options.Platform.ToString(); saved.NullableContext = options.NullableContextOptions.ToString();
        saved.MetadataImportOptions = options.MetadataImportOptions.ToString(); saved.CheckOverflow = options.CheckOverflow;
        saved.AllowUnsafe = options.AllowUnsafe; saved.Deterministic = options.Deterministic;
        saved.ReferencesSupersedeLowerVersions = supersedes; saved.AssemblyIdentityComparer = Comparer(options.AssemblyIdentityComparer);
        saved.Usings = [.. options.Usings]; saved.ResolverPolicy = "Materialized";
        snapshot.SyntaxTrees = [.. compilation.SyntaxTrees.Select(tree => CaptureTree(tree, cancellationToken))];
        snapshot.References = [.. compilation.References.Select(reference => CaptureReference(compilation, reference, cancellationToken))];
        return snapshot;
    }
    internal static CSharpCompilation Reconstruct(CompilerCompilationSnapshot snapshot, CancellationToken cancellationToken) {
        ValidateShape(snapshot); var trees = snapshot.SyntaxTrees.Select(tree => {
            cancellationToken.ThrowIfCancellationRequested();
            var parse = new CSharpParseOptions(Parse<LanguageVersion>(tree.LanguageVersion),
                Parse<DocumentationMode>(tree.DocumentationMode), Parse<SourceCodeKind>(tree.Kind), tree.PreprocessorSymbols)
                .WithFeatures(tree.Features.Select(static feature => new KeyValuePair<string, string>(feature.Key, feature.Value)));
            return CSharpSyntaxTree.ParseText(SourceText.From(tree.Text, Encoding.UTF8, SourceHashAlgorithm.Sha256), parse, tree.Path, cancellationToken);
        }).ToArray();
        var references = snapshot.References.Select(reference => {
            cancellationToken.ThrowIfCancellationRequested(); var image = ReadImage(reference.Path, cancellationToken);
            if (Hash(image) != reference.Sha256) throw new InvalidDataException("A compiler reference no longer matches its snapshot.");
            return MetadataReference.CreateFromImage(
                ImmutableArray.Create(image), new MetadataReferenceProperties(
                    Parse<MetadataImageKind>(reference.Kind), [.. reference.Aliases], reference.EmbedInteropTypes), filePath: reference.Path);
        }).ToArray();
        var options = snapshot.Options;
        var compilation = CSharpCompilation.Create(
            snapshot.AssemblyName, trees, references, new CSharpCompilationOptions(
                Parse<OutputKind>(options.OutputKind), usings: options.Usings,
                optimizationLevel: Parse<OptimizationLevel>(options.OptimizationLevel),
                checkOverflow: options.CheckOverflow, allowUnsafe: options.AllowUnsafe,
                platform: Parse<Platform>(options.Platform), concurrentBuild: false, deterministic: options.Deterministic,
                assemblyIdentityComparer: ParseComparer(options.AssemblyIdentityComparer),
                metadataImportOptions: Parse<MetadataImportOptions>(options.MetadataImportOptions),
                nullableContextOptions: Parse<NullableContextOptions>(options.NullableContext)));
        if (InternalBoolean(compilation.Options, "ReferencesSupersedeLowerVersions") != options.ReferencesSupersedeLowerVersions)
            throw new InvalidDataException("The compilation options cannot be reconstructed exactly.");
        ValidateIdentities(compilation, snapshot); return compilation;
    }
    internal static string ComputeSha256(CompilerCompilationSnapshot snapshot) {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilerCompilationSnapshot", 2, JsonSerializer.Serialize(snapshot, WorkerProtocolJson.Options)); return hash.Finish();
    }
    internal static void ValidateShape(CompilerCompilationSnapshot snapshot) {
        if (snapshot == null || !Path.IsPathRooted(snapshot.ProjectDirectory) || string.IsNullOrWhiteSpace(snapshot.AssemblyName) || string.IsNullOrWhiteSpace(snapshot.AssemblyIdentity) || string.IsNullOrWhiteSpace(snapshot.TargetFramework) ||
            string.IsNullOrWhiteSpace(snapshot.CompilerVersion) || !Guid.TryParseExact(snapshot.CompilerMvid, "D", out _) ||
            string.IsNullOrWhiteSpace(snapshot.CSharpCompilerVersion) || !Guid.TryParseExact(snapshot.CSharpCompilerMvid, "D", out _) ||
            !ValidOptions(snapshot.Options) || snapshot.SyntaxTrees == null || snapshot.SyntaxTrees.Any(static tree => !ValidTree(tree)) ||
            snapshot.References == null || snapshot.References.Any(static reference => !ValidReference(reference))) throw new JsonException(
            "The compiler compilation snapshot is invalid.");
    }
    private static CompilerSyntaxTreeSnapshot CaptureTree(SyntaxTree tree, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var parse = (CSharpParseOptions)tree.Options;
        var snapshot = new CompilerSyntaxTreeSnapshot();
        snapshot.Path = tree.FilePath ?? string.Empty; snapshot.Text = tree.GetText(cancellationToken).ToString();
        snapshot.LanguageVersion = parse.LanguageVersion.ToString(); snapshot.DocumentationMode = parse.DocumentationMode.ToString();
        snapshot.Kind = parse.Kind.ToString();
        snapshot.PreprocessorSymbols = [.. parse.PreprocessorSymbolNames.OrderBy(static value => value, StringComparer.Ordinal)];
        snapshot.Features = [.. parse.Features.OrderBy(static value => value.Key, StringComparer.Ordinal)
            .Select(static value => new CompilerFeatureSnapshot { Key = value.Key, Value = value.Value })];
        return snapshot;
    }
    private static CompilerReferenceSnapshot CaptureReference(CSharpCompilation compilation, MetadataReference reference,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var portable = reference as PortableExecutableReference ??
            throw new InvalidOperationException("The final compilation contains a non-file reference.");
        var path = portable.FilePath ?? portable.Display ?? throw new InvalidOperationException("The final compilation contains an unnamed reference.");
        var snapshot = new CompilerReferenceSnapshot();
        snapshot.Path = NormalizePath(path); snapshot.Kind = reference.Properties.Kind.ToString();
        snapshot.EmbedInteropTypes = reference.Properties.EmbedInteropTypes;
        snapshot.Aliases = [.. reference.Properties.Aliases.OrderBy(static value => value, StringComparer.Ordinal)];
        snapshot.Identity = Identity(compilation, reference); snapshot.Sha256 = Hash(ReadImage(path, cancellationToken));
        return snapshot;
    }
    private static void ValidateIdentities(CSharpCompilation compilation, CompilerCompilationSnapshot snapshot) {
        if (compilation.Assembly.Identity.ToString() != snapshot.AssemblyIdentity) throw new InvalidDataException(
            "The assembly identity does not match its snapshot.");
        for (var index = 0; index < snapshot.References.Length; index++)
            if (Identity(compilation, compilation.References.ElementAt(index)) != snapshot.References[index].Identity) throw new InvalidDataException(
                "A reference identity does not match its snapshot.");
    }
    private static string Identity(CSharpCompilation compilation, MetadataReference reference) =>
        compilation.GetAssemblyOrModuleSymbol(reference) switch {
            IAssemblySymbol assembly => assembly.Identity.ToString(),
            IModuleSymbol module => module.Name,
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
    private static AssemblyIdentityComparer ParseComparer(string value) => value == "Default" ? AssemblyIdentityComparer.Default :
        value == "Desktop" ? DesktopAssemblyIdentityComparer.Default : throw new InvalidDataException("The assembly identity comparer is invalid.");
    private static bool InternalBoolean(CSharpCompilationOptions options, string name) => (bool)(typeof(CompilationOptions).GetField(
        $"<{name}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(options) ??
        throw new InvalidOperationException($"The compiler option '{name}' is unavailable."));
    private static bool Defined<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(typeof(T), parsed);
    private static bool ValidFeatures(CompilerFeatureSnapshot[]? values) => values != null && values.All(
        static value => value != null && !string.IsNullOrWhiteSpace(value.Key) && value.Value != null);
    private static bool ValidOptions(CompilerCompilationOptionsSnapshot? value) => value != null && Defined<OutputKind>(value.OutputKind) &&
        Defined<OptimizationLevel>(value.OptimizationLevel) && Defined<Platform>(value.Platform) && Defined<NullableContextOptions>(value.NullableContext) &&
        Defined<MetadataImportOptions>(value.MetadataImportOptions) && !value.ReferencesSupersedeLowerVersions &&
        (value.AssemblyIdentityComparer == "Default" || value.AssemblyIdentityComparer == "Desktop") && value.Usings != null &&
        value.Usings.All(static item => !string.IsNullOrWhiteSpace(item)) && value.ResolverPolicy == "Materialized";
    private static bool ValidTree(CompilerSyntaxTreeSnapshot? value) => value != null && value.Text != null && Defined<LanguageVersion>(value.LanguageVersion) &&
        Defined<DocumentationMode>(value.DocumentationMode) && Defined<SourceCodeKind>(value.Kind) && value.PreprocessorSymbols != null &&
        value.PreprocessorSymbols.All(static item => !string.IsNullOrWhiteSpace(item)) && ValidFeatures(value.Features);
    private static bool ValidReference(CompilerReferenceSnapshot? value) => value != null && Path.IsPathRooted(value.Path) &&
        Defined<MetadataImageKind>(value.Kind) && value.Aliases != null && value.Aliases.All(static item => !string.IsNullOrWhiteSpace(item)) &&
        !string.IsNullOrWhiteSpace(value.Identity) && WorkerProtocolJson.IsSha256(value.Sha256);
    private static T Parse<T>(string value) where T : struct, Enum => Defined<T>(value) ? (T)Enum.Parse(typeof(T), value) :
        throw new InvalidDataException("A compiler snapshot option is invalid.");
    private static string Version(Type type) => type.Assembly.GetName().Version?.ToString() ?? throw new InvalidOperationException(
        "The compiler version is unavailable.");
    private static string Mvid(Type type) => type.Module.ModuleVersionId.ToString("D");
    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/');
    private static byte[] ReadImage(string path, CancellationToken cancellationToken) {
        using var input = File.OpenRead(path);
        using var output = new MemoryStream(input.Length <= int.MaxValue ? (int)input.Length : 0);
        var buffer = new byte[81920]; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static string Hash(byte[] bytes) => WorkerProtocolJson.ComputeSha256(bytes);
}
