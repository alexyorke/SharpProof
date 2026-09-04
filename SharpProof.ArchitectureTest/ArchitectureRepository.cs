using System.Diagnostics;
using System.Xml.Linq;

namespace SharpProof.ArchitectureTest;

internal static class ArchitectureRepository
{
    internal static Task<ProcessRunnerResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return ProcessRunner.RunCapturedAsync(
            workingDirectory,
            fileName,
            arguments);
    }

    internal static Task<ProcessRunnerResult> RunProcessAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string fileName,
        params string[] arguments)
    {
        var startInfo = ProcessRunner.CreateStartInfo(
            workingDirectory,
            fileName,
            arguments);
        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        return ProcessRunner.RunCapturedAsync(
            startInfo,
            CancellationToken.None);
    }

    internal static async Task<ProcessRunnerResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            return await ProcessRunner.RunCapturedAsync(
                startInfo,
                cancellation.Token);
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"'{startInfo.FileName}' did not exit within " +
                $"{timeout.TotalSeconds:N0} seconds.");
        }
    }

    internal static readonly string[] ProductionProjects = [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.Attributes",
        "SharpProof.BuildTasks",
        "SharpProof.Ir",
        "SharpProof.Meta.Analyzers",
        "SharpProof.CompilerArtifact",
        "SharpProof.CompilerCollector",
        "SharpProof.ContractForGenerator",
        "SharpProof.Specs",
        "SharpProof.Dataflow",
        "SharpProof.Frontend",
        "SharpProof.Fuzz",
        "SharpProof.Host",
        "SharpProof.Contracts",
        "SharpProof.Effects",
        "SharpProof.Verify",
        "SharpProof.Smt",
        "SharpProof.Summaries",
        "SharpProof.Worker.Protocol",
        "SharpProof.Worker",
        "SharpProof.Worker.Launcher"
    ];

    internal static readonly string[] BannedApiProjects = [..
        ProductionProjects
            .Append("SharpProof.Gates")
            .OrderBy(static project => project, StringComparer.Ordinal)];

    internal static IEnumerable<string> TransitiveProjectClosure(
        string root,
        bool includeRoot = true)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.Count != 0)
        {
            var project = pending.Pop();
            if (!visited.Add(project))
            {
                continue;
            }

            if (includeRoot || project != root)
            {
                yield return project;
            }

            foreach (var dependency in ProjectReferences(project))
            {
                pending.Push(dependency);
            }
        }
    }

    internal static string[] ProjectReferences(string project)
    {
        return ProjectReferences(XDocument.Load(ProjectFile(project)));
    }

    internal static string[] ProjectReferences(XDocument document)
    {
        return [.. document
            .Descendants("ProjectReference")
            .Where(static element =>
                !string.Equals(
                    (string?)element.Attribute("OutputItemType"),
                    "Analyzer",
                    StringComparison.OrdinalIgnoreCase))
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value =>
                Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))];
    }

    internal static string[] ProjectPackages(string project)
    {
        return ProjectPackages(XDocument.Load(ProjectFile(project)));
    }

    internal static string[] ProjectPackages(XDocument document)
    {
        return [.. document
            .Descendants("PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)];
    }

    internal static ProjectFileSnapshot ReadProjectFileSnapshot(string project)
    {
        var text = File.ReadAllText(ProjectFile(project));
        var document = XDocument.Parse(text);
        return new(
            ProjectReferences(document),
            ProjectPackages(document),
            text);
    }

    internal static string ProjectFile(string project)
    {
        return Path.Combine(ProjectDirectory(project), project + ".csproj");
    }

    internal static string ProjectDirectory(string project)
    {
        return project == "SharpProof.Fuzz"
            ? Path.Combine(TestRepository.FindRoot(), "Tools", project)
            : Path.Combine(TestRepository.FindRoot(), project);
    }

    internal static string ReadProductionSources(string project)
    {
        return string.Join("\n", ProductionSourceFiles(project).Select(File.ReadAllText));
    }

    internal static IEnumerable<string> ProductionSourceFiles(string project)
    {
        return Directory.GetFiles(
                ProjectDirectory(project),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains(
                    Path.DirectorySeparatorChar + "obj" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !path.Contains(
                    Path.DirectorySeparatorChar + "bin" +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal);
    }

    internal static IEnumerable<string> WorkflowFiles()
    {
        return Directory.EnumerateFiles(
                Path.Combine(TestRepository.FindRoot(), ".github", "workflows"))
            .Where(static path =>
                Path.GetExtension(path) is ".yml" or ".yaml")
            .OrderBy(static path => path, StringComparer.Ordinal);
    }
}

internal sealed class ProjectFileSnapshot(
    string[] references,
    string[] packages,
    string text)
{
    internal string[] References { get; } = references;

    internal string[] Packages { get; } = packages;

    internal string Text { get; } = text;
}
