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
    public void LineMapValidationHonorsCancellationBeforeScanningEveryEntry()
    {
        var artifact = CreateArtifact(
            "#line 17 \"mapped.cs\"\n" +
            "internal static class Subject { static int M() { return ; } }\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            (Action)(() => CompilerSourceLocationAuthority.HasValidLineMap(
                artifact.Compilation.SyntaxTrees.Single(),
                cancellation.Token)));
    }

    [Test]
    public void LineMapValidationRejectsNoncanonicalSourceLength()
    {
        var artifact = CreateArtifact("class Subject {}\nclass Other {}\n");
        var tree = artifact.Compilation.SyntaxTrees.Single();
        tree.LineMap[0].SourceLength++;
        tree.LineMapSha256 = CompilationFingerprint.ComputeLineMapSha256(tree.LineMap);

        Assert.That(
            CompilerSourceLocationAuthority.HasValidLineMap(tree),
            Is.False);
    }

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
            AssertBound(diagnostic, artifact.Compilation);
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
                    IsBound(authority, artifact.Compilation)),
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
    public void SourceRebindingAcceptsCapturedSource()
    {
        var artifact = CreateOnDiskContractArtifact(out _);

        Assert.DoesNotThrow((Action)(() =>
            CompilerSourceRebinding.Validate(artifact)));
    }

    [TestCase("utf-8")]
    [TestCase("utf-16")]
    [TestCase("utf-16BE")]
    [TestCase("utf-32")]
    [TestCase("utf-32BE")]
    public void SourceRebindingAcceptsCompilerByteOrderMarks(string encodingName)
    {
        var artifact = CreateOnDiskContractArtifact(out var source);
        var path = artifact.Compilation.SyntaxTrees.Single().Path;
        File.WriteAllText(path, source, Encoding.GetEncoding(encodingName));
        Assert.DoesNotThrow((Action)(() => CompilerSourceRebinding.Validate(artifact)));
        File.AppendAllText(path, "// changed", Encoding.GetEncoding(encodingName));
        Assert.Throws<InvalidDataException>((Action)(() => CompilerSourceRebinding.Validate(artifact)));
    }

    [TestCase("Ensures /* comment ( */ ")]
    [TestCase("Ensures // comment (\n")]
    [TestCase("Ensur\\u0065s")]
    [TestCase("@Ensures")]
    [TestCase("Ensu\\u200Cres")]
    public void SourceRebindingAcceptsEnsuresLexicalSpellings(string method)
    {
        var artifact = CreateOnDiskContractArtifact(out _, method);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.DoesNotThrow((Action)(() => CompilerSourceRebinding.Validate(artifact)));
    }

    [Test]
    public void SourceRebindingAcceptsUnicodeContractAlias()
    {
        var artifact = CreateOnDiskContractArtifact(out _, qualifier: "C\u0301");
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.DoesNotThrow((Action)(() => CompilerSourceRebinding.Validate(artifact)));
    }

    [Test]
    public void SourceRebindingRejectsResealedRelocationWithinCallable()
    {
        var artifact = CreateOnDiskContractArtifact(out var source);
        var claim = artifact.Manifest.Claims.Single();
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var start = source.IndexOf("return value", StringComparison.Ordinal);
        Assert.That(
            CompilerSourceLocationAuthority.TryMap(
                tree.LineMap,
                start,
                out var mappedPath,
                out var mappedLine,
                out var mappedColumn),
            Is.True);
        claim.Location = new WorkerSourceLocation
        {
            Path = mappedPath,
            Start = start,
            Length = "return value".Length,
            Line = mappedLine + 1,
            Column = mappedColumn + 1
        };
        artifact.LocationAuthorities.Single(authority =>
                authority.OwnerKind == CompilerSourceLocationOwnerKind.Claim)
            .Location = CompilerSourceLocationAuthority.CopyLocation(
                claim.Location);
        artifact.Manifest.Hash =
            WorkerProtocolJson.ComputeManifestHash(artifact.Manifest);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        using (Assert.EnterMultipleScope())
        {
            // The relocation stays inside the callable, so hydration alone
            // cannot tell it apart from the collector's own span.
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerSourceRebinding.Validate(artifact)));
        }
    }

    [Test]
    public void SourceRebindingSkipsTreesWithoutAFile()
    {
        var artifact = CreateOnDiskContractArtifact(out _);
        File.Delete(artifact.Compilation.SyntaxTrees.Single().Path);

        Assert.DoesNotThrow((Action)(() =>
            CompilerSourceRebinding.Validate(artifact)));
    }

    [Test]
    public void SourceRebindingRejectsChangedSource()
    {
        var artifact = CreateOnDiskContractArtifact(out _);
        var path = artifact.Compilation.SyntaxTrees.Single().Path;
        File.AppendAllText(path, "// changed\n");

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerSourceRebinding.Validate(artifact)));
    }

    [TestCase("callable-declaration")]
    [TestCase("other-callable")]
    public void HydrationRejectsResealedDirectClauseRelocation(string target)
    {
        var artifact = CreateContractArtifact(
            "using SharpProof.Attributes;\n" +
            "internal static class Subject {\n" +
            "  internal static int Identity(int value) {\n" +
            "    Contract.Ensures(Contract.Result<int>() == value);\n" +
            "    return value;\n" +
            "  }\n" +
            "  internal static int Same(int value) {\n" +
            "    Contract.Ensures(Contract.Result<int>() >= value);\n" +
            "    return value;\n" +
            "  }\n" +
            "}\n");
        var claims = artifact.Manifest.Claims;
        Assert.That(claims, Has.Length.EqualTo(2));
        var moved = claims[0];
        var location = target == "callable-declaration"
            ? artifact.Manifest.Callables.Single(callable =>
                callable.CallableId == moved.CallableId).Location
            : claims[1].Location;
        moved.Location = CompilerSourceLocationAuthority.CopyLocation(location);
        if (target == "other-callable")
        {
            // Keep geometry unique so only ownership distinguishes the rows.
            moved.Location.Length--;
        }

        artifact.LocationAuthorities.Single(authority =>
                authority.OwnerKind == CompilerSourceLocationOwnerKind.Claim &&
                authority.OwnerId == moved.ClaimId).Location =
            CompilerSourceLocationAuthority.CopyLocation(moved.Location);
        artifact.Manifest.Hash =
            WorkerProtocolJson.ComputeManifestHash(artifact.Manifest);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HydrationRejectsResealedDirectClauseLocationSwap(bool swap)
    {
        var artifact = CreateContractArtifact(
            "using SharpProof.Attributes;\n" +
            "internal static class Subject {\n" +
            "  internal static int Identity(int value) {\n" +
            "    Contract.Ensures(Contract.Result<int>() == value);\n" +
            "    Contract.Ensures(Contract.Result<int>() >= value);\n" +
            "    return value;\n" +
            "  }\n" +
            "}\n");
        var claims = artifact.Manifest.Claims
            .OrderBy(static claim => claim.Ordinal)
            .ToArray();
        Assert.That(claims, Has.Length.EqualTo(2));
        if (swap)
        {
            var first = claims[0].Location;
            claims[0].Location = claims[1].Location;
            claims[1].Location = first;
            foreach (var claim in claims)
            {
                artifact.LocationAuthorities.Single(authority =>
                        authority.OwnerKind ==
                            CompilerSourceLocationOwnerKind.Claim &&
                        authority.OwnerId == claim.ClaimId).Location =
                    CompilerSourceLocationAuthority.CopyLocation(claim.Location);
            }
        }

        artifact.Manifest.Hash =
            WorkerProtocolJson.ComputeManifestHash(artifact.Manifest);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        if (swap)
        {
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
        }
        else
        {
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
        }
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

        lineStart.Line++;
        Assert.That(
            CompilerSourceLocationAuthority.HasValidLocationGeometry(
                lineStart,
                tree),
            Is.False);

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
    public void LineMapPreservesEnhancedDirectiveCharacterOffsets()
    {
        const string source =
            "internal static class Subject { static void M() {\n" +
            "#line (5,3)-(5,17) 11 \"template.dsl\"\n" +
            "output.Add(Greet(\"Hello\"));\n" +
            "#line default\n" +
            "} }\n";
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "EnhancedLineDirective.g.cs"));
        var token = tree.GetRoot().DescendantTokens()
            .Single(static candidate => candidate.ValueText == "Greet");
        var expected = tree.GetMappedLineSpan(token.Span);
        var expectedPath = CompilerSourceLocationProjection.MappedPath(
            tree,
            expected);
        var snapshot = CompilerCompilationCapture.CaptureTree(
            tree,
            CancellationToken.None);

        Assert.That(
            CompilerSourceLocationAuthority.TryMap(
                snapshot.LineMap,
                token.SpanStart,
                out var mappedPath,
                out var mappedLine,
                out var mappedColumn),
            Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mappedPath, Is.EqualTo(expectedPath));
            Assert.That(mappedLine, Is.EqualTo(expected.StartLinePosition.Line));
            Assert.That(mappedColumn, Is.EqualTo(expected.StartLinePosition.Character));
        }

        var artifact = CreateArtifact(source);
        var mappedDiagnostics = artifact.CompilerDiagnostics
            .Where(diagnostic => diagnostic.Location.Path == expectedPath)
            .ToArray();
        Assert.That(mappedDiagnostics, Is.Not.Empty);
        Assert.That(
            mappedDiagnostics.All(diagnostic =>
                IsBound(diagnostic, artifact.Compilation)),
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

    private static void AssertBound(
        CompilerDiagnosticArtifact diagnostic,
        CompilerCompilationSnapshot compilation)
    {
        Assert.That(IsBound(diagnostic, compilation), Is.True);
    }

    private static bool IsBound(
        CompilerDiagnosticArtifact diagnostic,
        CompilerCompilationSnapshot compilation)
    {
        return CompilerSourceLocationAuthority.IsBound(
            diagnostic.Location,
            diagnostic.SourceTreeOrdinal,
            diagnostic.SourceTreePath,
            diagnostic.SourceTreeSha256,
            diagnostic.SourceLineMapSha256,
            compilation);
    }

    private static bool IsBound(
        CompilerLocationAuthorityArtifact authority,
        CompilerCompilationSnapshot compilation)
    {
        return CompilerSourceLocationAuthority.IsBound(
            authority.Location,
            authority.SourceTreeOrdinal,
            authority.SourceTreePath,
            authority.SourceTreeSha256,
            authority.SourceLineMapSha256,
            compilation);
    }

    private static CompilerManifestArtifact CreateArtifact(string source)
    {
        return CreateArtifact(
            CreateCompilation(source, includeContractReference: false));
    }

    private static CompilerManifestArtifact CreateArtifact(
        CSharpCompilation compilation)
    {
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
                TestCompilation.CreateOptions(OutputKind.ConsoleApplication));
        return CreateArtifact(compilation);
    }

    private static CompilerManifestArtifact CreateContractArtifact(
        string source =
            "using SharpProof.Attributes;\n" +
            "internal static class Subject {\n" +
            "  internal static int Identity(int value) {\n" +
            "    Contract.Ensures(Contract.Result<int>() == value);\n" +
            "    return value;\n" +
            "  }\n" +
            "}\n")
    {
        var compilation = CreateCompilation(source, includeContractReference: true);
        return CreateArtifact(compilation);
    }

    private static CompilerManifestArtifact CreateOnDiskContractArtifact(
        out string source, string ensures = "Ensures", string qualifier = "Contract")
    {
        source = "using SharpProof.Attributes;\n" +
            (qualifier == "Contract" ? "" : "using " + qualifier + " = SharpProof.Attributes.Contract;\n") +
            "internal static class Subject {\n" +
            "  internal static int Identity(int value) {\n" +
            "    " + qualifier + "." + ensures + "(Contract.Result<int>() == value);\n" +
            "    return value;\n" +
            "  }\n" +
            "}\n";
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SourceRebindingSubject-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var compilation = CSharpCompilation.Create(
            "CompilerSourceRebindingTest",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                path)],
            TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        return CreateArtifact(compilation);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        bool includeContractReference)
    {
        return CSharpCompilation.Create(
            "CompilerSourceLocationAuthorityTest",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "SourceAuthoritySubject.cs"))],
            includeContractReference
                ? TestMetadataReferences.WithSharpProof
                : TestMetadataReferences.CoreLibraryOnly,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
