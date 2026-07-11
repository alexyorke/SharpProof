using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SharpProof.Analyzer;
using SharpProof.Symbolic;

internal sealed class SymbolicCliInputContext : IDisposable
{
    private readonly MSBuildWorkspace? _workspace;

    private SymbolicCliInputContext(
        SymbolicSourceInput sourceInput,
        MSBuildWorkspace? workspace = null,
        SharpProofProjectAnalysisContext? projectContext = null,
        ImmutableArray<string> workspaceDiagnostics = default)
    {
        SourceInput = sourceInput;
        _workspace = workspace;
        ProjectContext = projectContext;
        WorkspaceDiagnostics = workspaceDiagnostics.IsDefault
            ? ImmutableArray<string>.Empty
            : workspaceDiagnostics;
    }

    public SymbolicSourceInput SourceInput { get; }

    public SharpProofProjectAnalysisContext? ProjectContext { get; }

    public ImmutableArray<string> WorkspaceDiagnostics { get; }

    public static async Task<SymbolicCliInputContext> CreateAsync(
        SymbolicCliOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        if (!options.IsProjectAware)
        {
            var standardInput = options.ReadSourceFromStdin
                ? await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return new SymbolicCliInputContext(options.CreateSourceInput(standardInput));
        }

        var workspaceDiagnostics = ImmutableArray.CreateBuilder<string>();
        var workspace = options.MSBuildProperties.Count == 0
            ? MSBuildWorkspace.Create()
            : MSBuildWorkspace.Create(options.MSBuildProperties);
        workspace.WorkspaceFailed += (_, eventArgs) =>
            workspaceDiagnostics.Add(eventArgs.Diagnostic.Kind + ": " + eventArgs.Diagnostic.Message);

        try
        {
            var containerPath = Path.GetFullPath(options.ProjectPath ?? options.SolutionPath!);
            var sourcePath = ResolveSourcePath(options.FilePath!, Path.GetDirectoryName(containerPath)!);
            var (project, solutionPath) = options.ProjectPath != null
                ? (await workspace.OpenProjectAsync(containerPath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false),
                    (string?)null)
                : (SelectSolutionProject(
                        await workspace.OpenSolutionAsync(containerPath, cancellationToken: cancellationToken)
                            .ConfigureAwait(false),
                        sourcePath,
                        options.ProjectName),
                    containerPath);

            if (options.ProjectName != null &&
                !MatchesProject(project, options.ProjectName))
                throw new ArgumentException(
                    $"Loaded project '{project.Name}' does not match --project-name '{options.ProjectName}'.");

            var document = FindDocument(project, sourcePath) ??
                           throw new ArgumentException(
                               $"Source file '{sourcePath}' is not compiled by project '{project.Name}'.");
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false) ??
                             throw new ArgumentException($"Could not parse project document '{sourcePath}'.");
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) ??
                              throw new ArgumentException(
                                  $"Could not create a compilation for project '{project.Name}'.");
            var analyzerConfigPaths = project.AnalyzerConfigDocuments
                .Select(static document => document.FilePath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path!)
                .ToArray();
            var context = new SharpProofProjectAnalysisContext(
                compilation,
                syntaxTree,
                project.AnalyzerOptions,
                project.Name,
                project.FilePath,
                solutionPath,
                analyzerConfigPaths);
            return new SymbolicCliInputContext(
                context.SourceInput,
                workspace,
                context,
                workspaceDiagnostics.ToImmutable());
        }
        catch (OperationCanceledException)
        {
            workspace.Dispose();
            throw;
        }
        catch (ArgumentException)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            workspace.Dispose();
            throw new ArgumentException(
                "Could not load the MSBuild project context: " + exception.Message,
                exception);
        }
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }

    private static Project SelectSolutionProject(
        Solution solution,
        string sourcePath,
        string? projectName)
    {
        var candidates = solution.Projects
            .Where(project => projectName == null || MatchesProject(project, projectName))
            .Where(project => FindDocument(project, sourcePath) != null)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 when projectName != null => throw new ArgumentException(
                $"Solution has no project named '{projectName}' that compiles '{sourcePath}'."),
            0 => throw new ArgumentException($"No solution project compiles '{sourcePath}'."),
            _ => throw new ArgumentException(
                $"Multiple solution projects compile '{sourcePath}'; specify --project-name.")
        };
    }

    private static bool MatchesProject(Project project, string requestedName)
    {
        return string.Equals(project.Name, requestedName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(project.AssemblyName, requestedName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   Path.GetFileNameWithoutExtension(project.FilePath),
                   requestedName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static Document? FindDocument(Project project, string sourcePath)
    {
        return project.Documents.FirstOrDefault(document =>
            document.FilePath != null && PathEquals(document.FilePath, sourcePath));
    }

    private static string ResolveSourcePath(string sourcePath, string containerDirectory)
    {
        if (Path.IsPathRooted(sourcePath))
        {
            var rootedPath = Path.GetFullPath(sourcePath);
            if (File.Exists(rootedPath)) return rootedPath;

            throw new ArgumentException("--file does not exist: " + rootedPath);
        }

        var currentDirectoryPath = Path.GetFullPath(sourcePath);
        if (File.Exists(currentDirectoryPath)) return currentDirectoryPath;

        var containerRelativePath = Path.GetFullPath(Path.Combine(containerDirectory, sourcePath));
        if (File.Exists(containerRelativePath)) return containerRelativePath;

        throw new ArgumentException(
            $"--file does not exist relative to the current directory or project container: {sourcePath}");
    }

    private static bool PathEquals(string left, string right)
    {
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
