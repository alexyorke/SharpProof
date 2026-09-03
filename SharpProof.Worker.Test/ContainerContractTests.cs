using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
[NonParallelizable]
public sealed class ContainerContractTests
{
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
            Assert.Throws<PlatformNotSupportedException>(
                (Action)(() => ContainerContract.ValidateRequired()));

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
                contract => contract["schemaVersion"] = "1",
                contract => contract["schemaVersion"] = 2,
                contract => contract["z3LibraryBytes"] = "invalid",
                contract => contract["z3LibraryBytes"] =
                    contract["z3LibraryBytes"]!.GetValue<long>() + 1,
                contract => contract["platform"] = " ",
                contract => contract["platform"] = "linux/arm64"
                ,contract => contract.Remove("dotnetTestRuntimeVersion")
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
            Assert.That(
                File.Exists(ContainerContract.ResolveZ3LibraryRequired()),
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
            File.WriteAllText(library, string.Empty);
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
