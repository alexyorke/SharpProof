using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
[NonParallelizable]
public sealed class LinuxHostContractTests
{
    [TestCase(Architecture.X64, "linux-x64")]
    [TestCase(Architecture.Arm64, "linux-arm64")]
    public void SupportedNativeHostsUseEmbeddedPins(Architecture architecture, string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        var contract = new LinuxHostContract(true, architecture, architecture, runtimeIdentifier: rid).ValidateRequired();
        Assert.That(contract.Platform, Is.EqualTo(rid));
        Assert.That(contract.Z3LibraryBytes, Is.GreaterThan(0));
        Assert.That(contract.Z3LibrarySha256, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(contract.VerifierPackageId, Is.EqualTo("SharpProof.Verifier"));
    }

    [TestCase(false, Architecture.X64, Architecture.X64, "linux-x64")]
    [TestCase(true, Architecture.Arm64, Architecture.X64, "linux-x64")]
    [TestCase(true, Architecture.X86, Architecture.X86, "linux-x86")]
    [TestCase(true, Architecture.X64, Architecture.X64, "linux-musl-x64")]
    [TestCase(true, Architecture.Arm64, Architecture.Arm64, "linux-bionic-arm64")]
    public void UnsupportedHostsFailBeforePayloadLoading(bool linux, Architecture host, Architecture process, string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        var contract = new LinuxHostContract(linux, host, process, runtimeIdentifier: rid);
        Assert.Throws<PlatformNotSupportedException>((Action)(() => contract.ValidateRequired()));
    }

    [Test]
    public void MissingAndMalformedCatalogsCannotAdmitAHost()
    {
        var missing = new LinuxHostContract(openToolchain: static () => null);
        Assert.Throws<InvalidDataException>((Action)(() => missing.ValidateRequired()));
        var malformed = new LinuxHostContract(openToolchain: static () => new MemoryStream(Encoding.UTF8.GetBytes("{")));
        Assert.Catch<JsonException>((Action)(() => malformed.ValidateRequired()));
    }

    [TestCase("libraryBytes", "0")]
    [TestCase("librarySha256", "null")]
    [TestCase("librarySha256", "\"short\"")]
    [TestCase("librarySha256", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [TestCase("librarySha256", "\"gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg\"")]
    public void MalformedNativePinsAreRejected(string property, string jsonValue)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(jsonValue);
        using var payload = new Payload();
        var catalog = JsonNode.Parse(payload.Catalog)!;
        catalog["z3"]![property] = JsonNode.Parse(jsonValue);
        payload.Catalog = catalog.ToJsonString();
        Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.ValidateRequired()));
    }

    [TestCase("")]
    [TestCase("relative/tools/net10/SharpProof.Host.dll")]
    [TestCase("/other/net10/SharpProof.Host.dll")]
    [TestCase("/tools/net9/SharpProof.Host.dll")]
    public void PayloadResolutionRequiresTheExactPackageClosure(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var contract = new LinuxHostContract(assemblyLocation: location);
        Assert.Throws<InvalidDataException>((Action)(() => contract.ResolveZ3LibraryRequired()));
    }

    [TestCase(Architecture.X64)]
    [TestCase(Architecture.Arm64)]
    public void NativeAndManagedPayloadsAreVerifiedFromTheSelectedPackageClosure(Architecture architecture)
    {
        using var payload = new Payload(architecture);
        Assert.That(payload.Contract.ResolveZ3LibraryRequired(), Is.EqualTo(payload.NativePath));
        Assert.That(payload.Contract.GetZ3LibrarySha256Required(), Is.EqualTo(payload.NativeDigest));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingNativeOrManagedPayloadIsRejected(bool managed)
    {
        using var payload = new Payload();
        File.Delete(managed ? payload.ManagedPath : payload.NativePath);
        var error = Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.ResolveZ3LibraryRequired()));
        Assert.That(error!.Message, Does.Contain("payload is missing"));
    }

    [Test]
    public void MissingNativeDirectoryIsRejected()
    {
        using var payload = new Payload();
        Directory.Delete(Path.GetDirectoryName(payload.NativePath)!, recursive: true);
        Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.ResolveZ3LibraryRequired()));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ChangedNativeOrManagedPayloadCannotBeLoaded(bool managed, bool size)
    {
        using var payload = new Payload();
        var path = managed ? payload.ManagedPath : payload.NativePath;
        var bytes = File.ReadAllBytes(path);
        if (size)
        {
            bytes = [.. bytes, 0];
        }
        else
        {
            bytes[0] ^= 1;
        }
        File.WriteAllBytes(path, bytes);
        var error = Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.LoadZ3LibraryRequired()));
        Assert.That(error!.Message, Does.Contain("pinned digest"));
    }

    [Test]
    public void LoaderUsesTheVerifiedDescriptorEvenIfThePathIsReplaced()
    {
        using var payload = new Payload(nativeBytes: File.ReadAllBytes(ContainerContract.ResolveZ3LibraryRequired()),
            loadVerified: static stream =>
            {
                ArgumentNullException.ThrowIfNull(stream);
                var path = stream.Name;
                File.Delete(path);
                File.WriteAllText(path, "unverified replacement");
                return ContainerContract.LoadVerifiedZ3Library(stream, isWindows: false);
            });
        var handle = payload.Contract.LoadZ3LibraryRequired();
        try
        {
            Assert.That(NativeLibrary.TryGetExport(handle, "Z3_get_version", out _), Is.True);
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    [Test]
    public void DefaultLoaderLoadsTheValidatedNativePayload()
    {
        using var payload = new Payload(nativeBytes: File.ReadAllBytes(ContainerContract.ResolveZ3LibraryRequired()));
        var handle = payload.Contract.LoadZ3LibraryRequired();
        try
        {
            Assert.That(NativeLibrary.TryGetExport(handle, "Z3_get_version", out _), Is.True);
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    [Test]
    public void NativeRoutingIgnoresContainerPayloadOverridesButRetainsCanonicalMarkerValidation()
    {
        var container = Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER");
        var nativeRoot = Environment.GetEnvironmentVariable("SHARPPROOF_NATIVE_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", null);
            Environment.SetEnvironmentVariable("SHARPPROOF_NATIVE_ROOT", "/untrusted/native");
            Assert.That(ContainerContract.ValidateRequired().Platform, Is.EqualTo("linux-x64"));
            Assert.That(LinuxHostContract.Current.ValidateRequired().Platform, Is.EqualTo("linux-x64"));
            // The test host is not installed under a package tools/net10 closure.
            Assert.Throws<InvalidDataException>((Action)(() => ContainerContract.ResolveZ3LibraryRequired()));
            Assert.Throws<InvalidDataException>((Action)(() => ContainerContract.GetZ3LibrarySha256Required()));
            Assert.Throws<InvalidDataException>((Action)(() => ContainerContract.LoadZ3LibraryRequired()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", container);
            Environment.SetEnvironmentVariable("SHARPPROOF_NATIVE_ROOT", nativeRoot);
        }
    }

    private sealed class Payload : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "SharpProof.LinuxHost." + Guid.NewGuid().ToString("N"));
        internal string NativePath { get; }
        internal string ManagedPath { get; }
        internal string NativeDigest { get; }
        internal string Catalog;
        internal LinuxHostContract Contract { get; }

        internal Payload(Architecture architecture = Architecture.X64, byte[]? nativeBytes = null,
            Func<FileStream, IntPtr>? loadVerified = null)
        {
            nativeBytes ??= Encoding.UTF8.GetBytes("native payload");
            var managedBytes = Encoding.UTF8.GetBytes("managed payload");
            var rid = architecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            NativePath = Path.Combine(root, "tools", "native", rid, "libz3.so");
            ManagedPath = Path.Combine(root, "tools", "net10", "Microsoft.Z3.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(NativePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ManagedPath)!);
            File.WriteAllBytes(NativePath, nativeBytes);
            File.WriteAllBytes(ManagedPath, managedBytes);
            NativeDigest = Convert.ToHexStringLower(SHA256.HashData(nativeBytes));
            var pin = new { libraryBytes = nativeBytes.Length, librarySha256 = NativeDigest };
            Catalog = JsonSerializer.Serialize(new
            {
                z3 = new
                {
                    version = "4.12.2",
                    libraryBytes = pin.libraryBytes,
                    librarySha256 = pin.librarySha256,
                    managedAssemblyBytes = managedBytes.Length,
                    managedAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(managedBytes)),
                    linuxArm64 = pin
                },
                support = new { verifierPackageId = "SharpProof.Verifier" }
            });
            Contract = new LinuxHostContract(true, architecture, architecture,
                Path.Combine(root, "tools", "net10", "SharpProof.Host.dll"),
                () => new MemoryStream(Encoding.UTF8.GetBytes(Catalog)), rid, loadVerified);
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
