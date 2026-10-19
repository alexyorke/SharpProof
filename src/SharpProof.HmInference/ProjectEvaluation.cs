using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace SharpProof.HmInference;

internal static class ProjectEvaluation
{
    private static readonly string[] CompileTargets = ["Compile"];
    // Keep SDK assembly resolution behind MSBuildLocator registration.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The ProjectCollection is unconditionally disposed in the enclosing finally after its awaited build; ownership tracking loses the alias through BuildParameters.")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static async Task<Solution> RestoreVisualBasicConstantsAsync(Solution solution, Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(properties);
        foreach (var projectId in solution.ProjectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = solution.GetProject(projectId)!;
            if (project.ParseOptions is not VisualBasicParseOptions options || project.FilePath is null)
            { continue; }
            var globals = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
            {
                ["DesignTimeBuild"] = "true",
                ["SkipCompilerExecution"] = "true",
                ["BuildProjectReferences"] = "false",
                ["ProvideCommandLineArgs"] = "true"
            };
            if (project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var framework))
            { globals["TargetFramework"] = framework; }
            ProjectCollection? collection = null;
            try
            {
                collection = new ProjectCollection(globals);
                var evaluated = collection.LoadProject(project.FilePath);
                if (string.IsNullOrEmpty(evaluated.GetPropertyValue("TargetFramework")) && !string.IsNullOrEmpty(evaluated.GetPropertyValue("TargetFrameworks")))
                { throw new InvalidOperationException("Cannot identify the target framework of Visual Basic project: " + project.Name); }
                using var manager = new BuildManager();
                manager.BeginBuild(new BuildParameters(collection));
                BuildResult built;
                try
                {
                    var request = new BuildRequestData(evaluated.CreateProjectInstance(), CompileTargets, null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);
                    var submission = manager.PendBuildRequest(request);
                    var completion = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = cancellationToken.Register(manager.CancelAllSubmissions);
                    submission.ExecuteAsync(finished =>
                    {
                        ArgumentNullException.ThrowIfNull(finished);
                        if (cancellationToken.IsCancellationRequested)
                        { completion.TrySetCanceled(cancellationToken); }
                        else if (finished.BuildResult is { } result)
                        { completion.TrySetResult(result); }
                        else
                        { completion.TrySetException(new InvalidOperationException("MSBuild completed without a result.")); }
                    }, null);
                    built = await completion.Task.ConfigureAwait(false);
                }
                finally
                { manager.EndBuild(); }
                cancellationToken.ThrowIfCancellationRequested();
                if (built.OverallResult != BuildResultCode.Success || built.ProjectStateAfterBuild is null)
                { throw new InvalidOperationException("Cannot evaluate Visual Basic compiler settings: " + project.Name); }
                var constants = built.ProjectStateAfterBuild.GetPropertyValue("FinalDefineConstants");
                var symbols = VisualBasicCommandLineParser.ParseConditionalCompilationSymbols(constants, out var diagnostics, options.PreprocessorSymbols);
                var errors = diagnostics.Where(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Severity == DiagnosticSeverity.Error;
                }).ToArray();
                if (errors.Length != 0)
                { throw new InvalidOperationException("Invalid Visual Basic conditional constants: " + errors[0]); }
                var updated = options.WithPreprocessorSymbols(symbols);
                var compilationOptions = project.CompilationOptions as VisualBasicCompilationOptions
                    ?? throw new InvalidOperationException("Missing Visual Basic compilation options.");
                // VB also uses compilation-level parse options for its generated My template.
                solution = solution.WithProjectParseOptions(projectId, updated)
                    .WithProjectCompilationOptions(projectId, compilationOptions.WithParseOptions(updated));
            }
            finally
            { collection?.Dispose(); }
        }
        return solution;
    }
}
