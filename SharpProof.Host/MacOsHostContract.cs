using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpProof.Host;

internal sealed class MacOsHostContract
{
    private const string EmbeddedToolchainName = "SharpProof.Host.toolchain.json";
    private readonly bool isMacOs;
    private readonly Architecture hostArchitecture;
    private readonly Architecture processArchitecture;
    private readonly string assemblyLocation;
    private readonly Func<Stream?> openToolchain;

    internal static MacOsHostContract Current { get; } = new();

    internal MacOsHostContract(
        bool? isMacOs = null,
        Architecture? hostArchitecture = null,
        Architecture? processArchitecture = null,
        string? assemblyLocation = null,
        Func<Stream?>? openToolchain = null)
    {
        this.isMacOs = isMacOs ?? OperatingSystem.IsMacOS();
        this.hostArchitecture = hostArchitecture ?? RuntimeInformation.OSArchitecture;
        this.processArchitecture = processArchitecture ?? RuntimeInformation.ProcessArchitecture;
        this.assemblyLocation = assemblyLocation ?? typeof(MacOsHostContract).Assembly.Location;
        this.openToolchain = openToolchain ?? OpenEmbeddedToolchain;
    }

    internal ContainerContractInfo ValidateRequired()
    {
        var rid = RequireRuntimeIdentifier();
        using var catalog = ReadToolchain();
        var root = catalog.RootElement;
        var z3 = root.GetProperty("z3");
        var mac = root.GetProperty("macos").GetProperty(rid == "osx-arm64" ? "arm64" : "x64");
        return new ContainerContractInfo(
            1,
            rid,
            "host",
            z3.GetProperty("version").GetString()!,
            mac.GetProperty("libraryBytes").GetInt64(),
            mac.GetProperty("librarySha256").GetString()!,
            root.GetProperty("support").GetProperty("verifierPackageId").GetString()!);
    }

    internal string ResolveZ3LibraryRequired()
    {
        var contract = ValidateRequired();
        var tools = GetPackageToolsDirectory();
        var library = Path.GetFullPath(Path.Combine(tools, "native", contract.Platform, "libz3.dylib"));
        ValidatePayload(library, contract.Z3LibraryBytes, contract.Z3LibrarySha256, "native");
        using var catalog = ReadToolchain();
        var managed = catalog.RootElement.GetProperty("z3");
        ValidatePayload(
            Path.Combine(tools, "net10", "Microsoft.Z3.dll"),
            managed.GetProperty("managedAssemblyBytes").GetInt64(),
            managed.GetProperty("managedAssemblySha256").GetString()!,
            "managed");
        return library;
    }

    internal string GetZ3LibrarySha256Required()
    {
        _ = ResolveZ3LibraryRequired();
        return ValidateRequired().Z3LibrarySha256;
    }

    internal IntPtr LoadZ3LibraryRequired()
    {
        var library = ResolveZ3LibraryRequired();
        return NativeLibrary.Load(library);
    }

    private string RequireRuntimeIdentifier()
    {
        if (!isMacOs || hostArchitecture != processArchitecture)
        {
            throw new PlatformNotSupportedException(
                "SharpProof native macOS verification requires matching host and process architecture.");
        }
        return processArchitecture switch
        {
            Architecture.Arm64 => "osx-arm64",
            Architecture.X64 => "osx-x64",
            _ => throw new PlatformNotSupportedException(
                "SharpProof native macOS verification requires ARM64 or x64.")
        };
    }

    private string GetPackageToolsDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ??
            throw new InvalidDataException("The SharpProof host assembly has no package path.");
        if (Path.GetFileName(assemblyDirectory) != "net10" ||
            Path.GetFileName(Path.GetDirectoryName(assemblyDirectory)) != "tools")
        {
            throw new InvalidDataException("The SharpProof macOS host assembly is outside its package runtime closure.");
        }
        return Path.GetDirectoryName(assemblyDirectory)!;
    }

    private static void ValidatePayload(string path, long bytes, string sha256, string kind)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != bytes ||
                !string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The SharpProof macOS Z3 " + kind + " payload does not match its pinned digest.");
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("The SharpProof macOS Z3 " + kind + " payload is missing.", exception);
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
        return typeof(MacOsHostContract).Assembly.GetManifestResourceStream(EmbeddedToolchainName);
    }
}
