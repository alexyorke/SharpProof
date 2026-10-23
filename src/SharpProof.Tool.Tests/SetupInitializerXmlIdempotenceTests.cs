using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
internal sealed class SetupInitializerXmlIdempotenceTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task TemplateXmlDeclarationPreservesPreviewAndApplyIdempotence(bool crLf, bool bom)
    {
        using var fixture = new TemplateFixture();
        await fixture.CreateAsync(crLf, bom, TestContext.CurrentContext.CancellationToken);
        var original = await fixture.SnapshotAsync(TestContext.CurrentContext.CancellationToken);

        var preview = await fixture.InitializeAsync(false, TestContext.CurrentContext.CancellationToken);
        Assert.That(preview.Files, Has.Count.EqualTo(3));
        foreach (var file in preview.Files)
        { Assert.That(file.Written, Is.False); }
        Assert.That(await fixture.SnapshotAsync(TestContext.CurrentContext.CancellationToken), Is.EqualTo(original));

        _ = await fixture.InitializeAsync(true, TestContext.CurrentContext.CancellationToken);
        var applied = await fixture.SnapshotAsync(TestContext.CurrentContext.CancellationToken);
        var timestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var files = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        { File.SetLastWriteTimeUtc(file, timestamp); }

        var repeatedPreview = await fixture.InitializeAsync(false, TestContext.CurrentContext.CancellationToken);
        var repeatedApply = await fixture.InitializeAsync(true, TestContext.CurrentContext.CancellationToken);
        foreach (var file in repeatedPreview.Files.Concat(repeatedApply.Files))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(file.Changed, Is.False, file.Path);
                Assert.That(file.Written, Is.False, file.Path);
                Assert.That(file.Path, Does.StartWith(fixture.Root + Path.DirectorySeparatorChar));
            }
        }

        Assert.That(await fixture.SnapshotAsync(TestContext.CurrentContext.CancellationToken), Is.EqualTo(applied));
        foreach (var file in files)
        { Assert.That(File.GetLastWriteTimeUtc(file), Is.EqualTo(timestamp), file); }

        var configPath = Path.Combine(fixture.Root, "NuGet.config");
        var config = await File.ReadAllTextAsync(configPath, TestContext.CurrentContext.CancellationToken);
        var newline = crLf ? "\r\n" : "\n";
        Assert.That(config, Does.StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>" + newline +
            "<!-- unrelated feed configuration -->" + newline + newline + "<configuration>"));
        var bytes = await File.ReadAllBytesAsync(configPath, TestContext.CurrentContext.CancellationToken);
        Assert.That(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), Is.EqualTo(bom));
        Assert.That(config.Replace("\r\n", "\n", StringComparison.Ordinal),
            Does.Contain("<packageSource key=\"nuget.org\"><package pattern=\"*\" /></packageSource>"));
        Assert.That(Directory.GetFiles(fixture.Root, ".sharpproof-init-*", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.Exists(Path.Combine(fixture.Root, "obj")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(fixture.Root, "bin")), Is.False);
    }

    private sealed class TemplateFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-init-xml-" + Guid.NewGuid().ToString("N"));
        private string ProjectPath => Path.Combine(Root, "Consumer.csproj");

        internal TemplateFixture()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, ".config"));
        }

        internal async Task CreateAsync(bool crLf, bool bom, CancellationToken cancellationToken)
        {
            var project = await File.ReadAllTextAsync(TemplatePath("portable-editor/SharpProofStarter.csproj.template"), cancellationToken);
            project = project.Replace("__SHARPPROOF_VERSION__", SetupInitializer.PackageVersion, StringComparison.Ordinal);
            await File.WriteAllTextAsync(ProjectPath, project, cancellationToken);
            var manifest = await File.ReadAllTextAsync(TemplatePath(".config/dotnet-tools.json"), cancellationToken);
            manifest = manifest.Replace("__SHARPPROOF_VERSION__", SetupInitializer.PackageVersion, StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(Root, ".config", "dotnet-tools.json"), manifest, cancellationToken);
            var config = await File.ReadAllTextAsync(TemplatePath("NuGet.config"), cancellationToken);
            config = config.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("<configuration>", "<!-- unrelated feed configuration -->\n\n<configuration>", StringComparison.Ordinal);
            if (crLf)
            { config = config.Replace("\n", "\r\n", StringComparison.Ordinal); }
            await File.WriteAllTextAsync(Path.Combine(Root, "NuGet.config"), config, new UTF8Encoding(bom), cancellationToken);
        }

        internal Task<SetupInitializationResult> InitializeAsync(bool apply, CancellationToken cancellationToken)
        {
            return SetupInitializer.InitializeAsync(ProjectPath, "portable", apply, null, cancellationToken);
        }

        internal async Task<Dictionary<string, string>> SnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories))
            {
                snapshot.Add(Path.GetRelativePath(Root, path), Directory.Exists(path) ? "directory" :
                    Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken))));
            }
            return snapshot;
        }

        private static string TemplatePath(string relativePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(relativePath);
            for (var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); current != null; current = current.Parent)
            {
                var path = Path.Combine(current.FullName, "src", "SharpProof.Templates", "content", relativePath);
                if (File.Exists(path))
                { return path; }
            }
            throw new AssertionException("The actual SharpProof template content is required for initializer acceptance regressions.");
        }

        public void Dispose()
        { Directory.Delete(Root, recursive: true); }
    }
}
