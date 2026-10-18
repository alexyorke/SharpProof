using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusCatalogWorkflowTests
{
    [TestCase(true, TestName = "ManifestWithoutMethodsReportsMissingList")]
    [TestCase(false, TestName = "PinnedSourceWithoutContentReportsSourceFileKey")]
    public void MissingManifestDataReportsItsExactDiagnostic(bool missingMethods)
    {
        using var temporary = new TempDirectory("SharpProof-incomplete-catalog-");
        var source = OpenSourceCorpusCatalog.GetCorpusDirectory(RepositoryLayout.FindRoot());
        var target = OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var path = Path.Combine(target, "oss-methods.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        string expectedMessage;
        if (missingMethods)
        {
            manifest.AsObject().Remove("methods");
            expectedMessage = "The OSS corpus manifest must list its methods.";
        }
        else
        {
            var file = manifest["files"]!.AsArray()[0]!;
            var key = OpenSourceCorpusCatalog.GetSourceFileKey(
                file["sourceId"]!.GetValue<string>(), file["path"]!.GetValue<string>());
            file["content"] = null;
            expectedMessage = $"OSS corpus source file has no content: {key}.";
        }
        var contents = manifest.ToJsonString();
        File.WriteAllText(path, contents);

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message, Is.EqualTo(expectedMessage));
        Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
    }

    [Test]
    public void MissingPinnedManifestReportsItsExpectedLocation()
    {
        using var temporary = new TempDirectory("SharpProof-missing-catalog-");
        var path = Path.Combine(OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName), "oss-methods.json");

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message, Is.EqualTo($"The checked-in OSS corpus manifest is missing: {path}"));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void DistinctCorpusIdsCannotCountTheSameSourceDeclarationTwice()
    {
        using var temporary = new TempDirectory("SharpProof-duplicate-catalog-declaration-");
        var source = OpenSourceCorpusCatalog.GetCorpusDirectory(RepositoryLayout.FindRoot());
        var target = OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var path = Path.Combine(target, "oss-methods.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        var methods = manifest["methods"]!.AsArray();
        var originalId = methods[1]!["id"]!.GetValue<string>();
        var originalPath = methods[1]!["path"]!.GetValue<string>();
        var firstPath = methods[0]!["path"]!.GetValue<string>();
        var files = manifest["files"]!.AsArray();
        var firstFile = files.Single(file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file!["path"]!.GetValue<string>() == firstPath))!;
        var secondFile = files.Single(file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file!["path"]!.GetValue<string>() == originalPath))!;
        secondFile["content"] = firstFile["content"]!.DeepClone();
        secondFile["contentSha256"] = firstFile["contentSha256"]!.DeepClone();
        var duplicate = methods[0]!.DeepClone();
        duplicate["id"] = originalId;
        duplicate["path"] = originalPath;
        methods[1] = duplicate;
        var contents = manifest.ToJsonString();
        File.WriteAllText(path, contents);

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message, Is.EqualTo($"OSS corpus method {originalId} duplicates another declaration."));
        Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
    }

    [Test]
    public void TwoHundredActualPinnedDeclarationsStillRequireSourceFileDiversity()
    {
        using var temporary = new TempDirectory("SharpProof-catalog-source-diversity-");
        var repositoryRoot = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var candidates = document.Files.SelectMany(file =>
        {
            System.ArgumentNullException.ThrowIfNull(file);
            var root = CSharpSyntaxTree.ParseText(file.Content).GetCompilationUnitRoot();
            return OpenSourceCorpusCatalog.BuildDeclarationIndex(root).Values
                .Where(static declarations => declarations.Length == 1)
                .Select(declarations =>
                {

                    if (declarations.IsDefault)
                    {
                        throw new System.ArgumentException("Array must be initialized.", nameof(declarations));
                    }
                    var declaration = declarations.Single();
                    var span = declaration.GetLocation().GetLineSpan();
                    return new OpenSourceCorpusMethod("pending", file.SourceId, file.Path,
                        span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                        OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
                        declaration.Identifier.ValueText, "effects", CorpusVerdict.Unknown, CorpusSupport.IntentionallyUnsupported);
                });
        }).DistinctBy(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.DeclarationSha256)).ToArray();
        var groups = candidates.GroupBy(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (OpenSourceCorpusCatalog.GetSourceFileKey(method.SourceId, method.Path)))
            .OrderByDescending(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Count()))
            .ThenBy(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Key), StringComparer.Ordinal)
            .Take(OpenSourceCorpusCatalog.MinimumSourceFileCount - 1)
            .Select(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.ToArray())).ToArray();
        Assert.That(groups, Has.Length.EqualTo(24));
        var selected = groups.Select(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group[0]))
            .Concat(groups.SelectMany(static group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Skip(1))))
            .Take(OpenSourceCorpusCatalog.MinimumMethodCount)
            .Select(static (method, index) => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method with { Id = $"OSS{index + 1:D4}" })).ToArray();
        Assert.That(selected, Has.Length.EqualTo(200));
        Assert.That(OpenSourceCorpusCatalog.CountSourceFiles(selected), Is.EqualTo(24));

        var source = OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var target = OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var path = Path.Combine(target, "oss-methods.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["methods"] = JsonSerializer.SerializeToNode(selected);
        var contents = manifest.ToJsonString();
        File.WriteAllText(path, contents);

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message,
            Is.EqualTo("The OSS corpus spans only 24 source files; 25 are required to prevent one-file padding."));
        Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
    }
}
