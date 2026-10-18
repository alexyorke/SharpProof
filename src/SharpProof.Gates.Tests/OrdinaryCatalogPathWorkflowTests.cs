using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryCatalogPathWorkflowTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void AbsoluteLicenseOrSourceLocationRequiresAContainedRelativePath(bool license)
    {
        using var temporary = new TempDirectory("SharpProof-absolute-catalog-location-");
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
        var absoluteLocation = Path.Combine(temporary.FullName, license ? "LICENSE.txt" : "ordinary.cs");
        string description;
        if (license)
        {
            var row = manifest["sources"]!.AsArray()[0]!;
            description = "source " + row["id"]!.GetValue<string>() + " license";
            row["licenseFile"] = absoluteLocation;
        }
        else
        {
            var row = manifest["files"]!.AsArray()[0]!;
            description = "source file " + absoluteLocation;
            row["path"] = absoluteLocation;
        }
        var contents = manifest.ToJsonString();
        File.WriteAllText(path, contents);

        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusCatalog.Load(temporary.FullName)));

        Assert.That(error!.Message, Is.EqualTo("OSS corpus " + description + " path must be relative and contained."));
        Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
        Assert.That(File.Exists(absoluteLocation), Is.False);
    }
}
