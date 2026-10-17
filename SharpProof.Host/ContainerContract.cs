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
    string VerifierPackageId)
{
    public string Platform { get; init; } = Platform ?? throw new ArgumentNullException(nameof(Platform));
    public string DotNetSdkVersion { get; init; } = DotNetSdkVersion ?? throw new ArgumentNullException(nameof(DotNetSdkVersion));
    public string Z3Version { get; init; } = Z3Version ?? throw new ArgumentNullException(nameof(Z3Version));
    public string Z3LibrarySha256 { get; init; } = Z3LibrarySha256 ?? throw new ArgumentNullException(nameof(Z3LibrarySha256));
    public string VerifierPackageId { get; init; } = VerifierPackageId ?? throw new ArgumentNullException(nameof(VerifierPackageId));
}

public static class ContainerContract
{
    private const int MaximumContractBytes = 16 * 1024;
    private const string DefaultContractPath =
        "/etc/sharpproof/container-contract.json";
    private const string EmbeddedToolchainName =
        "SharpProof.Host.toolchain.json";

    public static ContainerContractInfo ValidateRequired()
    {
        return SelectHost(OperatingSystem.IsMacOS(), MacOsHostContract.Current.ValidateRequired,
            () => ValidateHostRequired(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(),
                RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture))();
    }

    internal static Func<T> SelectHost<T>(bool isMacOs, Func<T> macOs, Func<T> container)
    {
        System.ArgumentNullException.ThrowIfNull(container);
        System.ArgumentNullException.ThrowIfNull(macOs);
        return isMacOs ? macOs : container;
    }

    internal static ContainerContractInfo ValidateHostRequired(
        bool isWindows, bool isLinux, Architecture osArchitecture, Architecture processArchitecture)
    {
        if (isWindows)
        {
            if (osArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("SharpProof native Windows verification requires an x64 OS.");
            }
            return ValidateWindowsRequired(processArchitecture);
        }
        if (isLinux && IsNativeLinuxHost())
        {
            return new LinuxHostContract(isLinux, osArchitecture, processArchitecture).ValidateRequired();
        }
        RequireCanonicalPlatform(isLinux, processArchitecture);
        return ValidateCanonicalContainerRequired();
    }

    // Repository/container callers retain an explicit strict boundary, independent
    // of consumer native-host selection. A marker never substitutes for the JSON pins.
    internal static ContainerContractInfo ValidateCanonicalContainerRequired()
    {
        RequireCanonicalPlatform(OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture);
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

    internal static ContainerContractInfo ValidateWindowsRequired(Architecture architecture)
    {
        if (architecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("SharpProof native Windows verification requires an x64 process.");
        }
        using var document = ReadEmbeddedToolchain();
        var toolchain = document.RootElement;
        var z3 = toolchain.GetProperty("z3");
        var windows = z3.GetProperty("windows");
        return new ContainerContractInfo(
            RequireInteger(toolchain, "containerContractVersion"),
            "win-x64",
            RequireString(toolchain.GetProperty("dotnet"), "minimumSdkVersion"),
            RequireString(z3, "version"),
            RequireInteger64(windows, "libraryBytes"),
            RequireSha256(windows, "librarySha256"),
            RequireString(toolchain.GetProperty("support"), "verifierPackageId"));
    }

    private static bool IsNativeLinuxHost()
    {
        return OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER") != "1";
    }

    internal static void RequireCanonicalPlatform(bool isLinux, Architecture architecture)
    {
        if (!isLinux || architecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "SharpProof verification requires the canonical Linux amd64 container.");
        }
    }

    public static string ResolveZ3LibraryRequired()
    {
        if (IsNativeLinuxHost())
        {
            return LinuxHostContract.Current.ResolveZ3LibraryRequired();
        }
        return SelectHost(OperatingSystem.IsMacOS(),
            MacOsHostContract.Current.ResolveZ3LibraryRequired, ResolveContainerZ3LibraryRequired)();
    }

    private static string ResolveContainerZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        return stream.Name;
    }

    internal static string GetZ3LibrarySha256Required()
    {
        if (IsNativeLinuxHost())
        {
            return LinuxHostContract.Current.GetZ3LibrarySha256Required();
        }
        return SelectHost(OperatingSystem.IsMacOS(),
            MacOsHostContract.Current.GetZ3LibrarySha256Required, GetContainerZ3LibrarySha256Required)();
    }

    private static string GetContainerZ3LibrarySha256Required()
    {
        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        return contract.Z3LibrarySha256;
    }

    internal static IntPtr LoadZ3LibraryRequired()
    {
        if (IsNativeLinuxHost())
        {
            return LinuxHostContract.Current.LoadZ3LibraryRequired();
        }
        return SelectHost(OperatingSystem.IsMacOS(),
            MacOsHostContract.Current.LoadZ3LibraryRequired, LoadContainerZ3LibraryRequired)();
    }

    private static IntPtr LoadContainerZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        using var stream = OpenZ3LibraryRequired(contract);
        return LoadVerifiedZ3Library(stream, OperatingSystem.IsWindows());
    }

    internal static IntPtr LoadVerifiedZ3Library(FileStream stream, bool isWindows)
    {
        System.ArgumentNullException.ThrowIfNull(stream);
        if (isWindows)
        {
            // FileShare.Read keeps the verified DLL immutable through loading.
            // Search dependencies only beside that DLL and in System32.
            return NativeLibrary.Load(stream.Name, typeof(ContainerContract).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
        }
        RequireLinuxZ3Loader(OperatingSystem.IsLinux());
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

    internal static void RequireLinuxZ3Loader(bool isLinux)
    {
        if (!isLinux)
        {
            throw new PlatformNotSupportedException(
                "The pinned Z3 payload loader requires Linux.");
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
        var library = ResolveZ3LibraryPath(contract, OperatingSystem.IsWindows(),
            typeof(ContainerContract).Assembly.Location, nativeRoot);
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

    internal static string ResolveZ3LibraryPath(
        ContainerContractInfo contract, bool isWindows, string assemblyLocation, string nativeRoot)
    {
        System.ArgumentNullException.ThrowIfNull(nativeRoot);
        System.ArgumentNullException.ThrowIfNull(assemblyLocation);
        System.ArgumentNullException.ThrowIfNull(contract);
        return isWindows
            ? PublicationLease.CanonicalMember(Path.Combine(Path.GetDirectoryName(assemblyLocation)!,
                "..", "native", "win-x64", "libz3.dll"))
            : Path.GetFullPath(Path.Combine(nativeRoot, "z3", contract.Z3Version, "linux-x64", "libz3.so"));
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
        System.ArgumentNullException.ThrowIfNull(path);
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
        System.ArgumentNullException.ThrowIfNull(stream);
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
        RepositoryParameterGuard.RequireInitialized(element.ValueKind == System.Text.Json.JsonValueKind.Undefined, nameof(element), "Value must be initialized.");

        System.ArgumentNullException.ThrowIfNull(accessor);
        System.ArgumentNullException.ThrowIfNull(name);
        var actual = accessor(element, name);
        if (!(comparer ?? EqualityComparer<T>.Default).Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"The SharpProof container contract property '{name}' does not match the toolchain.");
        }
        return actual;
    }
}
