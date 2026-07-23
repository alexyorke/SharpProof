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
        """, "SP0021", null, TestName = "ExpectedComplexityExceededReportsSP0021")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [ExpectedComplexity(ComplexityKind.Linear)]
            public static void M() => _ = System.Environment.GetEnvironmentVariable("PATH");
        }
        """, "SP0022", null, TestName = "ExpectedComplexityUnknownReportsSP0022")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [ZeroAllocations]
            public static object M(System.Type type) => System.Activator.CreateInstance(type)!;
        }
        """, "SP0045", "SP0013", TestName = "ZeroAllocationsUnknownReportsSP0045")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [DoesNotThrow]
            public static object M(System.Type type) => System.Activator.CreateInstance(type)!;
        }
        """, "SP0046", "SP0030", TestName = "ExceptionUnknownReportsSP0046")]
    [TestCase("""
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        public sealed class C {
            private string? Current => System.DateTime.Now.Ticks == 0 ? null : "value";
            [MemberNotNull(nameof(Current))]
            public void Initialize() { }
        }
        """, "SP0047", null, TestName = "UserDefinedNullableMemberTargetReportsSP0047")]
    [TestCase("""
        using SharpProof.Attributes;
        public static class C {
            [EffectContract((SharpProofEffect)1073741824, Complete = true)]
            private static extern void Boundary();
            [EnforcePure]
            public static void M() => Boundary();
        }
        """, "SP0025", null, TestName = "MalformedEffectFlagsReportSP0025")]
    public async Task UnknownContractDiagnostics(
        string source,
        string requiredDiagnosticId,
        string? forbiddenDiagnosticId) {
        var diagnosticIds = (await AnalyzerTestHost.GetDiagnosticsAsync(source)).Select(static value => value.Id);
        Assert.Multiple(() => {
            Assert.That(diagnosticIds, Does.Contain(requiredDiagnosticId));
            if (forbiddenDiagnosticId != null)
                Assert.That(diagnosticIds, Does.Not.Contain(forbiddenDiagnosticId));
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
}
