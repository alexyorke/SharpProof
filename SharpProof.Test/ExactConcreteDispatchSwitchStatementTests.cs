using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcreteDispatchSwitchStatementTests
{
    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualMethodDispatch_SameConcreteSwitchStatementMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualMethodHierarchy,
            "Process(int selector, int value)", CreateSwitchBody("Worker", "worker", "new ExactWorker()",
                "return worker.Compute(value);"));
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualPropertyDispatch_SameConcreteSwitchStatementMerge_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy,
            "ReadValue(int selector)", CreateSwitchBody("BaseValue", "value", "new ExactValue()",
                "return value.Value;"));
    }

    private static string CreateSwitchBody(string type, string variable, string value, string result)
    {
        return $@"{type} {variable};
switch (selector)
{{
    case 0:
        {variable} = {value};
        break;
    case 1:
        {variable} = {value};
        break;
    default:
        {variable} = {value};
        break;
}}

{result}";
    }

    [TestCaseSource(nameof(Scenarios))]
    public async Task ExactConcreteDispatchSwitch_NoDiagnostic(string hierarchy, string signature, string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(ExactConcreteDispatchTestSources.CreateSource(hierarchy, signature, body));
    }
}
