using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

// One self-applied project build: its worker result, when the build published one.
internal sealed record PerfSelfProject
{
    public string Name { get; init; } = string.Empty;
    public int? BuildExitCode { get; init; }
    public WorkerVerifyResponse? Response { get; init; }
    internal ImmutableArray<WorkerCallableCost> Costs =>
        Response?.Summary.Costs?.Callables ?? [];
}

internal sealed record PerfSelfBuild
{
    public string Project { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public long BuildElapsedMilliseconds { get; init; }
}

// Summarizes verification cost when SharpProof verifies its own source tree.
// Strictly advisory: the input changes with every commit, so outcomes, timeouts
// and cost changes are reported, never judged. Only unreadable results fail.
internal static class PerfSelf
{
    private static readonly JsonSerializerOptions s_buildOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Reads every published result beneath the directory (a multi-targeted
    // project publishes one per target framework) and joins it with the build
    // record, so projects without a result are still listed.
    internal static ImmutableArray<PerfSelfProject> Load(string resultDirectory, string? buildsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultDirectory);
        var builds = buildsPath != null && File.Exists(buildsPath)
            ? JsonSerializer.Deserialize<ImmutableArray<PerfSelfBuild>>(File.ReadAllText(buildsPath), s_buildOptions)
            : [];
        var exitCodes = builds.IsDefault ? ImmutableDictionary<string, int>.Empty
            : builds.ToImmutableDictionary(static build => build.Name, static build => build.ExitCode, StringComparer.Ordinal);
        var projects = ImmutableArray.CreateBuilder<PerfSelfProject>();
        var published = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(resultDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(resultDirectory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                var folder = Path.GetRelativePath(resultDirectory, Path.GetDirectoryName(path)!);
                var name = folder == "." ? stem : string.Create(CultureInfo.InvariantCulture, $"{stem} ({folder.Replace('\\', '/')})");
                var response = WorkerProtocolJson.DeserializeResponse(File.ReadAllText(path))
                    ?? throw new InvalidDataException("Empty worker result: " + path);
                projects.Add(new PerfSelfProject { Name = name, BuildExitCode = exitCodes.TryGetValue(stem, out var code) ? code : null, Response = response });
                _ = published.Add(stem);
            }
        }
        foreach (var (name, code) in exitCodes.Where(entry => !published.Contains(entry.Key)))
        {
            projects.Add(new PerfSelfProject { Name = name, BuildExitCode = code });
        }
        return [.. projects.OrderBy(static project => project.Name, StringComparer.Ordinal)];
    }

    internal static string Summarize(ImmutableArray<PerfSelfProject> projects)
    {
        if (projects.IsDefault)
        {
            throw new ArgumentException("Array must be initialized.", nameof(projects));
        }
        var all = projects.SelectMany(static project => project.Costs.Select(cost => (project.Name, Cost: cost))).ToImmutableArray();
        var text = new StringBuilder();
        text.AppendLine("## Verification performance (SharpProof on itself)").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Advisory: {projects.Count(static project => project.Response != null)} of {projects.Length} projects published a result; " +
            $"{all.Length:N0} callables, {all.Sum(static entry => entry.Cost.ConsumedRlimit):N0} rlimit, " +
            $"{all.Sum(static entry => entry.Cost.ElapsedMilliseconds):N0} ms. The input changes with every commit, so nothing here fails the run.").AppendLine();
        text.AppendLine("| Project | Run | Build exit | Callables | Total rlimit | Total ms |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var project in projects)
        {
            var run = project.Response?.RunStatus.ToString() ?? "no result";
            var exit = project.BuildExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {project.Name} | {run} | {exit} | {project.Costs.Length:N0} | {project.Costs.Sum(static cost => cost.ConsumedRlimit):N0} | {project.Costs.Sum(static cost => cost.ElapsedMilliseconds):N0} |");
        }
        text.AppendLine().AppendLine("| Costliest callables | Project | rlimit | ms |").AppendLine("| --- | --- | ---: | ---: |");
        foreach (var (name, cost) in all.OrderByDescending(static entry => entry.Cost.ConsumedRlimit)
            .ThenByDescending(static entry => entry.Cost.ElapsedMilliseconds).Take(10))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| `{cost.CallableId}` | {name} | {cost.ConsumedRlimit:N0} | {cost.ElapsedMilliseconds:N0} |");
        }
        return text.ToString();
    }
}
