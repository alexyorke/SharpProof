namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class ArgumentNullGuardBoundaryTests
{
    private static readonly string[] ExpectedParameterNames =
    [
        "transfer",
        "blocks",
        "edges",
        "graph",
        "domain",
        "options",
        "worklistOrder"
    ];

    [Test]
    public void PublicAndInternalGuardsPreserveEveryParameterName()
    {
        var domain = NullnessDomain.Instance;
        var graph = new DataflowGraph<NullnessValue>(
            [new(0, static value => value)],
            []);
        var options = new ForwardDataflowAnalysisOptions();

        var errors = new[]
        {
            Assert.Throws<ArgumentNullException>(
                (Action)(() =>
                {
                    _ = new DataflowBlock<NullnessValue>(0, null!);
                })),
            Assert.Throws<ArgumentNullException>(
                (Action)(() =>
                {
                    _ = new DataflowGraph<NullnessValue>(null!, []);
                })),
            Assert.Throws<ArgumentNullException>(
                (Action)(() =>
                {
                    _ = new DataflowGraph<NullnessValue>(
                        [new(0, static value => value)],
                        null!);
                })),
            Assert.Throws<ArgumentNullException>(
                (Action)(() => ForwardDataflowAnalysis.Analyze(
                    null!,
                    domain,
                    NullnessValue.MaybeNull))),
            Assert.Throws<ArgumentNullException>(
                (Action)(() => ForwardDataflowAnalysis.Analyze(
                    graph,
                    null!,
                    NullnessValue.MaybeNull))),
            Assert.Throws<ArgumentNullException>(
                (Action)(() => ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting(
                    graph,
                    domain,
                    NullnessValue.MaybeNull,
                    null!,
                    static pending => pending))),
            Assert.Throws<ArgumentNullException>(
                (Action)(() => ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting(
                    graph,
                    domain,
                    NullnessValue.MaybeNull,
                    options,
                    null!)))
        };

        Assert.That(
            errors.Select(static error => error!.ParamName),
            Is.EqualTo(ExpectedParameterNames));
    }
}
