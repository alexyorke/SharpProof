using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReportingTests
{
    [Test]
    public void LineMapIdentityRequiresAnInitializedEntryArray()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerReportingIdentity.ComputeLineMapSha256(default)));
        Assert.That(error!.ParamName, Is.EqualTo("entries"));
    }

    [Test]
    public void ReportingMapLookupRejectsNullableContainingDefaultTrees()
    {
        ImmutableArray<CompilerSyntaxTreeSnapshot>? defaultTrees =
            default(ImmutableArray<CompilerSyntaxTreeSnapshot>);
        Assert.That(defaultTrees.HasValue, Is.True);
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            CompilerSourceCoordinates.FindUniqueTree(
                Location(), defaultTrees, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("syntaxTrees"));
    }

    [Test]
    public void DefaultAdditionalFilesCaptureAsAnEmptyCollection()
    {
        var compilation = TestCompilation.CreateExact("DefaultAdditionalFiles",
            [("Subject.cs", "public static class Subject { public static int Root() => 1; }")]);
        var captured = CompilerCompilationCapture.Capture(compilation,
            Directory.GetCurrentDirectory(), "net8.0", default,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(captured.AdditionalFiles, Is.Empty);
        Assert.That(captured.SyntaxTrees, Has.Length.EqualTo(1));
    }

    private static readonly ImmutableArray<ImmutableArray<string>> CapturedPathCases = [
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
        if (caseIndex < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(caseIndex)); }

        var paths = CapturedPathCases[caseIndex];
        var compilation = TestCompilation.CreateExact("DocumentIdentityTests", Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, paths.Select((path, index) =>
            path is null ? throw new System.ArgumentNullException(nameof(path)) : ((path, "public static class Subject" + index + " { public static int Root() => " + index + "; }"))).ToImmutableArray());
        var snapshots = CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(snapshots.Select(static snapshot => snapshot is null ? throw new System.ArgumentNullException(nameof(snapshot)) : (snapshot.Path)).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(paths.Length));
        var trees = compilation.SyntaxTrees;
        for (var ordinal = 0; ordinal < trees.Length; ordinal++)
        {
            var independentlyCaptured = CompilerCompilationCapture.CaptureTree(trees[ordinal], TestContext.CurrentContext.CancellationToken);
            Assert.That(snapshots[ordinal].Sha256, Is.EqualTo(independentlyCaptured.Sha256));
            Assert.That(snapshots[ordinal].TextLength, Is.EqualTo(independentlyCaptured.TextLength));
            var originalPath = string.IsNullOrEmpty(paths[ordinal])
                ? $"<compiler-generated:{ordinal}>" : CompilerCaptureIdentity.NormalizePath(paths[ordinal]);
            if (!snapshots.Take(ordinal).Any(snapshot => snapshot is null ? throw new System.ArgumentNullException(nameof(snapshot)) : (snapshot.Path == originalPath)))
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
        System.ArgumentNullException.ThrowIfNull(coordinate);
        var first = new CompilerDiagnosticArtifact { Location = Location(), Code = "SP0027", Message = "same" };
        var second = new CompilerDiagnosticArtifact { Location = Location(), Code = "SP0027", Message = "same" };
        switch (coordinate)
        {
            case "start":
                second = second with { Location = second.Location with { Start = second.Location.Start + 1 } };
                break;
            case "line":
                { second = second with { Location = second.Location with { Line = second.Location.Line + 1 } }; }
                break;
            case "column":
                second = second with { Location = second.Location with { Column = second.Location.Column + 1 } };
                break;
            case "path":
                { second = second with { Location = second.Location with { Path = second.Location.Path + (".other") } }; }
                break;
            case "message":
                { second = second with { Message = second.Message + " other" }; }
                break;
        }
        Assert.That(CompilerDiagnosticArtifactOrdering.Canonicalize([second, first]), Is.EqualTo(ImmutableArray.Create(first, second)));
    }

    [Test]
    public void SharedMappedLocationsRetainTheirPhysicalTreeIdentity()
    {
        var first = Tree("first.cs");
        var second = Tree("second.cs");
        var compilation = new CompilerCompilationSnapshot { SyntaxTrees = [first, second] };
        var location = Location();

        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(-1));
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
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.Zero);
        location = location with { Column = location.Column + 1 };
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, compilation, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(-1));
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerSourceCoordinates.Bind(location, compilation, out _, out _, out _, out _)));
    }

    [Test]
    public void MappedColumnsAccountForLineDirectiveCharacterOffsets()
    {
        var tree = Tree("physical.cs");
        tree = tree with { LineMap = tree.LineMap.SetItem(0, tree.LineMap[0] with { CharacterOffset = 2 }) };
        var location = Location();
        { location = location with { Column = 3 }; }
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, [tree], cancellationToken: TestContext.CurrentContext.CancellationToken), Is.Zero);
        using (Assert.EnterMultipleScope())
        {
            { location = location with { Start = 1 }; }
            { location = location with { Column = 2 }; }
            Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
            Assert.That(CompilerSourceCoordinates.FindUniqueTree(null, [tree], cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(-1));
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
        System.ArgumentNullException.ThrowIfNull(malformed);
        var tree = Tree("physical.cs");
        var entry = tree.LineMap[0];
        tree = malformed switch
        {
            "start" => tree with { LineMap = [entry with { SourceStart = -1 }] },
            "length" => tree with { LineMap = [entry with { SourceLength = tree.TextLength + 1 }] },
            "path" => tree with { LineMap = [entry with { MappedPath = " " }] },
            "line" => tree with { LineMap = [entry with { MappedLine = -1 }] },
            "column" => tree with { LineMap = [entry with { MappedColumn = int.MaxValue }] },
            "offset" => tree with { LineMap = [entry with { CharacterOffset = entry.SourceLength + 1 }] },
            "gap" => tree with { LineMap = [entry, new CompilerSourceLineMapEntry { SourceStart = 2 }] },
            "missing" => tree with { LineMap = [] },
            "first-line" => tree with { LineMap = [entry with { SourceStart = 1, SourceLength = tree.TextLength - 1 }] },
            _ => tree
        };
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(Location(), tree, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
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
        { location = location with { Start = tree.TextLength + 1 }; }
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, tree, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
        tree = tree with { LineMap = tree.LineMap.SetItem(0, tree.LineMap[0] with { MappedColumn = int.MaxValue }) };
        Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(Location(), tree, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
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
            Assert.That(canonical, Is.EqualTo(ImmutableArray.Create(first, second)));
            Assert.That(CompilerDiagnosticArtifactOrdering.IsCanonical(canonical), Is.True);
            Assert.That(CompilerDiagnosticArtifactOrdering.IsCanonical(ImmutableArray<CompilerDiagnosticArtifact>.Empty), Is.True);
            Action invalidOrdering = () => CompilerDiagnosticArtifactOrdering.IsCanonical(default);
            Assert.That(Assert.Throws<ArgumentException>(invalidOrdering)!.ParamName, Is.EqualTo("diagnostics"));
            Assert.That(CompilerDiagnosticArtifactOrdering.IsCanonical([second, first]), Is.False);
            Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, first), Is.Zero);
        }
        second = second with { SourceTreeOrdinal = 0 };
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
        second = second with { SourceTreePath = first.SourceTreePath };
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
        second = second with { SourceTreeSha256 = first.SourceTreeSha256 };
        Assert.That(CompilerDiagnosticArtifactOrdering.Compare(first, second), Is.Negative);
    }

    private static CompilerSyntaxTreeSnapshot Tree(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
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
