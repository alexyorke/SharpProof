using System.Collections.Immutable;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace SharpProof.HmInference;

internal sealed class ProjectBuildContexts
{
    private static readonly char[] AliasSeparators = [',', ';'];
    private readonly ProjectGraph graph;
    internal ProjectBuildContexts(string path, Dictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(properties);
        graph = new ProjectGraph(path, properties);
    }

    internal IEnumerable<ProjectGraphNode> Roots => graph.EntryPointNodes.SelectMany(ExpandRoot).Distinct();

    private static IEnumerable<ProjectGraphNode> ExpandRoot(ProjectGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!IsOuter(node))
        { yield return node; yield break; }
        foreach (var inner in node.ProjectReferences)
        {
            if (!SamePath(node.ProjectInstance.FullPath, inner.ProjectInstance.FullPath))
            { continue; }
            foreach (var target in ExpandRoot(inner))
            { yield return target; }
        }
    }
    private static bool IsOuter(ProjectGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return string.IsNullOrEmpty(node.ProjectInstance.GetPropertyValue("TargetFramework"))
            && !string.IsNullOrEmpty(node.ProjectInstance.GetPropertyValue("TargetFrameworks"));
    }

    private static bool SamePath(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static ProjectItemInstance[] ReferenceItems(ProjectGraphNode node, ProjectGraphNode dependency)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(dependency);
        return node.ProjectInstance.GetItems("ProjectReference").Where(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return SamePath(item.GetMetadataValue("FullPath"), dependency.ProjectInstance.FullPath);
        }).ToArray();
    }

    internal static IEnumerable<ProjectGraphNode> References(ProjectGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var dependency in node.ProjectReferences)
        {
            if (SamePath(node.ProjectInstance.FullPath, dependency.ProjectInstance.FullPath))
            { yield return dependency; continue; }
            var items = ReferenceItems(node, dependency);
            if (items.Length == 0 || items.Any(item =>
            {
                ArgumentNullException.ThrowIfNull(item);
                return !item.GetMetadataValue("ReferenceOutputAssembly").Equals("false", StringComparison.OrdinalIgnoreCase);
            }))
            { yield return dependency; }
        }
    }

    internal static IEnumerable<ProjectGraphNode> CompilationReferences(ProjectGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var dependency in References(node))
        {
            if (IsOuter(dependency))
            {
                foreach (var inner in CompilationReferences(dependency))
                { yield return inner; }
            }
            else
            { yield return dependency; }
        }
    }

    internal static Dictionary<string, string> GetProperties(ProjectGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var properties = new Dictionary<string, string>(node.ProjectInstance.GlobalProperties, StringComparer.OrdinalIgnoreCase);
        // Graph discovery uses this internal protocol flag. The compiler snapshot
        // should retain the ordinary design-time project context.
        properties.Remove("IsGraphBuild");
        return properties;
    }

    internal static (ImmutableArray<string> Aliases, bool Embed) ReferenceProperties(ProjectGraphNode node, ProjectGraphNode dependency)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(dependency);
        var items = ReferenceItems(node, dependency).Where(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return !item.GetMetadataValue("ReferenceOutputAssembly").Equals("false", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (items.Length == 0)
        { return ([], false); }
        var aliases = items[0].GetMetadataValue("Aliases");
        var embed = items[0].GetMetadataValue("EmbedInteropTypes").Equals("true", StringComparison.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.GetMetadataValue("Aliases") != aliases || item.GetMetadataValue("EmbedInteropTypes").Equals("true", StringComparison.OrdinalIgnoreCase) != embed)
            { throw new InvalidOperationException("Ambiguous project-reference properties: " + dependency.ProjectInstance.FullPath); }
        }
        return (aliases.Split(AliasSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray(), embed);
    }
}
