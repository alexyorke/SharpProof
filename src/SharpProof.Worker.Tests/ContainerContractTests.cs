using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
[NonParallelizable]
public sealed class ContainerContractTests
{
    [TestCase(false, Architecture.X64)]
    [TestCase(true, Architecture.Arm64)]
    public void NonCanonicalPlatformsAreRejectedBeforeContractValidation(
        bool isLinux,
        Architecture architecture)
    {
        var exception = Assert.Throws<PlatformNotSupportedException>(
            (Action)(() => ContainerContract.RequireCanonicalPlatform(isLinux, architecture)));
        Assert.That(exception!.Message, Does.Contain("Linux amd64"));
    }

    [Test]
    public void CanonicalPlatformPassesBothNativeGuards()
    {
        Assert.DoesNotThrow((Action)(() => ContainerContract.RequireCanonicalPlatform(true, Architecture.X64)));
        Assert.DoesNotThrow((Action)(() => ContainerContract.RequireLinuxZ3Loader(true)));
        Assert.Throws<PlatformNotSupportedException>((Action)(() => ContainerContract.RequireLinuxZ3Loader(false)));
    }

    [TestCase(Architecture.X64, "win-x64", "windows")]
    [TestCase(Architecture.Arm64, "win-arm64", "windowsArm64")]
    public void WindowsContractUsesEmbeddedPinnedDllIdentity(Architecture architecture, string rid, string pinProperty)
    {
        ArgumentNullException.ThrowIfNull(rid);
        ArgumentNullException.ThrowIfNull(pinProperty);
        using var stream = typeof(ContainerContract).Assembly.GetManifestResourceStream("SharpProof.Host.toolchain.json");
        Assert.That(stream, Is.Not.Null);
        using var catalog = JsonDocument.Parse(stream!);
        var z3 = catalog.RootElement.GetProperty("z3");
        var pin = z3.GetProperty(pinProperty);
        var contract = ContainerContract.ValidateWindowsRequired(architecture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contract.Platform, Is.EqualTo(rid));
            Assert.That(contract.Z3Version, Is.EqualTo(z3.GetProperty("version").GetString()));
            Assert.That(contract.Z3LibraryBytes, Is.GreaterThan(0));
            Assert.That(contract.Z3LibraryBytes, Is.EqualTo(pin.GetProperty("libraryBytes").GetInt64()));
            Assert.That(contract.Z3LibrarySha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(contract.Z3LibrarySha256, Is.EqualTo(pin.GetProperty("librarySha256").GetString()));
        }
        Assert.Throws<PlatformNotSupportedException>((Action)(() =>
            ContainerContract.ValidateWindowsRequired(Architecture.X86)));
    }
    [Test]
    public void OpenedContractStreamEnforcesTheActualByteLimit()
    {
        var parseOpenedStream = typeof(ContainerContract).GetMethod(
            "ReadBoundedJson",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(Stream)],
            modifiers: null);
        Assert.That(
            parseOpenedStream,
            Is.Not.Null,
            "The size bound must be applied to the opened file descriptor.");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            new string(' ', 16 * 1024) + "{}"));

        var exception = Assert.Throws<TargetInvocationException>(
            (Action)(() =>
                _ = parseOpenedStream!.Invoke(null, [stream])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                exception!.InnerException,
                Is.TypeOf<InvalidDataException>());
            Assert.That(
                exception.InnerException!.Message,
                Does.Contain("invalid size"));
        }
    }

    [Test]
    public void EmbeddedToolchainDigestMustBeLowercaseSha256()
    {
        var requireDigest = typeof(ContainerContract).GetMethod(
            "RequireSha256",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(requireDigest, Is.Not.Null);
        using var invalid = JsonDocument.Parse(
            "{\"librarySha256\":\"" + new string('g', 64) + "\"}");
        var exception = Assert.Throws<TargetInvocationException>((Action)(() =>
            requireDigest!.Invoke(null, [invalid.RootElement, "librarySha256"])));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>());

        using var valid = JsonDocument.Parse(
            "{\"librarySha256\":\"" + new string('a', 64) + "\"}");
        Assert.That(
            requireDigest!.Invoke(null, [valid.RootElement, "librarySha256"]),
            Is.EqualTo(new string('a', 64)));
    }

    [Test]
    public void RequiredContractNormalizesMalformedJsonAsInvalidData()
    {
        AssertInvalidContractPayload("{");
    }

    [Test]
    public void RequiredContractNormalizesNonObjectJsonAsInvalidData()
    {
        AssertInvalidContractPayload("[]");
    }

    [Test]
    public void RequiredContractRejectsMissingAndMalformedMarkers()
    {
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(
            Architecture.X64));
        var originalContainer = Environment.GetEnvironmentVariable(
            "SHARPPROOF_CONTAINER");
        var originalContract = Environment.GetEnvironmentVariable(
            "SHARPPROOF_CONTAINER_CONTRACT");
        var canonicalContract = string.IsNullOrWhiteSpace(originalContract)
            ? "/etc/sharpproof/container-contract.json"
            : originalContract;
        var canonicalJson = File.ReadAllText(canonicalContract);
        using var temporaryDirectory = new TempDirectory(
            "SharpProof.ContainerContract.");
        var root = temporaryDirectory.FullName;

        try
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", "0");
            Assert.That(ContainerContract.ValidateRequired().Platform, Is.EqualTo("linux-x64"));
            Assert.Throws<PlatformNotSupportedException>(
                (Action)(() => ContainerContract.ValidateCanonicalContainerRequired()));

            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", "1");
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                Path.Combine(root, "missing.json"));
            Assert.Throws<InvalidDataException>(
                (Action)(() => ContainerContract.ValidateRequired()));

            var candidate = Path.Combine(root, "contract.json");
            File.WriteAllText(candidate, string.Empty);
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                candidate);
            Assert.Throws<InvalidDataException>(
                (Action)(() => ContainerContract.ValidateRequired()));

            var mutations = new Action<JsonObject>[]
            {
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["schemaVersion"] = "1"; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["schemaVersion"] = 1; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["schemaVersion"] = JsonNode.Parse("2147483648"); },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["schemaVersion"] = 2; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["powershellArchiveSha256"] = new string('0', 64); },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["z3LibraryBytes"] = "invalid"; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["z3LibraryBytes"] = JsonNode.Parse("9223372036854775808"); },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["z3LibraryBytes"] =
                    contract["z3LibraryBytes"]!.GetValue<long>() + 1; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["z3LibrarySha256"] = new string('0', 64); },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["platform"] = " "; },
                contract => { System.ArgumentNullException.ThrowIfNull(contract); contract["platform"] = "linux/arm64"; }
                ,contract => { System.ArgumentNullException.ThrowIfNull(contract); contract.Remove("dotnetTestRuntimeVersion"); }
            };
            foreach (var mutate in mutations)
            {
                var contract = JsonNode.Parse(canonicalJson)!.AsObject();
                mutate(contract);
                File.WriteAllText(candidate, contract.ToJsonString());
                Assert.Throws<InvalidDataException>(
                    (Action)(() => ContainerContract.ValidateRequired()));
            }

            File.WriteAllText(
                candidate,
                canonicalJson.TrimEnd('}', '\n', '\r') +
                ",\"unexpected\":true}");
            Assert.Throws<InvalidDataException>(
                (Action)(() => ContainerContract.ValidateRequired()));

            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                null);
            Assert.That(ContainerContract.ValidateRequired(), Is.Not.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER",
                originalContainer);
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                originalContract);
        }
    }

    [Test]
    public void Z3PayloadUsesTheDefaultRootAndRejectsTheWrongSize()
    {
        var originalRoot = Environment.GetEnvironmentVariable(
            "SHARPPROOF_NATIVE_ROOT");
        using var temporaryDirectory = new TempDirectory(
            "SharpProof.ContainerContract.");
        var root = temporaryDirectory.FullName;

        try
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_NATIVE_ROOT", null);
            var canonicalLibrary =
                ContainerContract.ResolveZ3LibraryRequired();
            Assert.That(
                File.Exists(canonicalLibrary),
                Is.True);

            Environment.SetEnvironmentVariable("SHARPPROOF_NATIVE_ROOT", root);
            Assert.Throws<InvalidDataException>(
                (Action)(() =>
                    ContainerContract.ResolveZ3LibraryRequired()));

            var contract = ContainerContract.ValidateRequired();
            var library = Path.Combine(
                root,
                "z3",
                contract.Z3Version,
                "linux-x64",
                "libz3.so");
            Directory.CreateDirectory(Path.GetDirectoryName(library)!);
            var missingFile = Assert.Throws<InvalidDataException>(
                (Action)(() => ContainerContract.ResolveZ3LibraryRequired()));
            Assert.That(missingFile!.InnerException, Is.TypeOf<FileNotFoundException>());
            File.WriteAllText(library, string.Empty);
            Assert.Throws<InvalidDataException>(
                (Action)(() =>
                    ContainerContract.ResolveZ3LibraryRequired()));

            File.Copy(canonicalLibrary, library, overwrite: true);
            using (var stream = new FileStream(
                       library,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.Read))
            {
                var original = stream.ReadByte();
                stream.Position = 0;
                stream.WriteByte((byte)(original ^ 0xff));
            }
            Assert.That(
                new FileInfo(library).Length,
                Is.EqualTo(contract.Z3LibraryBytes));
            Assert.Throws<InvalidDataException>(
                (Action)(() =>
                    ContainerContract.ResolveZ3LibraryRequired()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_NATIVE_ROOT",
                originalRoot);
        }
    }

    private static void AssertInvalidContractPayload(string payload)
    {
        System.ArgumentNullException.ThrowIfNull(payload);
        var originalContainer = Environment.GetEnvironmentVariable(
            "SHARPPROOF_CONTAINER");
        var originalContract = Environment.GetEnvironmentVariable(
            "SHARPPROOF_CONTAINER_CONTRACT");
        using var temporaryDirectory = new TempDirectory(
            "SharpProof.ContainerContract.");
        var root = temporaryDirectory.FullName;
        var candidate = Path.Combine(root, "contract.json");

        try
        {
            File.WriteAllText(candidate, payload);
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", "1");
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                candidate);

            var exception = Assert.Throws<InvalidDataException>(
                (Action)(() => ContainerContract.ValidateRequired()));
            Assert.That(
                exception!.Message,
                Does.StartWith("The SharpProof container contract"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER",
                originalContainer);
            Environment.SetEnvironmentVariable(
                "SHARPPROOF_CONTAINER_CONTRACT",
                originalContract);
        }
    }
}
