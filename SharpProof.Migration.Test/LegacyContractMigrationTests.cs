using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Migration.Test;

[TestFixture]
public sealed class LegacyContractMigrationTests {
    [Test]
    public async Task BlockMethodMigratesRequiresEnsuresResultAndOldInOrder() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                [Requires("value > 0")]
                [Ensures("result > old((value))")]
                public static long Advance(long value) {
                    return value + 1;
                }
            }
            """;
        var result = await ApplyAsync(source);

        Assert.That(result.ActionCount, Is.EqualTo(1));
        Assert.That(result.Source, Does.Not.Contain("[Requires"));
        Assert.That(result.Source, Does.Not.Contain("[Ensures"));
        Assert.That(
            result.Source.IndexOf("Contract.Requires", StringComparison.Ordinal),
            Is.LessThan(result.Source.IndexOf(
                "Contract.Ensures",
                StringComparison.Ordinal)));
        Assert.That(
            result.Source,
            Does.Contain(
                "Contract.Requires(value > 0);"));
        Assert.That(
            result.Source,
            Does.Contain(
                "Contract.Ensures(Contract.Result<long>() > Contract.Old((value)));"));
        Assert.That(result.Source, Does.Contain("return value + 1;"));
    }

    [Test]
    public async Task ExpressionBodyPreservesEscapedIdentifierAndPrecedence() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                [Requires("@class > 0 && (@class + 1) < 10")]
                public static long Read(long @class) => /* retain arrow */ @class;
            }
            """;
        var result = await ApplyAsync(source);

        Assert.That(result.ActionCount, Is.EqualTo(1));
        Assert.That(
            result.Source,
            Does.Contain(
                "Contract.Requires(@class > 0 && (@class + 1) < 10);"));
        Assert.That(result.Source, Does.Contain("return"));
        Assert.That(result.Source, Does.Contain("@class;"));
        Assert.That(result.Source, Does.Contain("/* retain arrow */"));
        Assert.That(result.Source, Does.Not.Contain("=>"));
    }

    [Test]
    public async Task ConstructorAndUnrelatedAttributeTriviaArePreserved() {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            public sealed class Subject {
                [Obsolete]
                // retain this contract comment
                [Requires("value > 0")]
                public Subject(long value) => Consume(value);
                private static void Consume(long value) { }
            }
            """;
        var result = await ApplyAsync(source);

        Assert.That(result.ActionCount, Is.EqualTo(1));
        Assert.That(result.Source, Does.Contain("[Obsolete]"));
        Assert.That(
            result.Source,
            Does.Contain("// retain this contract comment"));
        Assert.That(
            result.Source,
            Does.Contain("Contract.Requires(value > 0);"));
        Assert.That(result.Source, Does.Contain("Consume(value);"));
    }

    [TestCase("value >", "long value")]
    [TestCase("missing > 0", "long value")]
    [TestCase("Check(value)", "long value")]
    [TestCase("result > 0", "long value")]
    public async Task RequiresRefusesUnsupportedOrAmbiguousText(
        string condition,
        string parameter) {
        var source =
            $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [Requires("{{condition}}")]
                public static long Read({{parameter}}) => 1L;
                private static bool Check(long value) => true;
            }
            """;
        var result = await ApplyAsync(source);
        Assert.That(result.ActionCount, Is.Zero);
    }

    [TestCase("result > 0", "long result")]
    [TestCase("old(old(value)) > 0", "long value")]
    [TestCase("old(result) > 0", "long value")]
    [TestCase("old(output) > 0", "out long output")]
    public async Task EnsuresRefusesAmbiguousOrInvalidPlaceholders(
        string condition,
        string parameter) {
        var source =
            $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [Ensures("{{condition}}")]
                public static long Read({{parameter}}) {
                    {{(parameter.StartsWith("out ", StringComparison.Ordinal) ? "output = 1L;" : "")}}
                    return 1L;
                }
            }
            """;
        var result = await ApplyAsync(source);
        Assert.That(result.ActionCount, Is.Zero);
    }

    [Test]
    public async Task AbstractMethodAndPropertyReceiveNoFix() {
        const string abstractSource =
            """
            using SharpProof.Attributes;
            public abstract class Subject {
                [Requires("value > 0")]
                public abstract long Read(long value);
            }
            """;
        Assert.That(
            (await ApplyAsync(abstractSource)).ActionCount,
            Is.Zero);

        const string propertySource =
            """
            using SharpProof.Attributes;
            public sealed class Subject {
                [Ensures("result > 0")]
                public long Value => 1L;
            }
            """;
        Assert.That(
            (await ApplyAsync(propertySource)).ActionCount,
            Is.Zero);
    }

    [Test]
    public async Task OldPlaceholderRefusesAConflictingMember() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static long old(long value) => value;
                [Ensures("old(value) > 0")]
                public static long Read(long value) => value;
            }
            """;
        Assert.That((await ApplyAsync(source)).ActionCount, Is.Zero);
    }

    [Test]
    public async Task RewrittenDocumentHasNoCompilerErrors() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                [Requires("enabled && count > 0")]
                [Ensures("result >= old(count)")]
                public static long Read(bool enabled, long count) => count;
            }
            """;
        var result = await ApplyAsync(source);
        Assert.That(result.ActionCount, Is.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);
    }

    private static async Task<MigrationResult> ApplyAsync(string source) {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(
            ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "MigrationTest",
                "MigrationTest",
                LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12),
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: GetReferences()));
        var document = workspace.AddDocument(
            project.Id,
            "Subject.cs",
            SourceText.From(source));
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = compilation!.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Id == "CS0618")
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
        Assert.That(diagnostics, Is.Not.Empty);

        var actions = new List<CodeAction>();
        var provider = new LegacyContractMigrationCodeFixProvider();
        var context = new CodeFixContext(
            document,
            diagnostics[0],
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context);
        if (actions.Count == 0)
            return new MigrationResult(source, 0, []);

        var operations = await actions[0].GetOperationsAsync(
            CancellationToken.None);
        var changedSolution = operations
            .OfType<ApplyChangesOperation>()
            .Single()
            .ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        changedDocument = await Simplifier.ReduceAsync(changedDocument);
        changedDocument = await Formatter.FormatAsync(changedDocument);
        var changedText = (await changedDocument.GetTextAsync()).ToString();
        var changedCompilation = await changedDocument.Project
            .GetCompilationAsync();
        var errors = changedCompilation!.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToImmutableArray();
        return new MigrationResult(changedText, actions.Count, errors);
    }

    private static ImmutableArray<MetadataReference> GetReferences() {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Contract).Assembly.Location)
            .Append(Path.Combine(
                AppContext.BaseDirectory,
                "SharpProof.Legacy.Attributes.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(static path =>
            MetadataReference.CreateFromFile(path))];
    }

    private readonly record struct MigrationResult(
        string Source,
        int ActionCount,
        ImmutableArray<string> Errors);
}
