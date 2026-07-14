using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcreteDispatchLoopTests
{
    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualMethodDispatch_DoWhileFalseAssignedExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy,
            "Process(int value)", """
Worker worker;
do
{
    worker = new ExactWorker();
} while (false);

return worker.Compute(value);
""");
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualPropertyDispatch_DoWhileFalseAssignedExactConcreteLocal_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy,
            "ReadValue()", """
BaseValue value;
do
{
    value = new ExactValue();
} while (false);

return value.Value;
""");
    }

    [TestCaseSource(nameof(Scenarios))]
    public async Task ExactConcreteDispatchLoop_NoDiagnostic(string hierarchy, string signature, string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(ExactConcreteDispatchTestSources.CreateSource(hierarchy, signature, body));
    }
}
