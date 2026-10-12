using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.Host;

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
        Assert.Throws<InvalidOperationException>((Action)(() =>
            ContainerNativeLibrary.InstallZ3ResolverRequired(
                typeof(ContainerContract).Assembly)));
    }

    [Test]
    [Platform("Linux")]
    public void ExistingResolverCannotBeReplacedAndLoadedHandleIsReleased()
    {
        var context = new AssemblyLoadContext("z3-resolver-collision", isCollectible: true);
        try
        {
            var host = context.LoadFromAssemblyPath(typeof(ContainerNativeLibrary).Assembly.Location);
            var z3 = context.LoadFromAssemblyPath(typeof(Microsoft.Z3.Context).Assembly.Location);
            NativeLibrary.SetDllImportResolver(z3, (_, _, _) => IntPtr.Zero);
            var nativeLibrary = host.GetType("SharpProof.Host.ContainerNativeLibrary")!;
            var install = nativeLibrary.GetMethod("InstallZ3ResolverRequired")!;

            var failure = Assert.Throws<TargetInvocationException>((Action)(() =>
                install.Invoke(null, [z3])));
            Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>());
            using (Assert.EnterMultipleScope())
            {
                Assert.That(nativeLibrary.GetField("s_z3Handle", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null), Is.EqualTo(IntPtr.Zero));
                Assert.That(nativeLibrary.GetField("s_z3Assembly", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null), Is.Null);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    public void ResolverRejectsAnAmbientNativeLibraryName()
    {
        var resolve = typeof(ContainerNativeLibrary).GetMethod(
            "ResolveZ3Import",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(resolve, Is.Not.Null);

        var failure = Assert.Throws<TargetInvocationException>((Action)(() =>
            resolve!.Invoke(null, ["libc", typeof(Microsoft.Z3.Context).Assembly, null])));
        Assert.That(failure!.InnerException, Is.TypeOf<DllNotFoundException>());
    }
}
