using NUnit.Framework;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusSnapshotRowWorkflowTests
{
    [TestCase("ordinary|Unreviewed|Proven|")]
    [TestCase("ordinary|999|Proven|")]
    [TestCase("ordinary|Proven|Unreviewed|")]
    [TestCase("ordinary|Proven|Proven")]
    public void SnapshotWriterRejectsRowsWithUnknownOrIncompleteVerdictMetadata(string row)
    {
        System.ArgumentNullException.ThrowIfNull(row);
        var error = Assert.Throws<InvalidDataException>((Action)(() => CorpusSnapshotFormat.Render([row])));

        Assert.That(error!.Message,
            Is.EqualTo("Corpus snapshot does not use the canonical schema-3 byte format."));
    }
}
