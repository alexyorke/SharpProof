using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreCompanionCoverageTests
{
    [Test]
    public async Task DuplicateCompanionMembersReportAnAmbiguousMapping()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public interface IService { int Map(int value); }
            [ContractFor(typeof(IService))]
            public static class Contracts
            {
                public static int Map(IService receiver, int value) => value;
                public static int Map(IService receiver, int value) => value;
            }
            """, ["SPCF0006"]);
        AnalyzerTestHost.AssertIds(compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), "CS0111");
        Assert.That(compilation.GetTypeByMetadataName("Contracts")!.GetMembers("Map"), Has.Length.EqualTo(2));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", allowCompilationErrors: true);
        AnalyzerTestHost.AssertIds(diagnostics, "SPCF0006");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "mapping for method 'Map' is ambiguous");
    }

    [Test]
    public async Task DuplicateTargetAttributesDoNotSelectACompanionTarget()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public interface IService { int Map(int value); }
            [ContractFor(typeof(IService)), ContractFor(typeof(IService))]
            public static class Contracts
            {
                public static int Map(IService receiver, int value) => value;
            }
            """, ["SPCF0001"]);
        AnalyzerTestHost.AssertIds(compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), "CS0579");
        Assert.That(compilation.GetTypeByMetadataName("Contracts")!.GetAttributes(), Has.Length.EqualTo(2));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", allowCompilationErrors: true);
        AnalyzerTestHost.AssertIds(diagnostics, "SPCF0001");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "does not identify one resolvable named target type");
    }
}
