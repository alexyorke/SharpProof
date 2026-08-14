using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class NativeTestBootstrapTests
{
    [Test]
    public async Task SmtAndWorkerTestsInstallTheExactRequiredNativeBootstrap()
    {
        var root = FindRepositoryRoot();
        foreach (var project in new[] { "SharpProof.Smt.Test", "SharpProof.Worker.Test" })
        {
            var path = Path.Combine(root, project, "ContainerNativeLibrarySetup.cs");
            var source = await File.ReadAllTextAsync(path);
            Assert.That(
                CountOrdinal(
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

    private static int CountOrdinal(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(
                   needle,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
