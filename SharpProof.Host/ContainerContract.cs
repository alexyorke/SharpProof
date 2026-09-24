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
        RequireInteger(actual, "schemaVersion", 2);
        var contractVersion = RequireInteger(
            actual,
            "contractVersion",
            RequireInteger(expected, "containerContractVersion"));
        var platform = RequireString(
            actual,
            "platform",
            RequireString(expected, "platform"));
        var dotNetSdkVersion = RequireString(
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
        var z3Version = RequireString(
            actual,
            "z3Version",
            RequireString(expected.GetProperty("z3"), "version"));
        var z3LibraryBytes = RequireInteger64(
            actual,
            "z3LibraryBytes",
            RequireInteger64(expected.GetProperty("z3"), "libraryBytes"));
        var z3LibrarySha256 = RequireString(
            actual,
            "z3LibrarySha256",
            RequireSha256(expected.GetProperty("z3"), "librarySha256"));
        var verifierPackageId = RequireString(
            actual,
            "verifierPackageId",
            RequireString(
                expected.GetProperty("support"),
                "verifierPackageId"));

        return new ContainerContractInfo(
            contractVersion,
            platform,
            dotNetSdkVersion,
            z3Version,
            z3LibraryBytes,
            z3LibrarySha256,
            verifierPackageId);
    }

    public static string ResolveZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        return stream.Name;
    }

    internal static string GetZ3LibrarySha256Required()
    {
        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        return contract.Z3LibrarySha256;
    }

    internal static IntPtr LoadZ3LibraryRequired()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "The pinned Z3 payload loader requires Linux.");
        }

        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        var fileHandle = stream.SafeFileHandle;
        var addedReference = false;
        fileHandle.DangerousAddRef(ref addedReference);
        try
        {
            var descriptor = fileHandle.DangerousGetHandle().ToInt64();
            return NativeLibrary.Load($"/proc/self/fd/{descriptor}");
        }
        finally
        {
            if (addedReference)
            {
                fileHandle.DangerousRelease();
            }
        }
    }

    private static FileStream OpenZ3LibraryRequired(
        ContainerContractInfo contract)
    {
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
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                library,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan);
            if (stream.Length != contract.Z3LibraryBytes)
            {
                throw new InvalidDataException(
                    "The SharpProof Z3 native payload is missing or has the wrong size.");
            }
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(
                    digest,
                    contract.Z3LibrarySha256.ToUpperInvariant(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The SharpProof Z3 native payload has the wrong SHA-256 digest.");
            }
            stream.Position = 0;
            var verifiedStream = stream;
            stream = null;
            return verifiedStream;
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException(
                "The SharpProof Z3 native payload is missing or has the wrong size.",
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new InvalidDataException(
                "The SharpProof Z3 native payload is missing or has the wrong size.",
                exception);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static string RequireSha256(
        JsonElement element,
        string property)
    {
        var value = RequireString(element, property);
        if (value.Length != 64 ||
            value.Any(static character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                $"The SharpProof toolchain property '{property}' is not a lowercase SHA-256 digest.");
        }
        return value;
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
        var property = RequireProperty(element, name, JsonValueKind.Number);
        if (!property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return value;
    }

    private static int RequireInteger(
        JsonElement element,
        string name,
        int expected)
    {
        return RequireMatches(element, name, expected, RequireInteger);
    }

    private static long RequireInteger64(JsonElement element, string name)
    {
        var property = RequireProperty(element, name, JsonValueKind.Number);
        if (!property.TryGetInt64(out var value))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return value;
    }

    private static long RequireInteger64(
        JsonElement element,
        string name,
        long expected)
    {
        return RequireMatches(element, name, expected, RequireInteger64);
    }

    private static string RequireString(JsonElement element, string name)
    {
        var property = RequireProperty(element, name, JsonValueKind.String);
        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return value;
    }

    private static string RequireString(
        JsonElement element,
        string name,
        string expected)
    {
        return RequireMatches(
            element,
            name,
            expected,
            RequireString,
            StringComparer.Ordinal);
    }

    private static JsonElement RequireProperty(
        JsonElement element,
        string name,
        JsonValueKind kind)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != kind)
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' is invalid.");
        }
        return property;
    }

    private static T RequireMatches<T>(
        JsonElement element,
        string name,
        T expected,
        Func<JsonElement, string, T> accessor,
        IEqualityComparer<T>? comparer = null)
    {
        var actual = accessor(element, name);
        if (!(comparer ?? EqualityComparer<T>.Default).Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' does not match the toolchain.");
        }
        return actual;
    }
}
