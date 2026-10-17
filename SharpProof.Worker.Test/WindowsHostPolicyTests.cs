using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class WindowsHostPolicyTests
{
    [Test]
    public void NativeHostUsesRuntimeArchitectureAndEmbeddedContract()
    {
        Assert.That(ContainerContract.ValidateHostRequired(true, false, Architecture.X64, Architecture.X64).Platform,
            Is.EqualTo("win-x64"));
        Assert.Throws<PlatformNotSupportedException>((Action)(() =>
            ContainerContract.ValidateHostRequired(true, false, Architecture.Arm64, Architecture.X64)));
        Assert.Throws<PlatformNotSupportedException>((Action)(() =>
            ContainerContract.ValidateHostRequired(false, false, Architecture.X64, Architecture.X64)));
    }

    [Test]
    public void WindowsDllPathBelongsToPackageAndIgnoresNativeRoot()
    {
        using var temporary = new TempDirectory("SharpProof.WindowsPath.");
        var tools = Path.Combine(temporary.FullName, "tools");
        var assembly = Path.Combine(tools, "net10", "SharpProof.Host.dll");
        var contract = ContainerContract.ValidateWindowsRequired(Architecture.X64);
        Assert.That(ContainerContract.ResolveZ3LibraryPath(contract, true, assembly, "/untrusted/native"),
            Is.EqualTo(Path.Combine(tools, "native", "win-x64", "libz3.dll")));
        Assert.Throws<ArgumentException>((Action)(() => PublicationLease.CanonicalMember(temporary.FullName, true)));
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.CanonicalMember(Path.Combine(temporary.FullName, "ambiguous. ", "result.json"), true)));
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
    public void WindowsPathAliasesAndDevicesAreRejected(string path)
    {
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => true, _ => false)));
    }

    [Test]
    public void WindowsPublicationRequiresLocalNtfsAndNoReparsePoints()
    {
        const string path = @"C:\folder with spaces\result.json";
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => false, _ => false)));
        Assert.Throws<ArgumentException>((Action)(() =>
            PublicationLease.ValidateWindowsMember(path, _ => true, _ => true)));
        var visited = new List<string>();
        Assert.DoesNotThrow((Action)(() => PublicationLease.ValidateWindowsMember(path,
            root => root == @"C:\", member => { visited.Add(member); return false; })));
        Assert.That(visited, Is.EqualTo(new[] { @"C:\folder with spaces", path }));
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
