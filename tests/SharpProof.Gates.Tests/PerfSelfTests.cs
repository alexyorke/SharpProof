using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class PerfSelfTests
{
    private static readonly ImmutableArray<string> s_expectedNames = ["Broken", "Published", "Targeted (net10.0)"];

    [Test]
    public void SummaryListsProjectTotalsAndCostliestCallablesOverall()
    {
        ImmutableArray<PerfSelfProject> projects =
        [
            Project("Alpha", 0, WorkerRunStatus.Complete, Cost("M:A.Small", 1, 1), Cost("M:A.Large", 100, 3)),
            Project("Beta", 1, WorkerRunStatus.TimedOut, Cost("M:B.Medium", 10, 2))
        ];

        var summary = PerfSelf.Summarize(projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary, Does.Contain("2 of 2 projects published a result; 3 callables, 111 rlimit, 6 ms."));
            Assert.That(summary, Does.Contain("| Alpha | Complete | 0 | 2 | 101 | 4 |"));
            Assert.That(summary, Does.Contain("| Beta | TimedOut | 1 | 1 | 10 | 2 |"));
            Assert.That(summary, Does.Contain("| `M:A.Large` | Alpha | 100 | 3 |"));
            Assert.That(summary.IndexOf("M:A.Large", StringComparison.Ordinal),
                Is.LessThan(summary.IndexOf("M:B.Medium", StringComparison.Ordinal)));
            Assert.That(summary.IndexOf("M:B.Medium", StringComparison.Ordinal),
                Is.LessThan(summary.IndexOf("M:A.Small", StringComparison.Ordinal)));
        }
    }

    [Test]
    public void SummaryKeepsOnlyTheTenCostliestCallables()
    {
        var costs = Enumerable.Range(1, 12).Select(index => Cost("M:C.N" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), index, 1)).ToArray();

        var summary = PerfSelf.Summarize([Project("Many", 0, WorkerRunStatus.Complete, costs)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary, Does.Contain("`M:C.N12`").And.Contain("`M:C.N03`"));
            Assert.That(summary, Does.Not.Contain("`M:C.N02`").And.Not.Contain("`M:C.N01`"));
        }
    }

    [Test]
    public void ProjectsWithoutAResultAreListedFromTheBuildRecord()
    {
        using var temporary = new TempDirectory("SharpProof-perf-self-");
        var results = Path.Combine(temporary.FullName, "self");
        Directory.CreateDirectory(Path.Combine(results, "net10.0"));
        File.WriteAllText(Path.Combine(results, "Published.json"),
            WorkerProtocolJson.SerializeResponse(Response(WorkerRunStatus.Complete, Cost("M:P.M", 5, 1))));
        File.WriteAllText(Path.Combine(results, "net10.0", "Targeted.json"),
            WorkerProtocolJson.SerializeResponse(Response(WorkerRunStatus.Complete)));
        var builds = Path.Combine(temporary.FullName, "self-builds.json");
        File.WriteAllText(builds, """
            [
              { "project": "src/Published/Published.csproj", "name": "Published", "exitCode": 0, "buildElapsedMilliseconds": 10 },
              { "project": "src/Targeted/Targeted.csproj", "name": "Targeted", "exitCode": 0, "buildElapsedMilliseconds": 10 },
              { "project": "src/Broken/Broken.csproj", "name": "Broken", "exitCode": 1, "buildElapsedMilliseconds": 10 }
            ]
            """);

        var projects = PerfSelf.Load(results, builds);
        var summary = PerfSelf.Summarize(projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(projects.Select(static project => project.Name),
                Is.EqualTo(s_expectedNames));
            Assert.That(summary, Does.Contain("| Broken | no result | 1 | 0 | 0 | 0 |"));
            Assert.That(summary, Does.Contain("| Published | Complete | 0 | 1 | 5 | 1 |"));
            Assert.That(summary, Does.Contain("2 of 3 projects published a result"));
        }
    }

    [Test]
    public void MissingInputsYieldAnEmptySummary()
    {
        using var temporary = new TempDirectory("SharpProof-perf-self-empty-");

        var projects = PerfSelf.Load(Path.Combine(temporary.FullName, "self"), Path.Combine(temporary.FullName, "missing.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(projects, Is.Empty);
            Assert.That(PerfSelf.Summarize(projects), Does.Contain("0 of 0 projects published a result"));
        }
    }

    private static PerfSelfProject Project(string name, int exitCode, WorkerRunStatus status, params WorkerCallableCost[] costs)
    {
        return new() { Name = name, BuildExitCode = exitCode, Response = Response(status, costs) };
    }

    private static WorkerVerifyResponse Response(WorkerRunStatus status, params WorkerCallableCost[] costs)
    {
        return new()
        {
            RunStatus = status,
            Summary = new() { Costs = new() { Callables = [.. costs] } }
        };
    }

    private static WorkerCallableCost Cost(string id, long rlimit, long milliseconds)
    {
        return new() { CallableId = id, ConsumedRlimit = rlimit, ElapsedMilliseconds = milliseconds };
    }
}
