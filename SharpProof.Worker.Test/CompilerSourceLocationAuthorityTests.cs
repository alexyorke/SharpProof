using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using System.Text;
using System.Text.Json;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSourceLocationAuthorityTests
{
    [Test]
    public void ProducerBindsCompilerDiagnosticsToPhysicalTreeAndLineMap()
    {
        var artifact = CreateArtifact(
            "#line 17 \"mapped.cs\"\n" +
            "internal static class Subject { static int M() { return ; } }\n");
        var diagnostic = artifact.CompilerDiagnostics.Single();
        var tree = artifact.Compilation.SyntaxTrees.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostic.SourceTreeOrdinal, Is.EqualTo(0));
            Assert.That(diagnostic.SourceTreePath, Is.EqualTo(tree.Path));
            Assert.That(diagnostic.SourceTreeSha256, Is.EqualTo(tree.Sha256));
            Assert.That(
                diagnostic.SourceLineMapSha256,
                Is.EqualTo(tree.LineMapSha256));
            Assert.That(
                CompilerSourceLocationAuthority.IsBound(
                    diagnostic.Location,
                    diagnostic.SourceTreeOrdinal,
                    diagnostic.SourceTreePath,
                    diagnostic.SourceTreeSha256,
                    diagnostic.SourceLineMapSha256,
                    artifact.Compilation),
                Is.True);
        }
    }

    [Test]
    public void HydrationRejectsDiagnosticTreeHashTampering()
    {
        var artifact = CreateArtifact(
            "internal static class Subject { static int M() { return ; } }\n");
        artifact.CompilerDiagnostics[0].SourceTreeSha256 = new string('0', 64);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void ProducerBindsEveryGenericManifestLocationAuthority()
    {
        var artifact = CreateContractArtifact();
        var expected = artifact.Manifest.Callables.Length +
            artifact.Manifest.Claims.Length;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.LocationAuthorities, Has.Length.EqualTo(expected));
            Assert.That(
                artifact.LocationAuthorities,
                Is.Ordered.By(nameof(CompilerLocationAuthorityArtifact.OwnerId)));
            Assert.That(
                artifact.LocationAuthorities.All(authority =>
                    CompilerSourceLocationAuthority.IsBound(
                        authority.Location,
                        authority.SourceTreeOrdinal,
                        authority.SourceTreePath,
                        authority.SourceTreeSha256,
                        authority.SourceLineMapSha256,
                        artifact.Compilation)),
                Is.True);
        }
    }

    [Test]
    public void HydrationRejectsGenericLocationAuthorityGeometryTampering()
    {
        var artifact = CreateContractArtifact();
        var authority = artifact.LocationAuthorities[0];
        authority.Location.Line++;

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void LineMapBindsLineDirectiveAndExactEndCoordinates()
    {
        var artifact = CreateArtifact(
            "#line 42 \"mapped.cs\"\n" +
            "internal sealed class Subject {}\n");
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var mappedLine = tree.LineMap.First(entry => entry.SourceStart > 0);

        var lineStart = new WorkerSourceLocation
        {
            Path = mappedLine.MappedPath,
            Start = mappedLine.SourceStart,
            Length = 0,
            Line = mappedLine.MappedLine + 1,
            Column = mappedLine.MappedColumn + 1
        };
        Assert.That(
            CompilerSourceLocationAuthority.HasValidLocationGeometry(
                lineStart,
                tree),
            Is.True);

        Assert.That(
            CompilerSourceLocationAuthority.TryMap(
                tree.LineMap,
                tree.TextLength,
                out var mappedPath,
                out var mappedLineNumber,
                out var mappedColumn),
            Is.True);
        var exactEnd = new WorkerSourceLocation
        {
            Path = mappedPath,
            Start = tree.TextLength,
            Length = 0,
            Line = mappedLineNumber + 1,
            Column = mappedColumn + 1
        };
        Assert.That(
            CompilerSourceLocationAuthority.HasValidLocationGeometry(
                exactEnd,
                tree),
            Is.True);
    }

    [Test]
    public void OnlyAllZeroLocationIsTheNonSourceSentinel()
    {
        Assert.That(
            CompilerSourceLocationAuthority.IsNone(
                new WorkerSourceLocation()),
            Is.True);
        Assert.That(
            CompilerSourceLocationAuthority.IsNone(
                new WorkerSourceLocation { Line = 1 }),
            Is.False);
    }

    [Test]
    public void GenuineNonSourceOwnerUsesCanonicalSentinelAuthority()
    {
        var artifact = CreateArtifact(
            "internal sealed class Subject {}\n");
        var authority = CompilerSourceLocationAuthority.CreateAuthority(
            CompilerSourceLocationOwnerKind.Callable,
            "compiler-synthesized",
            new WorkerSourceLocation(),
            artifact.Compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(authority.SourceTreeOrdinal, Is.EqualTo(-1));
            Assert.That(authority.SourceTreePath, Is.Empty);
            Assert.That(authority.SourceTreeSha256, Is.Empty);
            Assert.That(authority.SourceLineMapSha256, Is.Empty);
            Assert.That(
                CompilerSourceLocationAuthority.IsBound(
                    authority.Location,
                    authority.SourceTreeOrdinal,
                    authority.SourceTreePath,
                    authority.SourceTreeSha256,
                    authority.SourceLineMapSha256,
                    artifact.Compilation,
                    allowNone: true),
                Is.True);
        }
    }

    [Test]
    public void ValidationContextCachesLineMapValidationPerTree()
    {
        var artifact = CreateArtifact(
            "#line 42 \"mapped.cs\"\n" +
            "internal sealed class Subject {}\n");
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var mappedLine = tree.LineMap.First(entry => entry.SourceStart > 0);
        var location = new WorkerSourceLocation
        {
            Path = mappedLine.MappedPath,
            Start = mappedLine.SourceStart,
            Length = 0,
            Line = mappedLine.MappedLine + 1,
            Column = mappedLine.MappedColumn + 1
        };
        var context = new CompilerSourceLocationAuthority.ValidationContext();

        CompilationFingerprint.ValidateShape(artifact.Compilation, context);

        for (var index = 0; index < 8; index++)
        {
            Assert.That(
                CompilerSourceLocationAuthority.HasValidLocationGeometry(
                    location,
                    tree,
                    context),
                Is.True);
        }

        Assert.That(context.LineMapValidationCount, Is.EqualTo(1));
    }

    [Test]
    public void ValidationContextCachesSnapshotHashPerTree()
    {
        var first = CreateArtifact(
            "internal sealed class First {}\n");
        var second = CreateArtifact(
            "internal sealed class Second {}\n");
        var firstTree = first.Compilation.SyntaxTrees.Single();
        var secondTree = second.Compilation.SyntaxTrees.Single();
        var context = new CompilerSourceLocationAuthority.ValidationContext();

        string? firstHash = null;
        for (var index = 0; index < 256; index++)
        {
            var hash = context.GetSyntaxTreeSnapshotSha256(firstTree);
            firstHash ??= hash;
            Assert.That(hash, Is.EqualTo(firstHash));
        }

        var secondHash = context.GetSyntaxTreeSnapshotSha256(secondTree);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstHash, Is.EqualTo(
                CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256(
                    firstTree)));
            Assert.That(secondHash, Is.EqualTo(
                CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256(
                    secondTree)));
            Assert.That(
                context.SnapshotHashComputeCount,
                Is.EqualTo(2));
        }
    }

    [Test]
    public void LargeLineMapsInternRepeatedMappedPathsOnTheWire()
    {
        var artifact = CreateArtifact(
            "internal sealed class Subject {}\n");
        var tree = artifact.Compilation.SyntaxTrees.Single();
        const int lineCount = 120_000;
        tree.TextLength = lineCount;
        tree.LineMap = Enumerable.Range(0, lineCount)
            .Select(index => new CompilerSourceLineMapEntry
            {
                SourceStart = index,
                SourceLength = 0,
                MappedPath = tree.Path,
                MappedLine = 0,
                MappedColumn = 0
            })
            .ToArray();
        tree.LineMapSha256 = CompilationFingerprint.ComputeLineMapSha256(
            tree.LineMap);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);

        var json = CompilerManifestArtifactJson.Serialize(artifact);
        var roundTrip = CompilerManifestArtifactJson.Deserialize(json);
        var roundTripTree = roundTrip.Compilation.SyntaxTrees.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"mappedPathIndex\""));
            Assert.That(
                Encoding.UTF8.GetByteCount(json),
                Is.LessThan(WorkerProtocolJson.MaximumJsonBytes));
            Assert.That(roundTripTree.LineMap, Has.Length.EqualTo(lineCount));
            Assert.That(
                roundTripTree.LineMap.Select(static entry => entry.MappedPath),
                Is.All.EqualTo(tree.Path));
            Assert.That(
                CompilerManifestArtifactJson.Serialize(roundTrip),
                Is.EqualTo(json));
        }
    }

    [Test]
    public void GenuineNonSourceCompilerDiagnosticUsesExplicitSentinelClassification()
    {
        var artifact = CreateNonSourceDiagnosticArtifact();
        var diagnostic = artifact.CompilerDiagnostics.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostic.IsSource, Is.False);
            Assert.That(
                CompilerSourceLocationAuthority.IsNone(diagnostic.Location),
                Is.True);
            Assert.That(
                CompilerManifestArtifactJson.Deserialize(
                    CompilerManifestArtifactJson.Serialize(artifact))
                    .CompilerDiagnostics.Single().IsSource,
                Is.False);
        }
    }

    [Test]
    public void SourceCompilerDiagnosticRejectsOmittedBindingAndSentinelConversion()
    {
        var omittedBinding = CreateArtifact(
            "internal static class Subject { static int M() { return ; } }\n");
        var omitted = omittedBinding.CompilerDiagnostics.Single();
        omitted.SourceTreeOrdinal = -1;
        omitted.SourceTreePath = string.Empty;
        omitted.SourceTreeSha256 = string.Empty;
        omitted.SourceLineMapSha256 = string.Empty;
        omittedBinding.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            omittedBinding.Compilation,
            omittedBinding.CompilerDiagnostics);

        var sourceToSentinel = CreateArtifact(
            "internal static class Subject { static int M() { return ; } }\n");
        var converted = sourceToSentinel.CompilerDiagnostics.Single();
        converted.Location = new WorkerSourceLocation();
        sourceToSentinel.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            sourceToSentinel.Compilation,
            sourceToSentinel.CompilerDiagnostics);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(omittedBinding)));
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(sourceToSentinel)));
        }
    }

    [Test]
    public void DiagnosticClassificationIsRequiredOnTheWire()
    {
        var json = CompilerManifestArtifactJson.Serialize(
            CreateNonSourceDiagnosticArtifact());
        var omitted = json.Replace(
            "\"isSource\":false,",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(omitted)));
    }

    private static CompilerManifestArtifact CreateArtifact(string source)
    {
        var compilation = CreateCompilation(source, includeContractReference: false);
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static CompilerManifestArtifact CreateNonSourceDiagnosticArtifact()
    {
        var compilation = CreateCompilation(
            "internal sealed class Subject {}\n",
            includeContractReference: false).WithOptions(
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static CompilerManifestArtifact CreateContractArtifact()
    {
        var source = "using SharpProof.Attributes;\n" +
            "internal static class Subject {\n" +
            "  internal static int Identity(int value) {\n" +
            "    Contract.Ensures(Contract.Result<int>() == value);\n" +
            "    return value;\n" +
            "  }\n" +
            "}\n";
        var compilation = CreateCompilation(source, includeContractReference: true);
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        bool includeContractReference)
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => includeContractReference ||
                string.Equals(
                    path,
                    typeof(object).Assembly.Location,
                    StringComparison.OrdinalIgnoreCase))
            .Append(includeContractReference
                ? typeof(Contract).Assembly.Location
                : typeof(object).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return CSharpCompilation.Create(
            "CompilerSourceLocationAuthorityTest",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "SourceAuthoritySubject.cs"))],
            paths.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
