using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcretePropertyDispatchTests
{
    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return ExactConcreteDispatchTestSources.Scenario(
            "InterfacePropertyDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic",
            ExactConcreteDispatchTestSources.InterfacePropertyHierarchy,
            "ReadValue()", "IValueProvider provider = new ExactValueProvider();\nreturn provider.Value;");
        yield return ExactConcreteDispatchTestSources.Scenario(
            "VirtualPropertyDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic",
            ExactConcreteDispatchTestSources.VirtualPropertyHierarchy,
            "ReadValue()", "BaseValue value = new ExactValue();\nreturn value.Value;");
    }

    [TestCaseSource(nameof(Scenarios))]
    public async Task ExactConcretePropertyDispatch_NoDiagnostic(string hierarchy, string signature, string body)
    {
        await VerifyCS.VerifyAnalyzerAsync(ExactConcreteDispatchTestSources.CreateSource(hierarchy, signature, body));
    }
}
