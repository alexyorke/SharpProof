using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Count(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == CancellationFlowAnalyzer.DroppedTokenId)), Is.EqualTo(4));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == CancellationFlowAnalyzer.NoTokenId)), Is.EqualTo(1));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == CancellationFlowAnalyzer.OptionalTokenId)), Is.EqualTo(1));
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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

    [Test]
    public async Task LocalFunctionsRespectTestAssembliesAndStaticCaptureBoundaries()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static Task RunAsync(CancellationToken outer)
                {
                    static Task BoundaryAsync(CancellationToken token)
                    {
                        async Task NestedAsync() { await Task.Delay(1, token); }
                        return NestedAsync();
                    }
                    return BoundaryAsync(outer);
                }
            }
            """;
        Assert.That(await Analyze(source), Is.Empty);
        Assert.That(await Analyze(source, assemblyName: "Sample.Test"), Is.Empty);
    }

    [Test]
    public async Task NonAwaitableAsyncNamesDoNotRequireTokens()
    {
        Assert.That(await Analyze("public static class Sample { public static T ValueAsync<T>(T value) => value; public static int[] ValuesAsync() => []; }"), Is.Empty);
    }

    [Test]
    public async Task LinkedTokenArraysAndParenthesesPreserveFlow()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task RunAsync(CancellationToken token)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken[] { (token) });
                    await Task.Delay(1, (linked.Token));
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync(TestContext.CurrentContext.CancellationToken);
        var declaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(declaration)!;
        Assert.That(operation.DescendantsAndSelf().Any(static child =>
            child is null ? throw new ArgumentNullException(nameof(child)) : child.Kind == OperationKind.Parenthesized), Is.False);
        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task DeepAliasesAreConservativelyReportedInsteadOfAssumedCancellable()
    {
        var declarations = string.Join("\n", Enumerable.Range(1, 20).Select(static index => $"var alias{index} = alias{index - 1};"));
        var source = $$"""
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task RunAsync(CancellationToken token)
                {
                    var alias0 = token;
                    {{declarations}}
                    await Task.Delay(1, alias20);
                }
            }
            """;
        Assert.That((await Analyze(source)).Select(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id),
            Is.EqualTo(ImmutableArray.Create(CancellationFlowAnalyzer.DroppedTokenId)));
    }

    [Test]
    public async Task CyclicOrRefModifiedAliasesDoNotInventCancellationFlow()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static Task CycleAsync(CancellationToken token)
                {
                    CancellationToken first = second;
                    CancellationToken second = first;
                    return Task.Delay(1, second);
                }
                public static Task RefAsync(CancellationToken token)
                {
                    var alias = token;
                    Change(ref alias);
                    return Task.Delay(1, alias);
                }
                private static void Change(ref CancellationToken token) { }
            }
            """;
        Assert.That((await Analyze(source)).Select(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id),
            Is.EqualTo(ImmutableArray.Create(CancellationFlowAnalyzer.DroppedTokenId, CancellationFlowAnalyzer.DroppedTokenId, CancellationFlowAnalyzer.DroppedTokenId)));
    }

    [Test]
    public async Task CapturedFieldLambdaAliasesCannotReplaceAnExplicitLocalToken()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public static class Sample
            {
                public static Func<CancellationToken, Task> Callback = token =>
                {
                    var alias = token;
                    Task LocalAsync(CancellationToken active) => Task.Delay(1, alias);
                    return LocalAsync(token);
                };
            }
            """;
        Assert.That((await Analyze(source)).Select(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id),
            Is.EqualTo(ImmutableArray.Create(CancellationFlowAnalyzer.DroppedTokenId)));
    }

    private static async Task<Diagnostic[]> Analyze(string source,
        string filePath = "input.cs", string? assemblyName = null, bool isExe = false)
    {
        System.ArgumentNullException.ThrowIfNull(filePath);
        System.ArgumentNullException.ThrowIfNull(source);
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
