using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpProof.Host;

public sealed class ContainerContractInfo
{
    internal ContainerContractInfo(
        int contractVersion,
        string platform,
        string dotNetSdkVersion,
        string z3Version,
        long z3LibraryBytes,
        string z3LibrarySha256,
        string verifierPackageId)
    {
        ContractVersion = contractVersion;
        Platform = platform;
        DotNetSdkVersion = dotNetSdkVersion;
        Z3Version = z3Version;
        Z3LibraryBytes = z3LibraryBytes;
        Z3LibrarySha256 = z3LibrarySha256;
        VerifierPackageId = verifierPackageId;
    }

    public int ContractVersion { get; }
    public string Platform { get; }
    public string DotNetSdkVersion { get; }
    public string Z3Version { get; }
    public long Z3LibraryBytes { get; }
    public string Z3LibrarySha256 { get; }
    public string VerifierPackageId { get; }
}

public static class ContainerContract
{
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
        var information = new FileInfo(path);
        if (information.Length <= 0 || information.Length > 16 * 1024)
        {
            throw new InvalidDataException(
                "The SharpProof container contract has an invalid size.");
        }
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
    }

    private static int RequireInteger(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
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
