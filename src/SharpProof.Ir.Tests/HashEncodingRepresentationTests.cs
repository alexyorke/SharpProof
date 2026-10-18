using System.IO;
using System.Text.Json;
using NUnit.Framework;
using IrHashEncoding = SharpProof.Ir.HashEncoding;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class HashEncodingRepresentationTests
{
    private const string AbcDigest =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyDigest =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Test]
    public void StreamDigestUsesOnlyTheRemainingBytesAndKeepsTheStreamOpen()
    {
        RecordBinding(nameof(StreamDigestUsesOnlyTheRemainingBytesAndKeepsTheStreamOpen));
        byte[] bytes = [88, 97, 98, 99];
        using var stream = new MemoryStream(bytes, writable: false);
        stream.Position = 1;
        var actual = IrHashEncoding.ComputeSha256Hex(stream);
        byte[] suffix = [97, 98, 99];

        Assert.That(actual, Is.EqualTo(AbcDigest));
        Assert.That(actual, Is.EqualTo(IrHashEncoding.ComputeSha256Hex(suffix)));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(IrHashEncoding.ComputeSha256Hex(stream), Is.EqualTo(EmptyDigest));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void DigestTextValidationMatchesTheCanonicalLowercaseRepresentation()
    {
        RecordBinding(nameof(DigestTextValidationMatchesTheCanonicalLowercaseRepresentation));
        byte[] bytes = [97, 98, 99];
        var actual = IrHashEncoding.ComputeSha256Hex(bytes);

        Assert.That(actual, Is.EqualTo(AbcDigest));
        Assert.That(IrHashEncoding.IsSha256(actual), Is.True);
        Assert.That(IrHashEncoding.IsSha256(new string('0', 64)), Is.True);
        Assert.That(IrHashEncoding.IsSha256(new string('f', 64)), Is.True);
        Assert.That(IrHashEncoding.IsSha256(null), Is.False);
        Assert.That(IrHashEncoding.IsSha256(new string('0', 63)), Is.False);
        Assert.That(IrHashEncoding.IsSha256(new string('0', 65)), Is.False);
        Assert.That(IrHashEncoding.IsSha256(AbcDigest.ToUpperInvariant()), Is.False);
        Assert.That(IrHashEncoding.IsSha256(new string('g', 64)), Is.False);
    }

    private static void RecordBinding(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        var assembly = typeof(IrHashEncoding).Assembly;
        Assert.That(assembly.GetName().Name, Is.EqualTo("SharpProof.Ir"));
        Assert.That(assembly, Is.Not.SameAs(typeof(HashEncodingRepresentationTests).Assembly));
        TestContext.WriteLine("SP_IR_HASH_REPRESENTATION_V1 " + JsonSerializer.Serialize(new
        {
            Scenario = scenario,
            ImplementationType = typeof(IrHashEncoding).FullName,
            AssemblyName = assembly.GetName().Name,
            ModuleMvid = assembly.ManifestModule.ModuleVersionId
        }));
    }
}
