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
                    await Task.Delay(1, CancellationToken.DISABLED_TOKEN);
                    await Task.Delay(1, default);
                    await OptionalAsync(1);
                    await Task.Delay(1, cancellationToken);
                }
            }
            """;

        var diagnostics = await Analyze(source.Replace("DISABLED_TOKEN", "None", StringComparison.Ordinal));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == CancellationFlowAnalyzer.DroppedTokenId), Is.EqualTo(4));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == CancellationFlowAnalyzer.NoTokenId), Is.EqualTo(1));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == CancellationFlowAnalyzer.OptionalTokenId), Is.EqualTo(1));
    }

    [Test]
    public async Task ReportsUnrelatedTokenInsteadOfCallerToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(1, new CancellationTokenSource().Token);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.DroppedTokenId]));
    }

    [Test]
    public async Task AllowsCallerTokenAliasesAndLinkedSources()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken, bool choose)
                {
                    var alias = cancellationToken;
                    await Task.Delay(1, alias);
                    await Task.Delay(1, choose ? alias : alias);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(alias);
                    await Task.Delay(1, linked.Token);
                }
            }
            """;

        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task ReportsReassignedTokenAlias()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    var alias = cancellationToken;
                    alias = new CancellationTokenSource().Token;
                    await Task.Delay(1, alias);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.DroppedTokenId]));
    }

    [Test]
    public async Task AllowsLocalFunctionToCaptureEnclosingToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    async Task LocalAsync() => await Task.Delay(1, cancellationToken);
                    await LocalAsync();
                }
            }
            """;

        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task ReportsDroppedCapturedTokenInsideLocalFunction()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    async Task LocalAsync() => await Task.Delay(1, new CancellationTokenSource().Token);
                    await LocalAsync();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.DroppedTokenId]));
    }

    [Test]
    public async Task ReportsStaticAsyncLocalFunctionWithoutToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task WorkAsync(CancellationToken cancellationToken)
                {
                    static async Task LocalAsync() => await Task.Yield();
                    await LocalAsync();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.MissingTokenId]));
    }

    [Test]
    public async Task RejectsDisabledTokensInTestAssemblies()
    {
        const string source = """
            using System.Threading;
            public static class Sample
            {
                public static CancellationToken Token() => CancellationToken.DISABLED_TOKEN;
            }
            """;

        var diagnostics = await Analyze(
            source.Replace("DISABLED_TOKEN", "None", StringComparison.Ordinal),
            assemblyName: "Sample.Test");
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.NoTokenId]));
    }

    [Test]
    public async Task RejectsStaticallyImportedDisabledToken()
    {
        const string source = """
            using System.Threading;
            using static System.Threading.CancellationToken;
            public static class Sample
            {
                public static CancellationToken Token() => None;
            }
            """;

        var diagnostics = await Analyze(source, assemblyName: "Sample.Test");
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([CancellationFlowAnalyzer.NoTokenId]));
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
