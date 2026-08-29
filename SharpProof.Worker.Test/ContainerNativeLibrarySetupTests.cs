using NUnit.Framework;
using SharpProof.Host;
using System.Runtime.InteropServices;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ContainerNativeLibrarySetupTests
{
    [Test]
    public void VerifiedZ3ResolverInstallationIsIdempotent()
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
    }

    [Test]
    [NonParallelizable]
    public void VerifiedZ3LoadRemainsBoundToTheValidatedFile()
    {
        var canonical = ContainerContract.ResolveZ3LibraryRequired();
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ContainerNativeLibrary.Test",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(
            temporaryDirectory,
            "z3",
            "version",
            "linux-x64",
            "libz3.so");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.Copy(canonical, path);
            using var verified = ContainerContract.OpenZ3LibraryRequired(path);
            var replacement = Path.Combine(
                temporaryDirectory,
                "replacement.so");
            File.Copy(FindUnrelatedNativeLibrary(), replacement);
            // Rename over the open path: unlike truncating the file in place,
            // this models a publication swap while the validated descriptor
            // remains bound to the original inode.
            File.Move(replacement, path, overwrite: true);

            var handle = NativeLibrary.Load(verified.LoadPath);
            try
            {
                Assert.That(
                    NativeLibrary.GetExport(handle, "Z3_mk_config"),
                    Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public void Z3ResolverWaitsForVerifiedHandlePublication()
    {
        var gate = new Z3ResolverGate();
        var resolution = System.Threading.Tasks.Task.Run(() => gate.Resolve());

        Assert.That(
            resolution.Wait(TimeSpan.FromMilliseconds(100)),
            Is.False,
            "A resolver callback must not return a zero handle before publication.");

        var expected = new IntPtr(1234);
        gate.Publish(expected);
        Assert.That(
            resolution.GetAwaiter().GetResult(),
            Is.EqualTo(expected));
    }

    [Test]
    public void Z3ResolverPropagatesInstallationFailure()
    {
        var gate = new Z3ResolverGate();
        var failure = new InvalidOperationException("installation failed");
        gate.Fail(failure);

        var observed = Assert.Throws<InvalidOperationException>(
            (Action)(() => _ = gate.Resolve()));
        Assert.That(observed, Is.SameAs(failure));
    }

    private static string FindUnrelatedNativeLibrary()
    {
        var candidates = new[]
        {
            "/lib/x86_64-linux-gnu/libm.so.6",
            "/usr/lib/x86_64-linux-gnu/libm.so.6"
        };
        return candidates.FirstOrDefault(File.Exists) ??
            throw new InvalidOperationException(
                "The canonical container has no unrelated native library fixture.");
    }
}
