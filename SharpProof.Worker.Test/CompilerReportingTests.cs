using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReportingTests
{
    private static readonly string[][] CapturedPathCases = [
        ["Subject.cs", "./Subject.cs"],
        ["Subject.cs", "Subject.cs", "Subject.cs#1"],
        ["Subject.cs#2", "Subject.cs", "Subject.cs"],
        ["Subject.cs#3", "Subject.cs#3#3", "Subject.cs", "Subject.cs"],
        ["", "", "<compiler-generated:0>"]
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void CapturedDocumentsHaveUniqueFinalIdentities(int caseIndex)
    {
        var paths = CapturedPathCases[caseIndex];
        var compilation = TestCompilation.Create("DocumentIdentityTests", paths.Select((path, index) =>
            (path, "public static class Subject" + index + " { public static int Root() => " + index + "; }")).ToArray());
        var snapshots = CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None);
        Assert.That(snapshots.Select(static snapshot => snapshot.Path).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(paths.Length));
        var trees = compilation.SyntaxTrees.ToArray();
        for (var ordinal = 0; ordinal < trees.Length; ordinal++)
        {
            var independentlyCaptured = CompilerCompilationCapture.CaptureTree(trees[ordinal], CancellationToken.None);
            Assert.That(snapshots[ordinal].Sha256, Is.EqualTo(independentlyCaptured.Sha256));
            Assert.That(snapshots[ordinal].TextLength, Is.EqualTo(independentlyCaptured.TextLength));
            var originalPath = string.IsNullOrEmpty(paths[ordinal])
                ? $"<compiler-generated:{ordinal}>" : CompilerCaptureIdentity.NormalizePath(paths[ordinal]);
            if (!snapshots.Take(ordinal).Any(snapshot => snapshot.Path == originalPath))
            { Assert.That(snapshots[ordinal].Path, Is.EqualTo(originalPath)); }
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CaptureReportingRejectsMissingPaths(string? path)
    {
        Assert.That(Assert.Throws<ArgumentException>((Action)(() =>
            CompilerCaptureIdentity.NormalizePath(path!)))!.ParamName, Is.EqualTo("path"));
    }

    [TestCase("start")]
    [TestCase("line")]
    [TestCase("column")]
    [TestCase("path")]
    [TestCase("message")]
    public void DiagnosticOrderingPreservesDistinctSourceCoordinates(string coordinate)
    {
        var first = new CompilerDiagnosticArtifact { Location = Location(), Code = "SP0027", Message = "same" };
        var second = new CompilerDiagnosticArtifact { Location = Location(), Code = "SP0027", Message = "same" };
        switch (coordinate)
        {
            case "start":
                second.Location.Start++;
                break;
            case "line":
                second.Location.Line++;
                break;
            case "column":
                second.Location.Column++;
                break;
            case "path":
                second.Location.Path += ".other";
                break;
            case "message":
                second.Message += " other";
                break;
        }
        Assert.That(CompilerDiagnosticArtifactOrdering.Canonicalize([second, first]), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void SharedMappedLocationsRetainTheirPhysicalTreeIdentity()
    {
        var first = Tree("first.cs");
        var second = Tree("second.cs");
        var compilation = new CompilerCompilationSnapshot { SyntaxTrees = [first, second] };
        var location = Location();

        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation), Is.EqualTo(-1));
        CompilerSourceCoordinates.RememberTree(location, 1);
        CompilerSourceCoordinates.Bind(location, compilation, out var ordinal,
            out var path, out var treeHash, out var mapHash);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ordinal, Is.EqualTo(1));
            Assert.That(path, Is.EqualTo("second.cs"));
            Assert.That(treeHash, Is.EqualTo(second.Sha256));
            Assert.That(mapHash, Is.EqualTo(second.LineMapSha256));
        }

        // Rebinding replaces the producer's previous association.
        CompilerSourceCoordinates.RememberTree(location, 0);
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation), Is.Zero);
        location.Column++;
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation), Is.EqualTo(-1));
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerSourceCoordinates.Bind(location, compilation, out _, out _, out _, out _)));
    }

    [Test]
    public void MappedColumnsAccountForLineDirectiveCharacterOffsets()
    {
        var tree = Tree("physical.cs");
        tree.LineMap[0].CharacterOffset = 2;
        var location = Location();
        location.Column = 3;
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree), Is.True);
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, [tree]), Is.Zero);
        using (Assert.EnterMultipleScope())
        {
            location.Start = 1;
            location.Column = 2;
            Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree), Is.True);
            Assert.That(CompilerSourceCoordinates.FindUniqueTree(null, [tree]), Is.EqualTo(-1));
        }
    }

    [TestCase("start")]
    [TestCase("length")]
    [TestCase("path")]
    [TestCase("line")]
    [TestCase("column")]
    [TestCase("offset")]
    [TestCase("gap")]
    [TestCase("missing")]
    [TestCase("first-line")]
    public void MalformedReportingMapsDoNotInventCoordinates(string malformed)
    {
        var tree = Tree("physical.cs");
        var entry = tree.LineMap[0];
        switch (malformed)
        {
            case "start":
                entry.SourceStart = -1;
                break;
            case "length":
                entry.SourceLength = tree.TextLength + 1;
                break;
            case "path":
                entry.MappedPath = " ";
                break;
            case "line":
                entry.MappedLine = -1;
                break;
            case "column":
                entry.MappedColumn = int.MaxValue;
                break;
            case "offset":
                entry.CharacterOffset = entry.SourceLength + 1;
                break;
            case "gap":
                tree.LineMap = [entry, new CompilerSourceLineMapEntry { SourceStart = 2 }];
                break;
            case "missing":
                tree.LineMap = [];
                break;
            case "first-line":
                entry.SourceStart = 1;
                entry.SourceLength = tree.TextLength - 1;
                break;
        }
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(Location(), tree), Is.False);
    }

    [Test]
    public void ReportingMapLookupHonorsCancellationAndSourceBounds()
    {
        var tree = Tree("physical.cs");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() =>
            CompilerSourceCoordinates.FindUniqueTree(Location(), [tree], canceled.Token)));
        var location = Location();
        location.Start = tree.TextLength + 1;
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree), Is.False);
        tree.LineMap[0].MappedColumn = int.MaxValue;
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(Location(), tree), Is.False);
    }

    [Test]
    public void DiagnosticsWithSharedMappedLocationsHaveStableCanonicalOrdering()
    {
        var first = new CompilerDiagnosticArtifact
        {
            Code = "SP0027",
            Message = "first",
            Location = Location(),
            SourceTreeOrdinal = 0,
            SourceTreePath = "first.cs",
            SourceTreeSha256 = new string('a', 64),
            SourceLineMapSha256 = new string('c', 64)
        };
        var second = new CompilerDiagnosticArtifact
        {
            Code = "SP0027",
            Message = "first",
            Location = Location(),
            SourceTreeOrdinal = 1,
            SourceTreePath = "second.cs",
            SourceTreeSha256 = new string('b', 64),
            SourceLineMapSha256 = new string('d', 64)
        };
        var canonical = CompilerDiagnosticArtifactOrdering.Canonicalize([second, first]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(canonical, Is.EqualTo(new[] { first, second }));
            Assert.That(CompilerDiagnosticArtifactOrdering.IsCanonical(canonical), Is.True);
            Assert.That(CompilerDiagnosticArtifactOrdering.IsCanonical([second, first]), Is.False);
            Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, first), Is.Zero);
        }
        second.SourceTreeOrdinal = 0;
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
        second.SourceTreePath = first.SourceTreePath;
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
        second.SourceTreeSha256 = first.SourceTreeSha256;
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
    }

    private static CompilerSyntaxTreeSnapshot Tree(string path)
    {
        return new CompilerSyntaxTreeSnapshot
        {
            Path = path,
            TextLength = 8,
            Sha256 = new string('a', 64),
            LineMapSha256 = new string('b', 64),
            LineMap = [new CompilerSourceLineMapEntry
            {
                SourceStart = 0,
                SourceLength = 8,
                MappedPath = "mapped.cs",
                MappedLine = 10,
                MappedColumn = 1
            }]
        };
    }

    private static WorkerSourceLocation Location()
    {
        return new WorkerSourceLocation
        {
            Path = "mapped.cs",
            Start = 3,
            Length = 1,
            Line = 11,
            Column = 5
        };
    }
}
