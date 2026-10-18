using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275SourceSpanCoverageTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void ASourceSpanRequiresADocument(string? document)
    {
        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new IrSourceSpan(document!, 0, 1)));
        Assert.That(exception!.ParamName, Is.EqualTo("document"));
    }

    [TestCase(-1, 1)]
    [TestCase(0, -1)]
    [TestCase(int.MaxValue, 1)]
    public void InvalidOffsetsCannotDescribeASourceSpan(int start, int length)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrSourceSpan("Subject.cs", start, length)));
        Assert.That(exception!.ParamName, Is.EqualTo("start"));
    }

    [TestCase(-1, 1)]
    [TestCase(1, -1)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public void DisplayPositionsMustBeUnknownOrBothOneBased(int line, int column)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrSourceSpan("Subject.cs", 2, 3, line, column)));
        Assert.That(exception!.ParamName, Is.EqualTo("line"));
    }

    [TestCase(0, 0)]
    [TestCase(4, 8)]
    public void ValidCoordinatesArePreserved(int line, int column)
    {
        var span = new IrSourceSpan("Subject.cs", 2, 3, line, column);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.Document, Is.EqualTo("Subject.cs"));
            Assert.That(span.Start, Is.EqualTo(2));
            Assert.That(span.Length, Is.EqualTo(3));
            Assert.That(span.Line, Is.EqualTo(line));
            Assert.That(span.Column, Is.EqualTo(column));
        }
    }
}
