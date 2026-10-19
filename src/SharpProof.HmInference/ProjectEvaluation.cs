using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace SharpProof.HmInference;

internal static class ProjectEvaluation
{
    private static readonly string[] CompileTargets = ["Compile"];
    // Keep SDK assembly resolution behind MSBuildLocator registration.
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
            var built = await BuildTargetsAsync(project.FilePath, globals, CompileTargets, cancellationToken).ConfigureAwait(false);
            var constants = built.GetPropertyValue("FinalDefineConstants");
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
        return solution;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The ProjectCollection is unconditionally disposed in the enclosing finally after its awaited build; ownership tracking loses the alias through BuildParameters.")]
    internal static async Task<ProjectInstance> BuildTargetsAsync(string path, Dictionary<string, string> properties, string[] targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectCollection? collection = null;
        try
        {
            collection = new ProjectCollection(properties);
            var evaluated = collection.LoadProject(path);
            var logger = new EvaluationLogger();
            using var manager = new BuildManager();
            manager.BeginBuild(new BuildParameters(collection) { Loggers = [logger] });
            BuildResult built;
            try
            {
                var request = new BuildRequestData(evaluated.CreateProjectInstance(), targets, null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);
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
            { throw new InvalidOperationException("Cannot evaluate project targets: " + path + ". " + string.Join("; ", logger.Errors)); }
            return built.ProjectStateAfterBuild;
        }
        finally
        { collection?.Dispose(); }
    }
    private sealed class EvaluationLogger : Microsoft.Build.Framework.ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> errors = new();
        internal IEnumerable<string> Errors => errors;
        public Microsoft.Build.Framework.LoggerVerbosity Verbosity { get; set; } = Microsoft.Build.Framework.LoggerVerbosity.Minimal;
        public string? Parameters { get; set; }
        public void Initialize(Microsoft.Build.Framework.IEventSource eventSource)
        {
            ArgumentNullException.ThrowIfNull(eventSource);
            eventSource.ErrorRaised += (object? sender, Microsoft.Build.Framework.BuildErrorEventArgs error) =>
            {
                ArgumentNullException.ThrowIfNull(error);
                errors.Enqueue(error.Code + ": " + error.Message);
                while (errors.Count > 10)
                { errors.TryDequeue(out _); }
            };
        }
        public void Shutdown() { }
    }
}
