using NUnit.Framework;
namespace SharpProof.Test;
[TestFixture]
public sealed class UnknownContractDiagnosticTests {
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [ExpectedComplexity(ComplexityKind.Linear)]
            public static void M(int n) {
                for (var i = 0; i < n; i++)
                    for (var j = 0; j < n; j++) { }
            }
        }
        """, "SP0021", TestName = "ExpectedComplexityExceededReportsSP0021")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [ExpectedComplexity(ComplexityKind.Linear)]
            public static void M() => _ = System.Environment.GetEnvironmentVariable("PATH");
        }
        """, "SP0022", TestName = "ExpectedComplexityUnknownReportsSP0022")]
    public async Task ExpectedComplexityDiagnostics(string source, string diagnosticId) =>
        Assert.That((await AnalyzerTestHost.GetDiagnosticsAsync(source)).Select(static value => value.Id),
            Does.Contain(diagnosticId));
    [Test]
    public async Task ZeroAllocationsUnknownReportsSP0045() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public static class C {
                [ZeroAllocations]
                public static object M(System.Type type) => System.Activator.CreateInstance(type)!;
            }
            """);
        Assert.Multiple(() => {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0045"));
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain("SP0013"));
        });
    }
    [Test]
    public async Task ExceptionUnknownReportsSP0046() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public static class C {
                [DoesNotThrow]
                public static object M(System.Type type) => System.Activator.CreateInstance(type)!;
            }
            """);
        Assert.Multiple(() => {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0046"));
            Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain("SP0030"));
        });
    }
    [Test]
    public async Task MixedProvenAndUnknownExceptionSiteReportsBothDiagnostics() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public static class C {
                private static void Callee(bool fail, System.Type type) {
                    if (fail) throw new System.InvalidOperationException();
                    _ = System.Activator.CreateInstance(type);
                }
                [DoesNotThrow]
                public static void Caller(bool fail, System.Type type) => Callee(fail, type);
            }
            """);
        var exceptionDiagnostics = diagnostics.Where(static diagnostic => diagnostic.Id is "SP0030" or "SP0046").ToArray();
        Assert.Multiple(() => {
            Assert.That(exceptionDiagnostics.Select(static diagnostic => diagnostic.Id), Is.EqualTo(["SP0030", "SP0046"]));
            Assert.That(exceptionDiagnostics.Select(static diagnostic => diagnostic.Location.SourceSpan).Distinct().Count(), Is.EqualTo(1));
        });
    }
    [Test]
    public async Task UserDefinedNullableMemberTargetReportsSP0047() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public sealed class C {
                private string? Current => System.DateTime.Now.Ticks == 0 ? null : "value";
                [MemberNotNull(nameof(Current))]
                public void Initialize() { }
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0047"));
    }
    [Test]
    public async Task MalformedEffectFlagsReportSP0025() {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
            using SharpProof.Attributes;
            public static class C {
                [EffectContract((SharpProofEffect)1073741824, Complete = true)]
                private static extern void Boundary();
                [EnforcePure]
                public static void M() => Boundary();
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0025"));
    }
}
