using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;

namespace SharpProof.HmInference;

internal static class ProjectGeneration
{
    internal static async Task<(Compilation? Compilation, AnalyzerOptions Options)> RunAsync(Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        var references = project.AnalyzerReferences.ToArray();
        // Own the generator run so diagnostics and exceptions cannot disappear into
        // workspace-only state. Removing references prevents duplicate executions.
        project = project.WithAnalyzerReferences([]);
        var options = project.AnalyzerOptions;
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        { return (null, options); }
        var generators = ImmutableArray.CreateBuilder<ISourceGenerator>();
        var failures = new List<string>();
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference is UnresolvedAnalyzerReference)
            { failures.Add("Unresolved generator/analyzer reference: " + reference.FullPath); continue; }
            if (reference is AnalyzerFileReference original)
            {
                // A workspace may already have cached a failed discovery. A fresh
                // reference preserves the loader while making failures observable.
                var fresh = new AnalyzerFileReference(original.FullPath, original.AssemblyLoader);
                fresh.AnalyzerLoadFailed += (_, failure) =>
                {
                    ArgumentNullException.ThrowIfNull(failure);
                    failures.Add("Generator loading failed: " + fresh.Display + ": " + failure.ErrorCode + ": " + failure.Message);
                };
                generators.AddRange(fresh.GetGenerators(project.Language));
            }
            else
            { generators.AddRange(reference.GetGenerators(project.Language)); }
        }
        if (failures.Count != 0)
        {
            foreach (var failure in failures.Take(10))
            { Program.Log.Error(failure); }
            Program.Log.Error("Source generator loading failed; inference skipped.");
            return (null, options);
        }
        if (generators.Count == 0)
        { return (compilation, options); }
        var output = project.CompilationOutputInfo;
        var baseDirectory = output.GeneratedFilesOutputDirectory
            ?? (output.AssemblyPath is { } assemblyPath ? Path.GetDirectoryName(assemblyPath) : null);
        var driverOptions = new GeneratorDriverOptions(baseDirectory: baseDirectory);
        GeneratorDriver driver = project.ParseOptions switch
        {
            CSharpParseOptions csharp => CSharpGeneratorDriver.Create(generators.ToImmutable(), options.AdditionalFiles, csharp, options.AnalyzerConfigOptionsProvider, driverOptions),
            VisualBasicParseOptions visualBasic => VisualBasicGeneratorDriver.Create(generators.ToImmutable(), options.AdditionalFiles, visualBasic, options.AnalyzerConfigOptionsProvider, driverOptions),
            _ => throw new NotSupportedException("Source generation is not supported for " + project.Language)
        };
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics, cancellationToken);
        var result = driver.GetRunResult();
        var effective = CompilationWithAnalyzers.GetEffectiveDiagnostics(diagnostics, generatedCompilation).Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return !diagnostic.IsSuppressed;
        }).ToArray();
        foreach (var diagnostic in effective.Take(10))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            { Program.Log.Error(diagnostic.ToString()); }
            else
            { Program.Log.Info(diagnostic.ToString()); }
        }
        var exceptions = result.Results.Where(run => run.Exception is not null).ToArray();
        if (exceptions.Length != 0 || effective.Any(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }))
        {
            foreach (var run in exceptions.Take(10))
            { Program.Log.Error("Source generator failed: " + run.Generator.GetType().FullName + ": " + run.Exception!.Message); }
            Program.Log.Error("Source generation failed; inference skipped.");
            return (null, options);
        }
        // Re-enter generated text as documents so workspace editor configuration
        // applies to their real output paths, without running the generators again.
        var generatedIds = new List<DocumentId>();
        foreach (var run in result.Results)
        {
            foreach (var source in run.GeneratedSources)
            {
                var document = project.AddDocument(source.HintName, source.SourceText, filePath: source.SyntaxTree.FilePath);
                generatedIds.Add(document.Id);
                project = document.Project;
            }
        }
        compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        { return (null, project.AnalyzerOptions); }
        var generatedTrees = new HashSet<SyntaxTree>();
        foreach (var id in generatedIds)
        {
            var tree = await project.GetDocument(id)!.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is not null)
            { generatedTrees.Add(tree); }
        }
        var analyzerOptions = new AnalyzerOptions(project.AnalyzerOptions.AdditionalFiles,
            new GeneratedAnalyzerOptions(project.AnalyzerOptions.AnalyzerConfigOptionsProvider, generatedTrees));
        compilation = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(
            new GeneratedTreeOptions(compilation.Options.SyntaxTreeOptionsProvider, analyzerOptions.AnalyzerConfigOptionsProvider, generatedTrees)));
        return (compilation, analyzerOptions);
    }

    private sealed class GeneratedTreeOptions : SyntaxTreeOptionsProvider
    {
        private readonly SyntaxTreeOptionsProvider? sourceOptions;
        private readonly HashSet<SyntaxTree> generated;
        private readonly AnalyzerConfigOptionsProvider configuration;
        internal GeneratedTreeOptions(SyntaxTreeOptionsProvider? sourceOptions, AnalyzerConfigOptionsProvider configuration, HashSet<SyntaxTree> generated)
        {
            ArgumentNullException.ThrowIfNull(generated);
            ArgumentNullException.ThrowIfNull(configuration);
            this.configuration = configuration;
            this.sourceOptions = sourceOptions;
            this.generated = generated;
        }
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tree);
            if (generated.Contains(tree))
            {
                return configuration.GetOptions(tree).TryGetValue("generated_code", out var configured)
                    && string.Equals(configured, "false", StringComparison.OrdinalIgnoreCase)
                    ? GeneratedKind.NotGenerated : GeneratedKind.MarkedGenerated;
            }
            return sourceOptions?.IsGenerated(tree, cancellationToken) ?? GeneratedKind.Unknown;
        }
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = ReportDiagnostic.Default;
            return sourceOptions?.TryGetDiagnosticValue(tree, diagnosticId, cancellationToken, out severity) ?? false;
        }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = ReportDiagnostic.Default;
            return sourceOptions?.TryGetGlobalDiagnosticValue(diagnosticId, cancellationToken, out severity) ?? false;
        }
    }
    private sealed class GeneratedAnalyzerOptions : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptionsProvider original;
        private readonly HashSet<SyntaxTree> generated;
        internal GeneratedAnalyzerOptions(AnalyzerConfigOptionsProvider original, HashSet<SyntaxTree> generated)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(generated);
            this.original = original;
            this.generated = generated;
        }
        public override AnalyzerConfigOptions GlobalOptions => original.GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            ArgumentNullException.ThrowIfNull(textFile);
            return original.GetOptions(textFile);
        }
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            ArgumentNullException.ThrowIfNull(tree);
            var options = original.GetOptions(tree);
            return generated.Contains(tree) && !options.TryGetValue("generated_code", out _)
                ? new GeneratedFileOptions(options) : options;
        }
    }
    private sealed class GeneratedFileOptions : AnalyzerConfigOptions
    {
        private readonly AnalyzerConfigOptions original;
        internal GeneratedFileOptions(AnalyzerConfigOptions original)
        {
            ArgumentNullException.ThrowIfNull(original);
            this.original = original;
        }
        public override IEnumerable<string> Keys => original.Keys.Append("generated_code");
        public override bool TryGetValue(string key, out string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (string.Equals(key, "generated_code", StringComparison.OrdinalIgnoreCase))
            { value = "true"; return true; }
            return original.TryGetValue(key, out value!);
        }
    }
}
