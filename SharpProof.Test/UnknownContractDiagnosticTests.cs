using NUnit.Framework;
namespace SharpProof.Test;
[TestFixture]
public sealed class UnknownContractDiagnosticTests {
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
