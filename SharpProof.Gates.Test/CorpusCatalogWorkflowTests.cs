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
        var duplicate = methods[0]!.DeepClone();
        duplicate["id"] = originalId;
        methods[1] = duplicate;
        var contents = manifest.ToJsonString();
        File.WriteAllText(path, contents);

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message, Is.EqualTo($"OSS corpus method {originalId} duplicates another declaration."));
        Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
    }
}
