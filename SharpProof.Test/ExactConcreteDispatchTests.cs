using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcreteDispatchTests
{
    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return ExactConcreteDispatchTestSources.Scenario(
            "InterfaceDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic",
            ExactConcreteDispatchTestSources.InterfaceMethodHierarchy,
            "Process(int value)",
            "IWorker worker = new ExactWorker();\nreturn worker.Compute(value);");
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy,
            "Process(int value)",
            "Worker worker = new ExactWorker();\nreturn worker.Compute(value);");
    }

    [TestCaseSource(nameof(Scenarios))]
    public async Task ExactConcreteDispatch_NoDiagnostic(string hierarchy, string signature, string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(ExactConcreteDispatchTestSources.CreateSource(hierarchy, signature, body));
    }
}
