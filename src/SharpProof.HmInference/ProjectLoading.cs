using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpProof.HmInference;

internal static class ProjectLoading
{
    internal static bool IsProjectInput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToUpperInvariant() is ".CSPROJ" or ".SLN" or ".SLNX" or ".SLNF";
    }

    internal static async Task<int> Run(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var offset = args is ["--project" or "--solution", ..] ? 1 : 0;
        if (args.Length <= offset || !IsProjectInput(args[offset]))
        { Program.Log.Error("Expected a C# project or solution path."); return 2; }
        var path = Path.GetFullPath(args[offset]);
        if (!File.Exists(path))
        { Program.Log.Error("File not found: " + path); return 2; }
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var restore = true;
        for (var index = offset + 1; index < args.Length; index++)
        {
            if (index < 0)
            { throw new InvalidOperationException("Invalid option index."); }
            if (args[index] == "--no-restore")
            {
                if (!restore)
                { Program.Log.Error("Duplicate project option."); return 2; }
                restore = false;
                continue;
            }
            if (index + 1 >= args.Length)
            { Program.Log.Error("Missing value for project option."); return 2; }
            var option = args[index];
            var value = args[++index];
            var name = option switch { "--framework" => "TargetFramework", "--configuration" => "Configuration", _ => null };
            if (option == "--property")
            {
                var equals = value.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0)
                { Program.Log.Error("MSBuild properties must use Name=Value."); return 2; }
                name = value[..equals];
                value = value[(equals + 1)..];
            }
            if (name is null || string.IsNullOrWhiteSpace(name) || !properties.TryAdd(name, value))
            { Program.Log.Error("Unknown or duplicate project option."); return 2; }
        }
        foreach (var name in properties.Keys)
        {
            try
            { System.Xml.XmlConvert.VerifyNCName(name); }
            catch (System.Xml.XmlException)
            { Program.Log.Error("Invalid MSBuild property name."); return 2; }
        }
        try
        {
            if (!MSBuildLocator.IsRegistered)
            {
                var query = VisualStudioInstanceQueryOptions.Default;
                query.WorkingDirectory = Path.GetDirectoryName(path)!;
                var instance = MSBuildLocator.QueryVisualStudioInstances(query).FirstOrDefault();
                if (instance is null)
                { Program.Log.Error("No compatible MSBuild SDK found for this project."); return 1; }
                MSBuildLocator.RegisterInstance(instance);
            }
            if (restore && !await ProjectRestoration.RunAsync(path, properties, cancellationToken).ConfigureAwait(false))
            { return 1; }
            return await Load(path, properties, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or AggregateException
            || exception.GetType().FullName is "Microsoft.Build.Exceptions.InvalidProjectFileException" or "Microsoft.Build.Exceptions.CircularDependencyException")
        { Program.Log.Error("Project loading failed: " + exception.Message); return 1; }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each per-project workspace is disposed by its using declaration on every exit; the analyzer loses ownership through asynchronous project snapshots.")]
    private static async Task<int> Load(string path, Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(properties);
        var contexts = new ProjectBuildContexts(path, properties);
        var nodes = contexts.Projects.ToArray();
        var needed = new HashSet<Microsoft.Build.Graph.ProjectGraphNode>(nodes.SelectMany(node =>
        {
            ArgumentNullException.ThrowIfNull(node);
            return ProjectBuildContexts.CompilationReferences(node);
        }));
        var images = new Dictionary<Microsoft.Build.Graph.ProjectGraphNode, (PortableExecutableReference Reference, string? OutputPath, string? RefPath)>();
        var prepared = new List<(string Name, string? Path, CSharpCompilation Compilation, Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions Options)>();
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var globals = ProjectBuildContexts.GetProperties(node);
            using var workspace = MSBuildWorkspace.Create(globals);
            workspace.LoadMetadataForReferencedProjects = false;
            workspace.SkipUnrecognizedProjects = false;
            var project = await workspace.OpenProjectAsync(node.ProjectInstance.FullPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (ReportFailures(workspace))
            { return 1; }
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
            foreach (var reference in project.AllProjectReferences.ToArray())
            { project = project.RemoveProjectReference(reference); }
            foreach (var dependency in dependencies)
            {
                if (!images.TryGetValue(dependency, out var image))
                { throw new InvalidOperationException("Missing current project-reference metadata: " + dependency.ProjectInstance.FullPath); }
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
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null || Program.ReportCompilerErrors(compilation, cancellationToken))
            { return 1; }
            if (needed.Contains(node))
            {
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: true), cancellationToken: cancellationToken);
                if (!emitted.Success)
                {
                    foreach (var diagnostic in emitted.Diagnostics.Take(10))
                    { Program.Log.Error(diagnostic.ToString()); }
                    Program.Log.Error("Cannot emit project reference metadata; inference skipped.");
                    return 1;
                }
                var kind = compilation.Options.OutputKind == OutputKind.NetModule ? MetadataImageKind.Module : MetadataImageKind.Assembly;
                images.Add(node, (MetadataReference.CreateFromImage(image.ToArray(), new MetadataReferenceProperties(kind), filePath: project.OutputFilePath), project.OutputFilePath, project.OutputRefFilePath));
            }
            if (compilation is CSharpCompilation csharp)
            { prepared.Add((project.Name, project.FilePath, csharp, project.AnalyzerOptions)); }
            else
            { Program.Log.Info("Skipping non-C# project: " + project.Name); }
        }
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
