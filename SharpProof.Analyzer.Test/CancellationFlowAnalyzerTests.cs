using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.DefensiveCodingAnalyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CancellationFlowAnalyzerTests
{
    [Test]
    public async Task ReportsAsyncMethodsWithoutTokens()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync() => await Task.Delay(1);
                public static ValueTask<int> ValueAsync() => new(1);
                public static async IAsyncEnumerable<int> StreamAsync()
                {
                    yield return 1;
                    await Task.Yield();
                }
                public static int Work() => 1;
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(ImmutableArray.Create(
                CancellationFlowAnalyzer.MissingTokenId,
                CancellationFlowAnalyzer.MissingTokenId,
                CancellationFlowAnalyzer.MissingTokenId)));
    }

    [Test]
    public async Task ReportsOmittedAndDisabledTokensAtAsyncCalls()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                private static Task OptionalAsync(int value, CancellationToken token = default) => Task.CompletedTask;
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(1);
                    await Task.Delay(1, CancellationToken.None);
                    await Task.Delay(1, default);
                    await OptionalAsync(1);
                    await Task.Delay(1, cancellationToken);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == CancellationFlowAnalyzer.DroppedTokenId), Is.EqualTo(4));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == CancellationFlowAnalyzer.OptionalTokenId), Is.EqualTo(1));
    }

    [Test]
    public async Task RejectsOptionalTokensEvenInSynchronousTestHelpers()
    {
        const string source = """
            using System.Threading;
            public static class Sample
            {
                public static void Work(CancellationToken token = default) { }
            }
            """;

        var diagnostics = await Analyze(source, assemblyName: "Sample.Test");
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.OptionalTokenId]));
    }

    [Test]
    public async Task AllowsRuntimeEntryPointAndGeneratedOrTestCode()
    {
        const string source = """
            using System.Threading.Tasks;
            public static class Sample
            {
                private static async Task<int> Main() { await Task.Yield(); return 0; }
            }
            """;

        Assert.That(await Analyze(source, isExe: true), Is.Empty);
        Assert.That(await Analyze("public static class Sample { public static int Value() => 1; }",
            assemblyName: "Sample.Test"), Is.Empty);
        Assert.That(await Analyze("using System.Threading.Tasks; public static class Sample { public static async Task WorkAsync() => await Task.Yield(); }",
            filePath: "Sample.g.cs"), Is.Empty);
    }

    private static async Task<Diagnostic[]> Analyze(string source,
        string filePath = "input.cs", string? assemblyName = null, bool isExe = false)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: filePath);
        if (isExe)
        {
            compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
        }
        if (assemblyName != null)
        {
            compilation = compilation.WithAssemblyName(assemblyName);
        }
        return (await compilation.WithAnalyzers([new CancellationFlowAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()).ToArray();
    }
}
