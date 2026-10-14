using Microsoft.CodeAnalysis;
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
                ($"Subject{index}.cs", $"public static class Subject{index} {{ public static int Value() => {index}; }}")).ToArray());
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var trees = compilation.SyntaxTrees.ToArray();
        for (var ordinal = 0; ordinal < treeCount; ordinal++)
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, ordinal, out var length), Is.False);
            Assert.That(length, Is.Zero);
        }

        var selectedOrdinal = treeCount - 1;
        var captured = supplyActualText
            ? CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None,
                selectedOrdinal, trees[selectedOrdinal].GetText())
            : CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None);
        Assert.That(CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), Is.SameAs(captured));
        Assert.That(captured, Has.Length.EqualTo(treeCount));
        for (var ordinal = 0; ordinal < treeCount; ordinal++)
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, ordinal, out var length), Is.True);
            Assert.That(length, Is.EqualTo(trees[ordinal].GetText().Length));
            Assert.That(captured[ordinal].TextLength, Is.EqualTo(length));
            Assert.That(captured[ordinal].Sha256,
                Is.EqualTo(CompilerCompilationCapture.CaptureTree(trees[ordinal], CancellationToken.None).Sha256));
        }
        foreach (var invalidOrdinal in new[] { -1, treeCount, int.MaxValue })
        {
            Assert.That(CompilerCompilationCapture.TryGetCapturedTreeTextLength(compilation, invalidOrdinal, out var length), Is.False);
            Assert.That(length, Is.Zero);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(9)]
    public void EnhancedLineDirectivesRetainActualRoslynCharacterOffsets(int offset)
    {
        var source = $"#line (10,2)-(10,80) {offset} \"original.dsl\"\n" +
            new string(' ', offset) + "public static class Subject { public static int Value() => 7; }\n#line default\n";
        var compilation = TestCompilation.Create("Pr275ActualEnhancedLine", ("generated.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var mapping = tree.GetLineMappings().Single(static item => item.CharacterOffset.HasValue);
        var captured = CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None).Single();
        var line = tree.GetText().Lines[mapping.Span.Start.Line];
        var entry = captured.LineMap.Single(item => item.SourceStart == line.Start);

        Assert.That(mapping.CharacterOffset, Is.EqualTo(offset));
        Assert.That(entry.CharacterOffset, Is.EqualTo(mapping.CharacterOffset));
        Assert.That(entry.SourceLength, Is.EqualTo(line.Span.Length));
        Assert.That(entry.MappedPath, Is.EqualTo("original.dsl"));
        Assert.That(entry.MappedLine, Is.EqualTo(tree.GetMappedLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(line.Start, 0))
            .StartLinePosition.Line));
        Assert.That(captured.TextLength, Is.EqualTo(tree.GetText().Length));
    }
}
