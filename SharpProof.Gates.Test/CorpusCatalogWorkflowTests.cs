using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusCatalogWorkflowTests
{
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
        var firstFile = files.Single(file => file!["path"]!.GetValue<string>() == firstPath)!;
        var secondFile = files.Single(file => file!["path"]!.GetValue<string>() == originalPath)!;
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
}
