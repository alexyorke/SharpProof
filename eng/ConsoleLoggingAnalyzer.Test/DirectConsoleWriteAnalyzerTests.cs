using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ConsoleLoggingAnalyzer.Test;

[TestFixture]
public sealed class DirectConsoleWriteAnalyzerTests
{
    [TestCase("System.Console.WriteLine(1);")]
    [TestCase("System.Console.Out.WriteLine(1);")]
    [TestCase("((System.IO.TextWriter)System.Console.Out).WriteLine(1);")]
    [TestCase("System.Console.Error.WriteLineAsync(\"failure\");")]
    public async Task DirectConsoleLineWritesAreRejected(string call)
    {
        var diagnostics = await AnalyzeAsync("class Subject { void Run() { " + call + " } }");
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(new[] { DirectConsoleWriteAnalyzer.DiagnosticId }));
    }

    [Test]
    public void InitializationRejectsNullContext()
    {
        Action initialize = () => new DirectConsoleWriteAnalyzer().Initialize(null!);
        Assert.Throws<ArgumentNullException>(initialize);
    }

    [Test]
    public async Task CompilationsWithoutConsoleMetadataAreAllowed()
    {
        var diagnostics = await AnalyzeAsync(
            "class Writer { void WriteLine() {} void Run() { WriteLine(); } }",
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task InjectedWritersAndLoggerCallsAreAllowed()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using System.IO;
            class Subject {
                void Run(TextWriter writer) {
                    writer.WriteLine("line");
                    writer.WriteLineAsync("line");
                    new Logger(writer).Info("line");
                }
            }
            class Logger {
                private readonly TextWriter writer;
                internal Logger(TextWriter writer) => this.writer = writer;
                internal void Info(string text) => writer.WriteLine(text);
            }
            """);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task UnrelatedConsoleNamedTypeIsAllowed()
    {
        var diagnostics = await AnalyzeAsync(
            "class Console { internal static void WriteLine() {} } class Subject { void Run() => Console.WriteLine(); }");
        Assert.That(diagnostics, Is.Empty);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzeAsync(source, TestMetadataReferences.Platform);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source, ImmutableArray<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            "ConsoleLoggingFixture",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return await compilation.WithAnalyzers(
            [new DirectConsoleWriteAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }
}
