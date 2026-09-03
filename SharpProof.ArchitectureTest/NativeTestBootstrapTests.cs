using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class NativeTestBootstrapTests
{
    [Test]
    public async Task SmtAndWorkerTestsInstallTheExactRequiredNativeBootstrap()
    {
        var root = TestRepository.FindRoot();
        foreach (var project in new[] { "SharpProof.Smt.Test", "SharpProof.Worker.Test" })
        {
            var path = Path.Combine(root, project, "ContainerNativeLibrarySetup.cs");
            var source = await File.ReadAllTextAsync(path);
            Assert.That(
                TestTextHelpers.CountOrdinal(
                    source,
                    "ContainerNativeLibrary.InstallZ3ResolverRequired("),
                Is.EqualTo(1),
                project);
            Assert.That(
                source,
                Does.Contain("typeof(Microsoft.Z3.Context).Assembly"),
                project);
            Assert.That(source, Does.Contain("[SetUpFixture]"), project);
            Assert.That(source, Does.Contain("[OneTimeSetUp]"), project);

            var projectFile = await File.ReadAllTextAsync(Path.Combine(
                root,
                project,
                project + ".csproj"));
            Assert.That(
                projectFile,
                Does.Contain("SharpProof.Host.csproj"),
                project);
        }
    }

}
