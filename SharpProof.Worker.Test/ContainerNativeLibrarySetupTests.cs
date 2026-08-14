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
    }
}
