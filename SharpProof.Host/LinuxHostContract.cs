using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpProof.Host;

internal sealed class LinuxHostContract
{
    private readonly bool isLinux;
    private readonly Architecture hostArchitecture;
    private readonly Architecture processArchitecture;
    private readonly string runtimeIdentifier;
    private readonly string assemblyLocation;
    private readonly Func<Stream?> openToolchain;
    private readonly Func<FileStream, IntPtr> loadVerifiedLibrary;

    internal static LinuxHostContract Current { get; } = new();

    internal LinuxHostContract(bool? isLinux = null, Architecture? hostArchitecture = null,
        Architecture? processArchitecture = null, string? assemblyLocation = null,
        Func<Stream?>? openToolchain = null, string? runtimeIdentifier = null,
        Func<FileStream, IntPtr>? loadVerifiedLibrary = null)
    {
        this.isLinux = isLinux ?? OperatingSystem.IsLinux();
        this.hostArchitecture = hostArchitecture ?? RuntimeInformation.OSArchitecture;
        this.processArchitecture = processArchitecture ?? RuntimeInformation.ProcessArchitecture;
        this.runtimeIdentifier = runtimeIdentifier ?? RuntimeInformation.RuntimeIdentifier;
        this.assemblyLocation = assemblyLocation ?? typeof(LinuxHostContract).Assembly.Location;
        this.openToolchain = openToolchain ?? OpenEmbeddedToolchain;
        this.loadVerifiedLibrary = loadVerifiedLibrary ?? LoadVerifiedLibrary;
    }

    internal ContainerContractInfo ValidateRequired()
    {
        var rid = RequireRuntimeIdentifier();
        using var catalog = ReadToolchain();
        var root = catalog.RootElement;
        var z3 = root.GetProperty("z3");
        var native = rid == "linux-arm64" ? z3.GetProperty("linuxArm64") : z3;
        var pin = ReadPin(native, "libraryBytes", "librarySha256");
        return new ContainerContractInfo(1, rid, "host", z3.GetProperty("version").GetString()!,
            pin.Bytes, pin.Sha256, root.GetProperty("support").GetProperty("verifierPackageId").GetString()!);
    }

    internal string ResolveZ3LibraryRequired()
    {
        using var stream = OpenZ3LibraryRequired();
        return stream.Name;
    }

    internal string GetZ3LibrarySha256Required()
    {
        using var stream = OpenZ3LibraryRequired();
        return ValidateRequired().Z3LibrarySha256;
    }

    internal IntPtr LoadZ3LibraryRequired()
    {
        using var stream = OpenZ3LibraryRequired();
        return loadVerifiedLibrary(stream);
    }

    private FileStream OpenZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        var tools = GetPackageToolsDirectory();
        using var catalog = ReadToolchain();
        var managedPin = ReadPin(catalog.RootElement.GetProperty("z3"), "managedAssemblyBytes", "managedAssemblySha256");
        using var managed = OpenVerifiedPayload(Path.Combine(tools, "net10", "Microsoft.Z3.dll"), managedPin, "managed");
        return OpenVerifiedPayload(Path.Combine(tools, "native", contract.Platform, "libz3.so"),
            (contract.Z3LibraryBytes, contract.Z3LibrarySha256), "native");
    }

    private string RequireRuntimeIdentifier()
    {
        if (!isLinux || hostArchitecture != processArchitecture)
        {
            throw new PlatformNotSupportedException(
                "SharpProof native Linux verification requires matching host and process architecture.");
        }
        var rid = processArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            _ => throw new PlatformNotSupportedException("SharpProof native Linux verification requires ARM64 or x64.")
        };
        if (runtimeIdentifier != rid)
        {
            throw new PlatformNotSupportedException(
                "SharpProof native Linux verification requires the glibc linux-x64 or linux-arm64 runtime.");
        }
        return rid;
    }

    private string GetPackageToolsDirectory()
    {
        if (!Path.IsPathFullyQualified(assemblyLocation))
        {
            throw new InvalidDataException("The SharpProof Linux host assembly has no absolute package path.");
        }
        var directory = Path.GetDirectoryName(PublicationLease.CanonicalMember(assemblyLocation))!;
        if (Path.GetFileName(directory) != "net10" || Path.GetFileName(Path.GetDirectoryName(directory)) != "tools")
        {
            throw new InvalidDataException("The SharpProof Linux host assembly is outside its package runtime closure.");
        }
        return Path.GetDirectoryName(directory)!;
    }

    private static (long Bytes, string Sha256) ReadPin(JsonElement element, string sizeProperty, string digestProperty)
    {
        ArgumentNullException.ThrowIfNull(sizeProperty);
        ArgumentNullException.ThrowIfNull(digestProperty);
        var bytes = element.GetProperty(sizeProperty).GetInt64();
        var digest = element.GetProperty(digestProperty).GetString();
        if (bytes <= 0 || digest == null || digest.Length != 64 ||
            digest.Any(static character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException("The SharpProof Linux payload pin must contain a positive size and lowercase SHA-256 digest.");
        }
        return (bytes, digest);
    }

    private static FileStream OpenVerifiedPayload(string path, (long Bytes, string Sha256) pin, string kind)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(kind);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != pin.Bytes ||
                !string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), pin.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The SharpProof Linux Z3 " + kind + " payload does not match its pinned digest.");
            }
            stream.Position = 0;
            var verified = stream;
            stream = null;
            return verified;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("The SharpProof Linux Z3 " + kind + " payload is missing.", exception);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private JsonDocument ReadToolchain()
    {
        using var stream = openToolchain() ??
            throw new InvalidDataException("The SharpProof host assembly has no embedded toolchain contract.");
        return JsonDocument.Parse(stream);
    }

    private static Stream? OpenEmbeddedToolchain()
    {
        return typeof(LinuxHostContract).Assembly.GetManifestResourceStream("SharpProof.Host.toolchain.json");
    }

    private static IntPtr LoadVerifiedLibrary(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ContainerContract.LoadVerifiedZ3Library(stream, isWindows: false);
    }
}
