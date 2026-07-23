using NUnit.Framework;
namespace SharpProof.Test;
[TestFixture]
public sealed class ContractConditionConsolidationTests {
    public sealed record ContractCase(string Name, string Source, params string[] ExpectedDiagnostics);
    private static IEnumerable<TestCaseData> Cases() {
        yield return Case(
            "Valid",
            """
            using SharpProof.Attributes;
            public static class C {
                [Requires("value > 0")]
                [Ensures("result > 0")]
                public static int Identity(int value) => value;
                public static int Call() => Identity(1);
            }
            """);
        yield return Case(
            "Invalid",
            """
            using SharpProof.Attributes;
            public static class C {
                [Ensures("")]
                [Requires("")]
                public static int M() => 1;
            }
            """,
            "SP0024@4[]: SharpProof contract '[Requires]' has invalid argument '\"\"': condition must not be empty",
            "SP0024@3[]: SharpProof contract '[Ensures]' has invalid argument '\"\"': condition must not be empty");
        yield return Case(
            "Unsupported",
            """
            using SharpProof.Attributes;
            public static class C {
                [Requires("result > 0")]
                public static void M() { }
            }
            """,
            "SP0028@3[]: Precondition 'result > 0' for 'C.M()' could not be verified: result placeholder is not supported in [Requires] conditions");
        yield return Case(
            "PropertyAccessor",
            """
            using SharpProof.Attributes;
            public static class C {
                [Ensures("result > 0")]
                public static int P { get; } = 1;
            }
            """,
            "SP0019@3[]: Method 'get_P' is marked [Ensures], but postcondition 'result > 0' could not be verified: auto-property getter result is not source-visible for [Ensures] verification");
        yield return Case(
            "Inherited",
            """
            using SharpProof.Attributes;
            public class B {
                [Requires("value > 0")]
                public virtual void M(int value) { }
            }
            public sealed class D : B {
                public override void M(int value) { }
                public void Call() => M(0);
            }
            """,
            "SP0027@8[3]: Call to 'D.M(int)' does not prove precondition 'value > 0'");
        yield return Case(
            "Duplicate",
            """
            using SharpProof.Attributes;
            public static class C {
                [Requires("value > 0")]
                [Requires("value > 0")]
                public static void M(int value) { }
                public static void Call() => M(0);
            }
            """,
            "SP0027@6[3]: Call to 'C.M(int)' does not prove precondition 'value > 0'");
    }
    [TestCaseSource(nameof(Cases))]
    public async Task ContractDiagnosticsRemainStable(ContractCase testCase) {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(testCase.Source);
        Assert.That(diagnostics.Select(Format), Is.EqualTo(testCase.ExpectedDiagnostics));
    }
    private static TestCaseData Case(string name, string source, params string[] expectedDiagnostics) =>
        new TestCaseData(new ContractCase(name, source, expectedDiagnostics)).SetName(name);
    private static string Format(Microsoft.CodeAnalysis.Diagnostic diagnostic) {
        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        var additionalLines = diagnostic.AdditionalLocations.Select(
            static location => (location.GetLineSpan().StartLinePosition.Line + 1).ToString());
        return diagnostic.Id + "@" + line + "[" + string.Join(",", additionalLines) + "]: " + diagnostic.GetMessage();
    }
}
