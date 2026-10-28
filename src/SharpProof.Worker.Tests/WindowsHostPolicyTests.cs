using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class WindowsHostPolicyTests
{
    [TestCase(Architecture.X64, "win-x64")]
    [TestCase(Architecture.Arm64, "win-arm64")]
    public void NativeHostUsesRuntimeArchitectureAndEmbeddedContract(Architecture architecture, string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        Assert.That(ContainerContract.ValidateHostRequired(true, false, architecture, architecture).Platform, Is.EqualTo(rid));
        Assert.Throws<PlatformNotSupportedException>((Action)(() =>
            ContainerContract.ValidateHostRequired(false, false, architecture, architecture)));
    }

    [TestCase(Architecture.Arm64, Architecture.X64)]
    [TestCase(Architecture.X64, Architecture.Arm64)]
    [TestCase(Architecture.X86, Architecture.X86)]
    public void WindowsRejectsTranslatedOrUnsupportedHosts(Architecture host, Architecture process)
    {
        Assert.Throws<PlatformNotSupportedException>((Action)(() =>
            ContainerContract.ValidateHostRequired(true, false, host, process)));
    }

    [TestCase(Architecture.X64, "win-x64")]
    [TestCase(Architecture.Arm64, "win-arm64")]
    public void WindowsDllPathBelongsToPackageAndIgnoresNativeRoot(Architecture architecture, string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);
        using var temporary = new TempDirectory("SharpProof.WindowsPath.");
        var tools = Path.Combine(temporary.FullName, "tools");
        var assembly = Path.Combine(tools, "net10", "SharpProof.Host.dll");
        var contract = ContainerContract.ValidateWindowsRequired(architecture);
        Assert.That(ContainerContract.ResolveZ3LibraryPath(contract, true, assembly, "/untrusted/native"),
            Is.EqualTo(Path.Combine(tools, "native", rid, "libz3.dll")));
        Assert.Throws<ArgumentException>((Action)(() => PublicationLease.CanonicalMember(temporary.FullName, true)));
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.CanonicalMember(Path.Combine(temporary.FullName, "ambiguous. ", "result.json"), true)));
    }

    [TestCase("../outside")]
    [TestCase("win-x64/../../outside")]
    [TestCase("/absolute/outside")]
    [TestCase("linux-arm64")]
    [TestCase("WIN-ARM64")]
    public void WindowsNativePathRejectsUnvalidatedRuntimeIdentifiers(string platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        using var temporary = new TempDirectory("SharpProof.WindowsPath.Invalid.");
        var assembly = Path.Combine(temporary.FullName, "tools", "net10", "SharpProof.Host.dll");
        var contract = ContainerContract.ValidateWindowsRequired(Architecture.X64) with { Platform = platform };
        Assert.Throws<InvalidDataException>((Action)(() =>
            ContainerContract.ResolveZ3LibraryPath(contract, true, assembly, "/untrusted/native")));
    }
    [Test]
    public void VerifiedAbsolutePathLoaderLoadsTheExplicitLibrary()
    {
        // Unix ignores Windows DLL search flags; the native Windows consumer
        // exercises the same API with the pinned DLL and real dependencies.
        using var stream = File.OpenRead(ContainerContract.ResolveZ3LibraryRequired());
        var handle = ContainerContract.LoadVerifiedZ3Library(stream, true);
        try
        {
            Assert.That(NativeLibrary.GetExport(handle, "Z3_get_version"), Is.Not.EqualTo(IntPtr.Zero));
        }
        finally { NativeLibrary.Free(handle); }
    }

    [TestCase(@"\\server\share\result.json")]
    [TestCase("C")]
    [TestCase(@"C:/result.json")]
    [TestCase(@"C:\folder\result.json:stream")]
    [TestCase(@"C:\folder\ALIAS~1.json")]
    [TestCase(@"C:\folder\trailing.")]
    [TestCase("C:\\folder\\trailing ")]
    [TestCase(@"C:\folder\CON.json")]
    [TestCase(@"C:\folder\PRN.json")]
    [TestCase(@"C:\folder\AUX.json")]
    [TestCase(@"C:\folder\NUL.json")]
    [TestCase(@"C:\folder\COM1.json")]
    [TestCase(@"C:\folder\LPT9.json")]
    [TestCase("C:\\folder\\COM\u00b9.json")]
    [TestCase("C:\\folder\\COM\u00b2.json")]
    [TestCase("C:\\folder\\COM\u00b3.json")]
    [TestCase("C:\\folder\\LPT\u00b9.json")]
    [TestCase("C:\\folder\\LPT\u00b2.json")]
    [TestCase("C:\\folder\\LPT\u00b3.json")]
    [TestCase("C:\\COM\u00b9\\result.json")]
    [TestCase("C:\\folder\\lpt\u00b2.txt")]
    public void WindowsPathAliasesAndDevicesAreRejected(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => true, _ => false)));
    }

    [TestCase("/", "/", true)]
    [TestCase("/tmp/result.json", "/", true)]
    [TestCase("/tmp/result.json", "/tmp", true)]
    [TestCase("/tmp/result.json", "/tmp/", true)]
    [TestCase("/tmp-other/result.json", "/tmp", false)]
    public void PublicationContainmentPreservesFilesystemRoots(string path, string directory, bool expected)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);
        Assert.That(PublicationPaths.IsWithin(path, directory), Is.EqualTo(expected));
    }

    [Test]
    public void WindowsPublicationRequiresLocalNtfsAndNoReparsePoints()
    {
        const string path = @"C:\folder with spaces\result.json";
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => false, _ => false)));
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => true, _ => true)));
        var visited = ImmutableArray.CreateBuilder<string>();
        Assert.DoesNotThrow((Action)(() => PublicationLease.ValidateWindowsMember(path,
            root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root == @"C:\"), member =>
            {
                System.ArgumentNullException.ThrowIfNull(member);
                visited.Add(member);
                return false;
            })));
        Assert.That(visited.ToImmutable(), Is.EqualTo(ImmutableArray.Create(@"C:\folder with spaces", path)));
        Assert.That(PublicationLease.IsLocalNtfs("/"), Is.False);
    }

    [Test]
    public void FilesystemAdapterDistinguishesRegularMissingAndLinkedEntries()
    {
        using var temporary = new TempDirectory("SharpProof.WindowsFs.");
        var file = Path.Combine(temporary.FullName, "file");
        File.WriteAllText(file, "test");
        var link = Path.Combine(temporary.FullName, "link");
        File.CreateSymbolicLink(link, file);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PublicationLease.IsReparsePoint(file), Is.False);
            Assert.That(PublicationLease.IsReparsePoint(link), Is.True);
            Assert.That(PublicationLease.IsReparsePoint(temporary.FullName), Is.False);
            Assert.That(PublicationLease.IsReparsePoint(Path.Combine(temporary.FullName, "missing")), Is.False);
            Assert.That(PublicationPaths.Comparer.Equals("path", "PATH"), Is.False);
        }
    }
}
