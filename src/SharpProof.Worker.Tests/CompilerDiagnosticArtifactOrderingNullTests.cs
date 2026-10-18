using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerDiagnosticArtifactOrderingNullTests
{
    [Test]
    public void CompareRejectsNullLeftDiagnostic()
    {
        var right = Diagnostic();
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerDiagnosticArtifactOrdering.Compare(null!, right)));
        Assert.That(exception!.ParamName, Is.EqualTo("left"));
    }

    [Test]
    public void CompareRejectsNullRightDiagnostic()
    {
        var left = Diagnostic();
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerDiagnosticArtifactOrdering.Compare(left, null!)));
        Assert.That(exception!.ParamName, Is.EqualTo("right"));
    }

    [Test]
    public void CompareRejectsLeftDiagnosticWithoutLocation()
    {
        var left = Diagnostic();
        left.Location = null!;
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerDiagnosticArtifactOrdering.Compare(left, Diagnostic())));
    }

    [Test]
    public void CompareRejectsRightDiagnosticWithoutLocation()
    {
        var right = Diagnostic();
        right.Location = null!;
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerDiagnosticArtifactOrdering.Compare(Diagnostic(), right)));
    }

    private static CompilerDiagnosticArtifact Diagnostic()
    {
        return new CompilerDiagnosticArtifact
        {
            Code = "SP0027",
            Message = "message",
            Location = new WorkerSourceLocation
            {
                Path = "mapped.cs",
                Start = 3,
                Length = 1,
                Line = 11,
                Column = 5
            }
        };
    }
}
