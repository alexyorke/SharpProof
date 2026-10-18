using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class MacOsHostContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void HostSelectionInvokesOnlyTheSelectedBackend(bool isMacOs)
    {
        var macCalls = 0;
        var containerCalls = 0;
        var selected = ContainerContract.SelectHost(isMacOs,
            () => { macCalls++; return "mac"; },
            () => { containerCalls++; return "container"; });

        Assert.That(selected(), Is.EqualTo(isMacOs ? "mac" : "container"));
        Assert.That(macCalls, Is.EqualTo(isMacOs ? 1 : 0));
        Assert.That(containerCalls, Is.EqualTo(isMacOs ? 0 : 1));
    }

    [Test]
    public void CurrentHostCannotUseMacVerificationOnLinux()
    {
        Assert.Throws<PlatformNotSupportedException>((Action)(() => MacOsHostContract.Current.ValidateRequired()));
    }

    [TestCase(false, Architecture.X64, Architecture.X64)]
    [TestCase(true, Architecture.Arm64, Architecture.X64)]
    [TestCase(true, Architecture.X86, Architecture.X86)]
    public void UnsupportedOrTranslatedHostsAreRejected(bool isMacOs, Architecture host, Architecture process)
    {
        var contract = new MacOsHostContract(isMacOs, host, process);
        Assert.Throws<PlatformNotSupportedException>((Action)(() => contract.ValidateRequired()));
    }

    [TestCase(Architecture.Arm64, "osx-arm64")]
    [TestCase(Architecture.X64, "osx-x64")]
    public void SupportedHostsUseTheEmbeddedArchitecturePin(Architecture architecture, string rid)
    {
        System.ArgumentNullException.ThrowIfNull(rid);
        var contract = new MacOsHostContract(true, architecture, architecture).ValidateRequired();
        Assert.That(contract.Platform, Is.EqualTo(rid));
        Assert.That(contract.Z3Version, Is.EqualTo("4.12.2"));
        Assert.That(contract.Z3LibraryBytes, Is.GreaterThan(0));
        Assert.That(contract.Z3LibrarySha256, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(contract.VerifierPackageId, Is.EqualTo("SharpProof.Verifier"));
    }

    [Test]
    public void MissingOrMalformedToolchainCannotEstablishAContract()
    {
        var missing = new MacOsHostContract(true, Architecture.X64, Architecture.X64,
            openToolchain: () => null);
        Assert.Throws<InvalidDataException>((Action)(() => missing.ValidateRequired()));
        var malformed = new MacOsHostContract(true, Architecture.X64, Architecture.X64,
            openToolchain: () => new MemoryStream(Encoding.UTF8.GetBytes("{")));
        Assert.Catch<JsonException>((Action)(() => malformed.ValidateRequired()));
    }

    [TestCase(Architecture.Arm64)]
    [TestCase(Architecture.X64)]
    public void VerifiedPackageClosureResolvesItsAbsoluteNativePathAndDigest(Architecture architecture)
    {
        using var payload = new Payload(architecture);
        Assert.That(payload.Contract.ResolveZ3LibraryRequired(), Is.EqualTo(payload.NativePath));
        Assert.That(payload.Contract.GetZ3LibrarySha256Required(), Is.EqualTo(payload.NativeDigest));
    }

    [TestCase("")]
    [TestCase("other/net10/SharpProof.Host.dll")]
    [TestCase("tools/net8/SharpProof.Host.dll")]
    public void HostMustResideInsideThePackageToolsClosure(string assemblyLocation)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyLocation);
        var contract = new MacOsHostContract(true, Architecture.X64, Architecture.X64,
            assemblyLocation: assemblyLocation);
        Assert.Throws<InvalidDataException>((Action)(() => contract.ResolveZ3LibraryRequired()));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingNativeOrManagedPayloadIsRejected(bool managed)
    {
        using var payload = new Payload();
        File.Delete(managed ? payload.ManagedPath : payload.NativePath);
        var exception = Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.ResolveZ3LibraryRequired()));
        Assert.That(exception!.Message, Does.Contain("payload is missing"));
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
    public void ChangedSizeOrDigestOfEitherPayloadIsRejected(bool managed, bool changeSize)
    {
        using var payload = new Payload();
        var path = managed ? payload.ManagedPath : payload.NativePath;
        var bytes = File.ReadAllBytes(path);
        if (changeSize)
        {
            File.WriteAllBytes(path, [.. bytes, 0]);
        }
        else
        {
            bytes[0] ^= 1;
            File.WriteAllBytes(path, bytes);
        }
        var exception = Assert.Throws<InvalidDataException>((Action)(() => payload.Contract.ResolveZ3LibraryRequired()));
        Assert.That(exception!.Message, Does.Contain("pinned digest"));
    }

    [Test]
    public void LoaderLoadsTheVerifiedAbsolutePath()
    {
        // Exercise the loading mechanism with a library this Linux host can load.
        // Native macOS consumers separately exercise the pinned Mach-O libraries.
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

    private sealed class Payload : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "SharpProof.MacHost." + Guid.NewGuid().ToString("N"));
        internal string NativePath { get; }
        internal string ManagedPath { get; }
        internal string NativeDigest { get; }
        internal MacOsHostContract Contract { get; }

        internal Payload(Architecture architecture = Architecture.X64, byte[]? nativeBytes = null)
        {
            nativeBytes ??= Encoding.UTF8.GetBytes("native payload");
            var managedBytes = Encoding.UTF8.GetBytes("managed payload");
            var rid = architecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            NativePath = Path.Combine(root, "tools", "native", rid, "libz3.dylib");
            ManagedPath = Path.Combine(root, "tools", "net10", "Microsoft.Z3.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(NativePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ManagedPath)!);
            File.WriteAllBytes(NativePath, nativeBytes);
            File.WriteAllBytes(ManagedPath, managedBytes);
            NativeDigest = Convert.ToHexStringLower(SHA256.HashData(nativeBytes));
            var pin = new { libraryBytes = nativeBytes.Length, librarySha256 = NativeDigest };
            var catalog = JsonSerializer.SerializeToUtf8Bytes(new
            {
                z3 = new
                {
                    version = "4.12.2",
                    managedAssemblyBytes = managedBytes.Length,
                    managedAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(managedBytes))
                },
                macos = new { arm64 = pin, x64 = pin },
                support = new { verifierPackageId = "SharpProof.Verifier" }
            });
            Contract = new MacOsHostContract(true, architecture, architecture,
                Path.Combine(root, "tools", "net10", "SharpProof.Host.dll"),
                () => new MemoryStream(catalog, writable: false));
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
