using Microsoft.Build.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpProof.HmInference;

internal sealed class ProjectLoadSession
{
    private readonly ProjectToolBuilder? toolBuilder;
    internal ProjectLoadSession(bool buildTools) { toolBuilder = buildTools ? new ProjectToolBuilder() : null; }
    private readonly Dictionary<ProjectGraphNode, (Compilation Compilation, string? OutputPath, string? RefPath)> loaded = [];
    private readonly Dictionary<ProjectGraphNode, (PortableExecutableReference Reference, string? OutputPath, string? RefPath)> images = [];
    private readonly List<(string Name, string? Path, CSharpCompilation Compilation, Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions Options)> prepared = [];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each per-project workspace is disposed by its using declaration on every exit; the analyzer loses ownership through asynchronous project snapshots.")]
    internal async Task<bool> LoadAsync(ProjectGraphNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded.ContainsKey(node))
        { return true; }
        if (toolBuilder is not null && !await toolBuilder.EnsureAsync(node, cancellationToken).ConfigureAwait(false))
        { return false; }
        var globals = ProjectBuildContexts.GetProperties(node);
        using var workspace = MSBuildWorkspace.Create(globals);
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.SkipUnrecognizedProjects = false;
        var project = await workspace.OpenProjectAsync(node.ProjectInstance.FullPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ReportFailures(workspace))
        { return false; }
        // Graph evaluation expands outer multi-target projects. Use the SDK workspace
        // target negotiation to select the inner compilation for each source reference.
        var selectedFrameworks = project.AllProjectReferences.Select(reference => { ArgumentNullException.ThrowIfNull(reference); return project.Solution.GetProject(reference.ProjectId)!; }).Select(selected =>
        {
            ArgumentNullException.ThrowIfNull(selected);
            selected.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var framework);
            return (selected.FilePath, Framework: framework);
        }).ToArray();
        var dependencies = ProjectBuildContexts.CompilationReferences(node).Distinct().GroupBy(dependency =>
        {
            ArgumentNullException.ThrowIfNull(dependency);
            return dependency.ProjectInstance.FullPath;
        }, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).SelectMany(group =>
        {
            ArgumentNullException.ThrowIfNull(group);
            if (group.Count() == 1)
            { return group; }
            var selected = group.Where(dependency =>
            {
                ArgumentNullException.ThrowIfNull(dependency);
                return selectedFrameworks.Any(item => string.Equals(item.FilePath, dependency.ProjectInstance.FullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && item.Framework == dependency.ProjectInstance.GetPropertyValue("TargetFramework"));
            }).ToArray();
            if (selected.Length != 1)
            { throw new InvalidOperationException("Cannot identify a unique reference target framework: " + group.Key); }
            return selected.AsEnumerable();
        }).ToArray();
        foreach (var dependency in dependencies)
        {
            if (!await LoadAsync(dependency, cancellationToken).ConfigureAwait(false))
            { return false; }
        }
        foreach (var reference in project.AllProjectReferences.ToArray())
        { project = project.RemoveProjectReference(reference); }
        foreach (var dependency in dependencies)
        {
            if (!images.TryGetValue(dependency, out var image))
            {
                var snapshot = loaded[dependency];
                using var stream = new MemoryStream();
                var emitted = snapshot.Compilation.Emit(stream, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: true), cancellationToken: cancellationToken);
                if (!emitted.Success)
                {
                    foreach (var diagnostic in emitted.Diagnostics.Take(10))
                    { Program.Log.Error(diagnostic.ToString()); }
                    Program.Log.Error("Cannot emit project reference metadata; inference skipped.");
                    return false;
                }
                var kind = snapshot.Compilation.Options.OutputKind == OutputKind.NetModule ? MetadataImageKind.Module : MetadataImageKind.Assembly;
                image = (MetadataReference.CreateFromImage(stream.ToArray(), new MetadataReferenceProperties(kind), filePath: snapshot.OutputPath), snapshot.OutputPath, snapshot.RefPath);
                images.Add(dependency, image);
            }
            foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>().ToArray())
            {
                if (reference.FilePath is not null && (string.Equals(reference.FilePath, image.OutputPath, (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    || string.Equals(reference.FilePath, image.RefPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                { project = project.RemoveMetadataReference(reference); }
            }
            var (aliases, embed) = ProjectBuildContexts.ReferenceProperties(node, dependency);
            project = project.AddMetadataReference(image.Reference.WithProperties(image.Reference.Properties.WithAliases(aliases).WithEmbedInteropTypes(embed)));
        }
        var solution = project.Solution;
        foreach (var projectId in solution.ProjectIds)
        {
            if (projectId != project.Id)
            { solution = solution.RemoveProject(projectId); }
        }
        solution = await ProjectEvaluation.RestoreVisualBasicConstantsAsync(solution, globals, cancellationToken).ConfigureAwait(false);
        project = solution.GetProject(project.Id)!;
        var generated = await ProjectGeneration.RunAsync(project, cancellationToken).ConfigureAwait(false);
        var compilation = generated.Compilation;
        if (compilation is null || Program.ReportCompilerErrors(compilation, cancellationToken))
        { return false; }
        loaded.Add(node, (compilation, project.OutputFilePath, project.OutputRefFilePath));
        if (compilation is CSharpCompilation csharp)
        { prepared.Add((project.Name, project.FilePath, csharp, generated.Options)); }
        else
        { Program.Log.Info("Skipping non-C# project: " + project.Name); }
        return true;
    }

    internal async Task<int> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var result = 0;
        foreach (var item in prepared)
        {
            Program.Log.Info("Project: " + item.Name + " (" + item.Path + ")");
            result |= await Program.AnalyzeCompilationAsync(item.Compilation, item.Options, diagnosticsOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        Program.Log.Info("C# project compilations analyzed: " + prepared.Count);
        return prepared.Count == 0 ? 1 : result;
    }
    private static bool ReportFailures(MSBuildWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var failures = workspace.Diagnostics.Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Kind == WorkspaceDiagnosticKind.Failure;
        }).ToArray();
        foreach (var failure in failures.Take(10))
        { Program.Log.Error(failure.Message); }
        if (failures.Length != 0)
        { Program.Log.Error("MSBuild loading failed; restore the target project and resolve its build errors before inference."); }
        return failures.Length != 0;
    }
}
