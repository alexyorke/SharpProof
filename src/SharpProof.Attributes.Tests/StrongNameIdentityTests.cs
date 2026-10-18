using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Attributes.Test;

[TestFixture]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates test fixtures through reflection.")]
internal sealed class StrongNameIdentityTests
{
    [Test]
    public void ContractAssemblyHasStableFullySignedIdentity()
    {
        var assembly = typeof(Contract).Assembly;
        var identity = assembly.GetName();
        var image = ReadOriginalSignedAssembly();
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var corHeader = pe.PEHeaders.CorHeader ?? throw new InvalidDataException("Missing CLR header.");
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        Assert.That(metadata.GetString(definition.Name), Is.EqualTo(identity.Name));
        Assert.That(definition.Version, Is.EqualTo(identity.Version));
        Assert.That(metadata.GetBlobBytes(definition.PublicKey), Is.EqualTo(identity.GetPublicKey()));
        Assert.That(identity.Flags.HasFlag(AssemblyNameFlags.PublicKey), Is.True);
        Assert.That(Convert.ToHexString(identity.GetPublicKeyToken()!), Is.EqualTo("75825C3E099F5FAC"));
        Assert.That(corHeader.Flags.HasFlag(CorFlags.StrongNameSigned), Is.True);
        Assert.That(corHeader.StrongNameSignatureDirectory.Size, Is.EqualTo(256));
        Assert.That(VerifySignature(image), Is.True,
            "A public key token or public-sign flag alone is insufficient for .NET Framework consumers.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SignatureVerificationRejectsUnsignedOrModifiedPayload(bool modifyMethodBody)
    {
        var image = ReadOriginalSignedAssembly();
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var corHeader = pe.PEHeaders.CorHeader ?? throw new InvalidDataException("Missing CLR header.");
        if (modifyMethodBody)
        {
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.MethodDefinitions)
            {
                var method = metadata.GetMethodDefinition(handle);
                if (metadata.GetString(method.Name) == nameof(Contract.Requires))
                {
                    image[FileOffset(pe.PEHeaders, method.RelativeVirtualAddress) + 1] ^= 1;
                    break;
                }
            }
        }
        else
        {
            Array.Clear(image, FileOffset(pe.PEHeaders, corHeader.StrongNameSignatureDirectory.RelativeVirtualAddress),
                corHeader.StrongNameSignatureDirectory.Size);
        }
        Assert.That(VerifySignature(image), Is.False);
    }

    private static byte[] ReadOriginalSignedAssembly()
    {
        // Coverage instrumentation changes the loaded DLL's signed payload. The
        // resource contains the actual build output captured before instrumentation.
        using var stream = typeof(StrongNameIdentityTests).Assembly.GetManifestResourceStream(
            "SharpProof.Attributes.Test.OriginalSignedAttributes") ??
            throw new InvalidDataException("The original signed Attributes build output was not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static bool VerifySignature(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var corHeader = headers.CorHeader ?? throw new InvalidDataException("Missing CLR header.");
        var metadata = pe.GetMetadataReader();
        var publicKey = metadata.GetBlobBytes(metadata.GetAssemblyDefinition().PublicKey);
        Assert.That(publicKey, Has.Length.EqualTo(288));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(publicKey.AsSpan(20)), Is.EqualTo(0x31415352u));
        var modulus = publicKey.AsSpan(32).ToArray();
        Array.Reverse(modulus);
        var exponent = publicKey.AsSpan(28, 4).ToArray();
        Array.Reverse(exponent);
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        var signatureDirectory = corHeader.StrongNameSignatureDirectory;
        var signatureOffset = FileOffset(headers, signatureDirectory.RelativeVirtualAddress);
        var signature = image.AsSpan(signatureOffset, signatureDirectory.Size).ToArray();
        Array.Reverse(signature);

        // Strong-name hashing omits header alignment padding and the signature
        // blob. PE checksum/certificate directory fields are hashed as zero.
        var signedImage = (byte[])image.Clone();
        Array.Clear(signedImage, headers.PEHeaderStartOffset + 64, 4);
        var directoriesOffset = headers.PEHeaderStartOffset + (headers.PEHeader!.Magic == PEMagic.PE32 ? 96 : 112);
        Array.Clear(signedImage, directoriesOffset + 4 * 8, 8);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(signedImage, 0,
            headers.PEHeaderStartOffset + headers.CoffHeader.SizeOfOptionalHeader + headers.SectionHeaders.Length * 40);
        foreach (var section in headers.SectionHeaders)
        {
            var start = section.PointerToRawData;
            var end = start + section.SizeOfRawData;
            if (signatureOffset >= start && signatureOffset < end)
            {
                hash.AppendData(signedImage, start, signatureOffset - start);
                hash.AppendData(signedImage, signatureOffset + signatureDirectory.Size,
                    end - signatureOffset - signatureDirectory.Size);
            }
            else
            {
                hash.AppendData(signedImage, start, section.SizeOfRawData);
            }
        }
        return rsa.VerifyHash(hash.GetHashAndReset(), signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
    }

    private static int FileOffset(PEHeaders headers, int relativeVirtualAddress)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var section in headers.SectionHeaders)
        {
            if (relativeVirtualAddress >= section.VirtualAddress &&
                relativeVirtualAddress < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
            {
                return section.PointerToRawData + relativeVirtualAddress - section.VirtualAddress;
            }
        }
        throw new InvalidDataException("The strong-name data is outside the PE sections.");
    }
}
