using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275HashStreamInputTests
{
    [Test]
    public void APositionBeyondTheActualStreamLengthIsRejected()
    {
        using var writer = new CanonicalHashWriter();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);
        stream.Position = stream.Length + 1;
        var error = Assert.Throws<ArgumentException>((Action)(() => writer.Add(stream)));
        Assert.That(error!.ParamName, Is.EqualTo("stream"));
    }

    [Test]
    public void AnActualStreamSuffixHasTheSameFrameAsItsBytes()
    {
        using var streamWriter = new CanonicalHashWriter();
        using var byteWriter = new CanonicalHashWriter();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);
        stream.Position = 1;
        Assert.That(streamWriter.Add(stream).Finish(),
            Is.EqualTo(byteWriter.Add(new byte[] { 2, 3 }).Finish()));
    }
}
