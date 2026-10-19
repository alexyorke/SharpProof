using Microsoft.Build.Graph;

namespace SharpProof.HmInference;

internal sealed class ProjectToolBuilder
{
    private static readonly string[] ResolveTargets = ["ResolveReferences"];
    private readonly HashSet<ProjectGraphNode> built = [];

    internal async Task<bool> EnsureAsync(ProjectGraphNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = ProjectBuildContexts.ToolReferences(node).Distinct().ToArray();
        if (candidates.Length == 0 || candidates.All(built.Contains))
        { return true; }
        var globals = ProjectBuildContexts.GetProperties(node);
        globals["BuildProjectReferences"] = "false";
        globals["DesignTimeBuild"] = "true";
        globals["SkipCompilerExecution"] = "true";
        var resolved = await ProjectEvaluation.BuildTargetsAsync(node.ProjectInstance.FullPath, globals, ResolveTargets, cancellationToken).ConfigureAwait(false);
        var selected = new HashSet<ProjectGraphNode>();
        foreach (var item in resolved.Items)
        {
            var origin = item.GetMetadataValue("MSBuildSourceProjectFile");
            if (string.IsNullOrEmpty(origin))
            { continue; }
            var possible = candidates.Where(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return SamePath(candidate.ProjectInstance.FullPath, origin);
            }).ToArray();
            if (possible.Length == 0)
            { continue; }
            var path = item.GetMetadataValue("FullPath");
            var matches = possible.Where(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                var target = candidate.ProjectInstance.GetPropertyValue("TargetPath");
                return !string.IsNullOrEmpty(target) && SamePath(target, path);
            }).ToArray();
            if (matches.Length == 1)
            { selected.Add(matches[0]); }
        }
        foreach (var dependency in selected)
        {
            if (built.Contains(dependency))
            { continue; }
            if (!await ProjectRestoration.BuildAsync(dependency.ProjectInstance.FullPath, ProjectBuildContexts.GetProperties(dependency), cancellationToken).ConfigureAwait(false))
            { return false; }
            built.Add(dependency);
        }
        return true;
    }
    private static bool SamePath(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
