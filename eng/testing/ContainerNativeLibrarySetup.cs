using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Testing;

[SetUpFixture]
public sealed class ContainerNativeLibrarySetup
{
    [OneTimeSetUp]
    public void InstallVerifiedZ3()
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
    }
}
