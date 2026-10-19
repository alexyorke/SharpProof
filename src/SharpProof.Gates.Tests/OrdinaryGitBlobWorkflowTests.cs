using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class OrdinaryGitBlobWorkflowTests
{
    [TestCase("valid")]
    [TestCase("missing")]
    [TestCase("cancelled")]
    public async Task ActualCommittedBlobPreservesBytesAndCleansUpGitAfterCancellation(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        using var repository = new TempDirectory("SharpProof-ordinary-git-blob-");
        await GitAsync(repository.FullName, "init", "--quiet");
        await GitAsync(repository.FullName, "config", "user.name", "SharpProof Test");
        await GitAsync(repository.FullName, "config", "user.email", "test@example.invalid");
        var pipeMaximum = int.Parse(await File.ReadAllTextAsync("/proc/sys/fs/pipe-max-size"), CultureInfo.InvariantCulture);
        var source = "public static class OrdinaryBlob { public static int Value() => 37; }\n/*" +
            new string('a', checked(pipeMaximum + 4096)) + "*/\n";
        var expected = Encoding.UTF8.GetBytes(source);
        await File.WriteAllBytesAsync(Path.Combine(repository.FullName, "ordinary.cs"), expected);
        await GitAsync(repository.FullName, "add", "ordinary.cs");
        await GitAsync(repository.FullName, "commit", "--quiet", "-m", "ordinary source blob");
        var commit = await GitAsync(repository.FullName, "rev-parse", "HEAD");
        Assert.That(GitProcessesIn(repository.FullName), Is.Empty);

        if (scenario == "cancelled")
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            Assert.CatchAsync<OperationCanceledException>((Func<Task>)(async () =>
                await OpenSourceCorpusImporter.ReadGitBlobAsync(repository.FullName, commit, "ordinary.cs", cancellation.Token)));
        }
        else if (scenario == "missing")
        {
            var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await OpenSourceCorpusImporter.ReadGitBlobAsync(repository.FullName, commit, "missing.cs", TestContext.CurrentContext.CancellationToken)));
            Assert.That(error!.Message, Does.StartWith("git cat-file blob " + commit + ":missing.cs failed: "));
        }
        else
        {
            var actual = await OpenSourceCorpusImporter.ReadGitBlobAsync(repository.FullName, commit, "ordinary.cs", TestContext.CurrentContext.CancellationToken);
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            Assert.That(Convert.ToHexString(SHA256.HashData(actual.AsSpan())), Is.EqualTo(Convert.ToHexString(SHA256.HashData(expected))));
        }

        Assert.That(GitProcessesIn(repository.FullName), Is.Empty);
        Assert.That(await GitAsync(repository.FullName, "status", "--porcelain"), Is.Empty);
    }

    private static Task<string> GitAsync(string path, params string[] arguments)
    {
        System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments));
        System.ArgumentNullException.ThrowIfNull(path);
        return OpenSourceCorpusImporter.ReadGitAsync(path, arguments, TestContext.CurrentContext.CancellationToken);
    }

    private static int[] GitProcessesIn(string directory)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        var ids = new List<int>();
        foreach (var process in Process.GetProcessesByName("git"))
        {
            using (process)
            {
                try
                {
                    var target = new DirectoryInfo("/proc/" + process.Id.ToString(CultureInfo.InvariantCulture) + "/cwd")
                        .ResolveLinkTarget(returnFinalTarget: true);
                    if (target?.FullName == directory)
                    {
                        ids.Add(process.Id);
                    }
                }
                catch (IOException)
                {
                    // Unrelated Git processes can exit while the inventory is read.
                }
            }
        }
        return [.. ids];
    }
}
