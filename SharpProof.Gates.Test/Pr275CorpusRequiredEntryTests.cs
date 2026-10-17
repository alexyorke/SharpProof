using System.Text.Json;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class Pr275CorpusRequiredEntryTests
{
    [TestCase("source", "OSS corpus sources must not be null.")]
    [TestCase("file", "OSS corpus source files must not be null.")]
    [TestCase("method", "OSS corpus method at index 0 must not be null.")]
    public void RequiredCorpusEntriesCannotBeNull(string entry, string message)
    {
        System.ArgumentNullException.ThrowIfNull(message);
        var repository = RepositoryLayout.FindRoot();
        var original = OpenSourceCorpusCatalog.Load(repository);
        using var temporary = new TempDirectory("SharpProof.Gates.Test-pr275-required-entry-");
        var corpus = Path.Combine(temporary.FullName, "SharpProof.Gates", "Corpus");
        Directory.CreateDirectory(corpus);
        foreach (var source in original.Sources)
        {
            var destination = Path.Combine(corpus, source.LicenseFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(repository, "SharpProof.Gates", "Corpus", source.LicenseFile), destination);
        }
        var manifestPath = Path.Combine(corpus, "oss-methods.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(original));
        Assert.That(JsonSerializer.Serialize(OpenSourceCorpusCatalog.Load(temporary.FullName)),
            Is.EqualTo(JsonSerializer.Serialize(original)));

        var incomplete = entry switch
        {
            "source" => original with { Sources = original.Sources.SetItem(0, null!) },
            "file" => original with { Files = original.Files.SetItem(0, null!) },
            "method" => original with { Methods = original.Methods.SetItem(0, null!) },
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(incomplete));
        var error = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.Load(temporary.FullName)));
        Assert.That(error!.Message, Is.EqualTo(message));
    }
}
