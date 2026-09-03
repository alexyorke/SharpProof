namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class ArgumentNullGuardBoundaryTests
{
    [TestCaseSource(nameof(GuardCases))]
    public void PublicAndInternalGuardsPreserveEveryParameterName(
        string expectedParameterName,
        Action guard)
    {
        var error = Assert.Throws<ArgumentNullException>(guard);

        Assert.That(error!.ParamName, Is.EqualTo(expectedParameterName));
    }

    private static IEnumerable<TestCaseData> GuardCases()
    {
        yield return new TestCaseData(
            "transfer",
            (Action)(() =>
            {
                _ = new DataflowBlock<NullnessValue>(0, null!);
            }));
        yield return new TestCaseData(
            "blocks",
            (Action)(() =>
            {
                _ = new DataflowGraph<NullnessValue>(null!, []);
            }));
        yield return new TestCaseData(
            "edges",
            (Action)(() =>
            {
                _ = new DataflowGraph<NullnessValue>(
                    [new(0, static value => value)],
                    null!);
            }));
        yield return new TestCaseData(
            "graph",
            (Action)(() => ForwardDataflowAnalysis.Analyze(
                null!,
                NullnessDomain.Instance,
                NullnessValue.MaybeNull)));
        yield return new TestCaseData(
            "domain",
            (Action)(() => ForwardDataflowAnalysis.Analyze(
                CreateGraph(),
                null!,
                NullnessValue.MaybeNull)));
        yield return new TestCaseData(
            "options",
            (Action)(() =>
                ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting(
                    CreateGraph(),
                    NullnessDomain.Instance,
                    NullnessValue.MaybeNull,
                    null!,
                    static pending => pending)));
        yield return new TestCaseData(
            "worklistOrder",
            (Action)(() =>
                ForwardDataflowAnalysis.AnalyzeWithWorklistOrderForTesting(
                    CreateGraph(),
                    NullnessDomain.Instance,
                    NullnessValue.MaybeNull,
                    new ForwardDataflowAnalysisOptions(),
                    null!)));
    }

    private static DataflowGraph<NullnessValue> CreateGraph()
    {
        return new DataflowGraph<NullnessValue>(
            [new(0, static value => value)],
            []);
    }
}
