using System.Security.Cryptography;
using System.Text;
#pragma warning disable RS1035 // This build-only analyzer emits the selected seal.
namespace SharpProof.Analyzer;
internal static class FinalCompilationCollector {
    private const string OutputOption =
        "build_property._SharpProofCompilationSealPath";
    private const string TargetFrameworkOption =
        "build_property._SharpProofCompilationTargetFramework";
    private static readonly UTF8Encoding Utf8 = new(false);
    internal static void Collect(CompilationAnalysisContext context, AnalyzerConfiguration configuration) {
        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!options.TryGetValue(OutputOption, out var path) ||
            string.IsNullOrWhiteSpace(path))
            return;
        try {
            Write(path, Create(context, options, configuration));
        }
#pragma warning disable CA1031
        catch (Exception exception)
            when (!context.CancellationToken.IsCancellationRequested) {
#pragma warning restore CA1031
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.CompilationSealFailureRule,
                Location.None,
                exception.GetType().Name + ": " + exception.Message));
        }
    }
    private static string Create(
        CompilationAnalysisContext context,
        AnalyzerConfigOptions options, AnalyzerConfiguration configuration) {
        var compilation = (CSharpCompilation)context.Compilation;
        var targetFramework = Get(options, TargetFrameworkOption);
        var roslynVersion = typeof(Compilation).Assembly.GetName().Version?
            .ToString() ?? string.Empty;
        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilationSeal", 1,
            compilation.Assembly.Identity, targetFramework, roslynVersion);
        var compilerOptions = compilation.Options;
        hash.Add(compilerOptions.AllowUnsafe, compilerOptions.CheckOverflow,
            compilerOptions.Deterministic, compilerOptions.NullableContextOptions,
            compilerOptions.OptimizationLevel, compilerOptions.OutputKind,
            compilerOptions.Platform, compilerOptions.MetadataImportOptions,
            compilerOptions.WarningLevel, compilerOptions.GeneralDiagnosticOption);
        hash.Add("usings", string.Join("\0", compilerOptions.Usings.OrderBy(
            static value => value, StringComparer.Ordinal)));
        hash.Add("policies",
            configuration.Profile, configuration.Features,
            Get(options, "build_property.SharpProofVerifyPolicy").Trim()
                .ToUpperInvariant(),
            Get(options, "build_property.SharpProofAssumptionPolicy").Trim()
                .ToUpperInvariant());
        foreach (var (tree, index) in compilation.SyntaxTrees
                     .Select(static (tree, index) => (tree, index))) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var text = tree.GetText(context.CancellationToken).ToString();
            var parse = (CSharpParseOptions)tree.Options;
            hash.Add("tree", index, Normalize(tree.FilePath), text,
                parse.LanguageVersion, parse.SpecifiedLanguageVersion,
                parse.DocumentationMode, parse.Kind);
            hash.Add("symbols", string.Join("\0",
                parse.PreprocessorSymbolNames.OrderBy(
                    static value => value, StringComparer.Ordinal)));
            foreach (var feature in parse.Features.OrderBy(
                         static value => value.Key, StringComparer.Ordinal))
                hash.Add("feature", feature.Key, feature.Value);
        }
        foreach (var (reference, index) in compilation.References
                     .Select(static (reference, index) => (reference, index))) {
            context.CancellationToken.ThrowIfCancellationRequested();
            hash.Add("reference", index, reference.Properties.Kind,
                reference.Properties.EmbedInteropTypes,
                Normalize(reference.Display ?? string.Empty),
                compilation.GetAssemblyOrModuleSymbol(reference) switch {
                    IAssemblySymbol assembly => assembly.Identity,
                    IModuleSymbol module => module.Name,
                    _ => string.Empty
                });
            hash.Add("aliases", string.Join("\0",
                reference.Properties.Aliases.OrderBy(
                    static value => value, StringComparer.Ordinal)));
            if (reference is not PortableExecutableReference {
                FilePath: { } referencePath
            } || !File.Exists(referencePath))
                throw new InvalidOperationException(
                    "The final compilation contains a non-file reference.");
            hash.Add("image", HashFile(referencePath, context.CancellationToken));
        }
        foreach (var (file, index) in context.Options.AdditionalFiles
                     .Select(static (file, index) => (file, index))) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var text = file.GetText(context.CancellationToken);
            hash.Add("additional", index, Normalize(file.Path), text != null,
                text?.ToString() ?? string.Empty);
        }
        return FormattableString.Invariant($"""
            schema=SharpProof.CompilationSeal
            schemaVersion=1
            assemblyIdentity={compilation.Assembly.Identity}
            targetFramework={targetFramework}
            roslynVersion={roslynVersion}
            syntaxTreeCount={compilation.SyntaxTrees.Length}
            referenceCount={compilation.References.Count()}
            additionalFileCount={context.Options.AdditionalFiles.Length}
            compilationSha256={hash.Finish()}
            """) + "\n";
    }
    private static string Get(AnalyzerConfigOptions options, string key) => options.TryGetValue(key, out var value) ? value : string.Empty;
    private static string Normalize(string value) => value.Replace('\\', '/');
    private static string HashFile(string path, CancellationToken cancellationToken) {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var count = stream.Read(buffer, 0, buffer.Length);
            if (count == 0) return Convert.ToBase64String(hash.GetHashAndReset());
            hash.AppendData(buffer, 0, count);
        }
    }
    private static void Write(string path, string content) {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException(
                "The compilation seal path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, content, Utf8);
            if (File.Exists(destination))
                File.Replace(temporary, destination, null);
            else
                File.Move(temporary, destination);
        }
        finally {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
#pragma warning restore RS1035
