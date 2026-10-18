using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GoldenSourceAnchorsTests
{
    [Test]
    public void AnchorFollowsCodeThroughWhitespaceCommentsAndUnrelatedDeclarations()
    {
        const string original = "class C { int M(int x) { return x + 1; } }";
        const string moved = "// heading\nclass C\n{\n void Other() {}\n int M(int x)\n {\n return x /* comment */ +\n 1;\n }\n}";
        var originalStart = original.IndexOf("x + 1", StringComparison.Ordinal);
        var movedStart = moved.IndexOf("x /*", StringComparison.Ordinal);
        var movedEnd = moved.IndexOf(';', movedStart);
        Assert.That(Anchor(moved, movedStart, movedEnd - movedStart),
            Is.EqualTo(Anchor(original, originalStart, "x + 1".Length)));
    }

    [Test]
    public void IdenticalExpressionsHaveDistinctAnchors()
    {
        const string source = "class C { int M(int x) { return (x + 1) * (x + 1); } }";
        var first = source.IndexOf("x + 1", StringComparison.Ordinal);
        var second = source.LastIndexOf("x + 1", StringComparison.Ordinal);
        Assert.That(Anchor(source, first, 5), Does.EndWith("#1"));
        Assert.That(Anchor(source, second, 5), Does.EndWith("#2"));
        Assert.That(Anchor(source, first, 5), Is.Not.EqualTo(Anchor(source, second, 5)));
    }

    [Test]
    public void WrongExpressionOrWidenedSpanChangesAnchor()
    {
        const string source = "class C { int M(int x) { return (x + 1) * (x + 2); } }";
        var start = source.IndexOf("x + 1", StringComparison.Ordinal);
        var other = source.IndexOf("x + 2", StringComparison.Ordinal);
        Assert.That(Anchor(source, start, 5), Is.Not.EqualTo(Anchor(source, other, 5)));
        Assert.That(Anchor(source, start, 5), Is.Not.EqualTo(Anchor(source, start - 1, 7)));
    }

    [Test]
    public void WhitespaceInsideLiteralRemainsSignificant()
    {
        const string source = "class C { string M() { return \"a b\" + \"ab\"; } }";
        var spaced = source.IndexOf("\"a b\"", StringComparison.Ordinal);
        var compact = source.IndexOf("\"ab\"", StringComparison.Ordinal);
        Assert.That(Anchor(source, spaced, 5), Is.Not.EqualTo(Anchor(source, compact, 4)));
    }

    [Test]
    public void OperationDescriptionOffsetsFollowTheirStartingToken()
    {
        const string source = "class C { void M() { int x = 1; } }";
        const string moved = "// heading\nclass C\n{\n void M() { int /* comment */ x = 1; }\n}";
        var original = new GoldenSourceAnchors(CSharpSyntaxTree.ParseText(source, path: "fixture.cs"));
        var relocated = new GoldenSourceAnchors(CSharpSyntaxTree.ParseText(moved, path: "fixture.cs"));
        Assert.That(relocated.FormatOperationDescription("SimpleAssignment@" + moved.IndexOf("x =", StringComparison.Ordinal)),
            Is.EqualTo(original.FormatOperationDescription("SimpleAssignment@" + source.IndexOf("x =", StringComparison.Ordinal))));
        Assert.That(original.FormatOperationDescription("candidate:cfg"), Is.EqualTo("candidate:cfg"));
        Assert.Throws<ArgumentException>((Action)(() => original.FormatOperationDescription("SimpleAssignment@1")));
        Assert.Throws<ArgumentException>((Action)(() => original.FormatOperationDescription("SimpleAssignment@999999999999")));
    }

    [Test]
    public void InvalidDocumentAndPartialTokenSpansAreRejected()
    {
        const string source = "class Subject {}";
        var anchors = new GoldenSourceAnchors(CSharpSyntaxTree.ParseText(source, path: "fixture.cs"));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("other.cs", 0, 5)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", 1, 4)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", 0, 4)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", 0, 0)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", -1, 5)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", 0, int.MaxValue)));
        Assert.Throws<ArgumentException>((Action)(() => anchors.Format("fixture.cs", 6, int.MaxValue)));
    }

    private static string Anchor(string source, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new GoldenSourceAnchors(CSharpSyntaxTree.ParseText(source, path: "fixture.cs"))
            .Format("fixture.cs", start, length);
    }
}
