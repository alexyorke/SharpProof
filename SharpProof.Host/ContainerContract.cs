using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpProof.Host;

public sealed record ContainerContractInfo(
    int ContractVersion,
    string Platform,
    string DotNetSdkVersion,
    string Z3Version,
    long Z3LibraryBytes,
    string Z3LibrarySha256,
    string VerifierPackageId);

public static class ContainerContract
{
    private const int MaximumContractBytes = 16 * 1024;
    private const string DefaultContractPath =
        "/etc/sharpproof/container-contract.json";
    private const string EmbeddedToolchainName =
        "SharpProof.Host.toolchain.json";

    public static ContainerContractInfo ValidateRequired()
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "SharpProof verification requires the canonical Linux amd64 container.");
        }
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
                "1",
                StringComparison.Ordinal))
        {
            throw new PlatformNotSupportedException(
                "SharpProof verification requires the canonical container contract.");
        }

        var contractPath = Environment.GetEnvironmentVariable(
            "SHARPPROOF_CONTAINER_CONTRACT");
        if (string.IsNullOrWhiteSpace(contractPath))
        {
            contractPath = DefaultContractPath;
        }
        contractPath = Path.GetFullPath(contractPath);
        if (!File.Exists(contractPath))
        {
            throw new InvalidDataException(
                "The SharpProof container contract marker is missing.");
        }

        using var expectedDocument = ReadEmbeddedToolchain();
        using var actualDocument = ReadBoundedJson(contractPath);
        var expected = expectedDocument.RootElement;
        var actual = actualDocument.RootElement;
        if (actual.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The SharpProof container contract root is not a JSON object.");
        }
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "contractVersion", "platform", "dotnetSdkVersion",
            "dotnetMinimumSdkVersion", "dotnetMinimumSdkFrameworkVersion",
            "dotnetTestRuntimeVersion", "dotnetBaseImage", "dotnetBaseImageDigest",
            "powershellVersionLine", "powershellImageDigest", "z3Version",
            "z3LibraryBytes", "z3LibrarySha256", "verifierPackageId"
        };
        foreach (var property in actual.EnumerateObject())
        {
            if (!required.Remove(property.Name))
            {
                throw new InvalidDataException(
                    $"The SharpProof container contract property '{property.Name}' is unknown or duplicated.");
            }
        }
        if (required.Count != 0)
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{required.First()}' is missing.");
        }
        RequireInteger(actual, "schemaVersion", 1);
        RequireInteger(
            actual,
            "contractVersion",
            RequireInteger(expected, "containerContractVersion"));
        RequireString(
            actual,
            "platform",
            RequireString(expected, "platform"));
        RequireString(
            actual,
            "dotnetSdkVersion",
            RequireString(expected.GetProperty("dotnet"), "sdkVersion"));
        RequireString(actual, "dotnetMinimumSdkVersion", RequireString(expected.GetProperty("dotnet"), "minimumSdkVersion"));
        RequireString(actual, "dotnetMinimumSdkFrameworkVersion", RequireString(expected.GetProperty("dotnet"), "minimumSdkFrameworkVersion"));
        RequireString(actual, "dotnetTestRuntimeVersion", RequireString(expected.GetProperty("dotnet"), "testRuntimeVersion"));
        RequireString(actual, "dotnetBaseImage", RequireString(expected.GetProperty("dotnet"), "baseImage"));
        RequireString(actual, "dotnetBaseImageDigest", RequireString(expected.GetProperty("dotnet"), "baseImageDigest"));
        RequireString(actual, "powershellVersionLine", RequireString(expected.GetProperty("powershell"), "versionLine"));
        RequireString(actual, "powershellImageDigest", RequireString(expected.GetProperty("powershell"), "imageDigest"));
        RequireString(
            actual,
            "z3Version",
            RequireString(expected.GetProperty("z3"), "version"));
        RequireInteger64(
            actual,
            "z3LibraryBytes",
            RequireInteger64(expected.GetProperty("z3"), "libraryBytes"));
        RequireString(
            actual,
            "z3LibrarySha256",
            RequireString(expected.GetProperty("z3"), "librarySha256"));
        RequireString(
            actual,
            "verifierPackageId",
            RequireString(
                expected.GetProperty("support"),
                "verifierPackageId"));

        return new ContainerContractInfo(
            actual.GetProperty("contractVersion").GetInt32(),
            actual.GetProperty("platform").GetString()!,
            actual.GetProperty("dotnetSdkVersion").GetString()!,
            actual.GetProperty("z3Version").GetString()!,
            actual.GetProperty("z3LibraryBytes").GetInt64(),
            actual.GetProperty("z3LibrarySha256").GetString()!,
            actual.GetProperty("verifierPackageId").GetString()!);
    }

    public static string ResolveZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        var nativeRoot = Environment.GetEnvironmentVariable(
            "SHARPPROOF_NATIVE_ROOT");
        if (string.IsNullOrWhiteSpace(nativeRoot))
        {
            nativeRoot = "/opt/sharpproof/native";
        }
        var library = LinuxPathIdentity.RequireLocalPath(Path.Combine(
            nativeRoot,
            "z3",
            contract.Z3Version,
            "linux-x64",
            "libz3.so"));
        var information = new FileInfo(library);
        if (!information.Exists || information.Length != contract.Z3LibraryBytes)
        {
            throw new InvalidDataException(
                "The SharpProof Z3 native payload is missing or has the wrong size.");
        }
        using var stream = new FileStream(
            library,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(
                hash,
                contract.Z3LibrarySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The SharpProof Z3 native payload hash does not match the container contract.");
        }
        return library;
    }

    private static JsonDocument ReadEmbeddedToolchain()
    {
        var stream = typeof(ContainerContract).Assembly.GetManifestResourceStream(
            EmbeddedToolchainName) ?? throw new InvalidDataException(
            "The SharpProof host assembly has no embedded toolchain contract.");
        using (stream)
        {
            return JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
        }
    }

    private static JsonDocument ReadBoundedJson(string path)
    {
        // Reject empty special files before open so a FIFO cannot block while
        // waiting for a writer. The opened stream is bounded again below
        // because this path metadata is only a preflight observation.
        var information = new FileInfo(path);
        if (information.Length <= 0 ||
            information.Length > MaximumContractBytes)
        {
            throw new InvalidDataException(
                "The SharpProof container contract has an invalid size.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.SequentialScan);
        return ReadBoundedJson(stream);
    }

    private static JsonDocument ReadBoundedJson(Stream stream)
    {
        var bytes = new byte[MaximumContractBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = stream.Read(bytes, length, bytes.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length <= 0 || length > MaximumContractBytes)
        {
            throw new InvalidDataException(
                "The SharpProof container contract has an invalid size.");
        }

        try
        {
            return JsonDocument.Parse(
                bytes.AsMemory(0, length),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The SharpProof container contract JSON is invalid.",
                exception);
        }
    }

    private static int RequireInteger(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return value;
    }

    private static void RequireInteger(
        JsonElement element,
        string name,
        int expected)
    {
        if (RequireInteger(element, name) != expected)
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' does not match the toolchain.");
        }
    }

    private static long RequireInteger64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var value))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return value;
    }

    private static void RequireInteger64(
        JsonElement element,
        string name,
        long expected)
    {
        if (RequireInteger64(element, name) != expected)
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' does not match the toolchain.");
        }
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return property.GetString()!;
    }

    private static void RequireString(
        JsonElement element,
        string name,
        string expected)
    {
        if (!string.Equals(
                RequireString(element, name),
                expected,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' does not match the toolchain.");
        }
    }
}
