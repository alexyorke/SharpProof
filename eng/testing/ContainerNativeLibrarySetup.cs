using NUnit.Framework;
using SharpProof.Host;

// Place the fixture in the common parent namespace so NUnit applies it to
// both SharpProof.Smt.Test and SharpProof.Worker.Test assemblies.
namespace SharpProof;

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
