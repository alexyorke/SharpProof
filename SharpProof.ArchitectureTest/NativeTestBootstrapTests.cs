using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class NativeTestBootstrapTests
{
    [Test]
    public async Task SmtAndWorkerTestsInstallTheExactRequiredNativeBootstrap()
    {
        var root = TestRepository.FindRoot();
        var setupPath = Path.Combine(
            root,
            "eng",
            "testing",
            "ContainerNativeLibrarySetup.cs");
        var source = await File.ReadAllTextAsync(setupPath);
        Assert.That(
            TestTextHelpers.CountOrdinal(
                source,
                "ContainerNativeLibrary.InstallZ3ResolverRequired("),
            Is.EqualTo(1));
        Assert.That(
            source,
            Does.Contain("typeof(Microsoft.Z3.Context).Assembly"));
        Assert.That(source, Does.Contain("[SetUpFixture]"));
        Assert.That(source, Does.Contain("[OneTimeSetUp]"));

        foreach (var project in new[] { "SharpProof.Smt.Test", "SharpProof.Worker.Test" })
        {
            var projectFile = await File.ReadAllTextAsync(Path.Combine(
                root,
                project,
                project + ".csproj"));
            Assert.That(
                projectFile,
                Does.Contain("SharpProof.Host.csproj"),
                project);
            Assert.That(
                projectFile,
                Does.Contain("..\\eng\\testing\\ContainerNativeLibrarySetup.cs"),
                project);
        }
    }

}
