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
            """);
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
        yield return Case(
            "PropertyGetter",
            """
            using SharpProof.Attributes;
            public static class C {
                public static int P {
                    [Requires("false")]
                    get => 1;
                }
                public static int Read() => P;
            }
            """,
            "SP0027@7[4]: Call to 'C.P.get' does not prove precondition 'false'");
        yield return Case(
            "PropertySetter",
            """
            using SharpProof.Attributes;
            public static class C {
                private static int _value;
                public static int P {
                    get => _value;
                    [Requires("value > 0")]
                    set => _value = value;
                }
                public static void Write() => P = 0;
            }
            """,
            "SP0027@9[6]: Call to 'C.P.set' does not prove precondition 'value > 0'");
        yield return Case(
            "NamedArguments",
            """
            using SharpProof.Attributes;
            public static class C {
                [Requires("left > right")]
                public static void M(int left, int right) { }
                public static void Call() => M(right: 2, left: 1);
            }
            """,
            "SP0027@5[3]: Call to 'C.M(int, int)' does not prove precondition 'left > right'");
    }
    [TestCaseSource(nameof(Cases))]
    public async Task ContractDiagnosticsRemainStable(ContractCase testCase) {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(testCase.Source);
        Assert.That(diagnostics.Select(Format), Is.EqualTo(testCase.ExpectedDiagnostics));
    }
    [Test]
    public async Task InterfaceRequiresContractMapsRenamedImplementationParameter() {
        var diagnosticIds = (await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public interface IWorker {
                [Requires("input > 0")]
                void Work(int input);
            }
            public sealed class Worker : IWorker {
                public void Work(int value) { }
                public void Call() => Work(0);
            }
            """)).Select(static diagnostic => diagnostic.Id);
        Assert.Multiple(() => {
            Assert.That(diagnosticIds, Does.Contain("SP0027"));
            Assert.That(diagnosticIds, Does.Not.Contain("SP0028"));
        });
    }
    [Test]
    public async Task InterfaceEnsuresContractMapsRenamedImplementationParameter() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public interface IWorker {
                [Ensures("result == input")]
                int Identity(int input);
            }
            public sealed class Worker : IWorker {
                public int Identity(int value) => value;
            }
            """);
        var diagnosticIds = diagnostics.Select(static diagnostic => diagnostic.Id);
        Assert.Multiple(() => {
            var message = string.Join(Environment.NewLine, diagnostics);
            Assert.That(diagnosticIds, Does.Not.Contain("SP0018"), message);
            Assert.That(diagnosticIds, Does.Not.Contain("SP0019"), message);
        });
    }
    [Test]
    public async Task InterfaceEnsuresMapsRenamedRequiresAssumptionParameter() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public interface IWorker {
                [Requires("input > 0")]
                [Ensures("result > 0")]
                int Identity(int input);
            }
            public sealed class Worker : IWorker {
                public int Identity(int value) => value;
            }
            """);
        var diagnosticIds = diagnostics.Select(static diagnostic => diagnostic.Id);
        Assert.Multiple(() => {
            var message = string.Join(Environment.NewLine, diagnostics);
            Assert.That(diagnosticIds, Does.Not.Contain("SP0018"), message);
            Assert.That(diagnosticIds, Does.Not.Contain("SP0019"), message);
        });
    }
    [Test]
    public async Task IdenticalRequiresTextPreservesDistinctSourceParameterBindings() {
        var diagnosticIds = (await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public interface IWorker {
                [Requires("value > 0")]
                void Work(int value, int other);
            }
            public sealed class Worker : IWorker {
                [Requires("value > 0")]
                public void Work(int other, int value) { }
                public void Call() => Work(0, 1);
            }
            """)).Select(static diagnostic => diagnostic.Id);
        Assert.That(diagnosticIds, Does.Contain("SP0027"));
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
