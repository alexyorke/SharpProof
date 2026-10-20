using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275ActualCompilerCaptureCacheTests
{
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public void ActualTreeTextLengthsBecomeAvailableOnlyAfterOwnedCapture(int treeCount, bool supplyActualText)
    {
        var compilation = TestCompilation.Create("Pr275ActualCaptureCache",
            Enumerable.Range(0, treeCount).Select(index =>
                ($"Subject{index}.cs", $"public static class Subject{index} {{ public static int Value() => {index}; }}")).ToImmutableArray());
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var trees = compilation.SyntaxTrees;
        for (var ordinal = 0; ordinal < treeCount; ordinal++)
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, ordinal, out var length), Is.False);
            Assert.That(length, Is.Zero);
        }

        var selectedOrdinal = treeCount - 1;
        var captured = supplyActualText
            ? CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken,
                selectedOrdinal, trees[selectedOrdinal].GetText())
            : CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), Is.SameAs(captured));
        Assert.That(captured, Has.Count.EqualTo(treeCount));
        for (var ordinal = 0; ordinal < treeCount; ordinal++)
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, ordinal, out var length), Is.True);
            Assert.That(length, Is.EqualTo(trees[ordinal].GetText().Length));
            Assert.That(captured[ordinal].TextLength, Is.EqualTo(length));
            Assert.That(captured[ordinal].Sha256,
                Is.EqualTo(CompilerCompilationCapture.CaptureTree(trees[ordinal], TestContext.CurrentContext.CancellationToken).Sha256));
        }
        foreach (var invalidOrdinal in ImmutableArray.Create(-1, treeCount, int.MaxValue))
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, invalidOrdinal, out var length), Is.False);
            Assert.That(length, Is.Zero);
        }
    }

    [TestCase(2)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(9)]
    public void EnhancedLineDirectivesRetainActualRoslynCharacterOffsets(int offset)
    {
        var source = $"#line (10,2)-(10,80) {offset} \"original.dsl\"\n" +
            new string(' ', offset) + "public static class Subject { public static int Value() => 7; }\n#line default\n";
        var compilation = TestCompilation.CreateExact("Pr275ActualEnhancedLine", [("generated.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var mapping = tree.GetLineMappings().Single(static item => item.CharacterOffset.HasValue);
        var captured = CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken).Single();
        var line = tree.GetText().Lines[mapping.Span.Start.Line];
        var entry = captured.LineMap.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.SourceStart == line.Start));

        Assert.That(mapping.CharacterOffset, Is.EqualTo(offset));
        Assert.That(entry.CharacterOffset, Is.EqualTo(mapping.CharacterOffset));
        Assert.That(entry.SourceLength, Is.EqualTo(line.Span.Length));
        Assert.That(entry.MappedPath, Is.EqualTo("original.dsl"));
        Assert.That(entry.MappedLine, Is.EqualTo(tree.GetMappedLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(line.Start, 0))
            .StartLinePosition.Line));
        Assert.That(captured.TextLength, Is.EqualTo(tree.GetText().Length));
    }

    [Test]
    public void OriginalExplicitZeroLineOffsetIsRejectedByTheActualSdk()
    {
        const string source = "#line (10,2)-(10,80) 0 \"original.dsl\"\n" +
            "public static class Subject { public static int Value() => 7; }\n#line default\n";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "generated.cs");
        var compilation = CSharpCompilation.Create("Pr275ActualRejectedZeroOffset", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS8938"));
        Assert.That(errors[0].Location.SourceTree, Is.SameAs(tree));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
    }
}
