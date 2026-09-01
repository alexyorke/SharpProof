using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class VerifierPublicationTransactionTests
{
    [Test]
    public void PublicationStagesBeforeCommitAndUsesResultAsTheLastMember()
    {
        var source = ReadLauncherSource();
        var atomicFile = ReadAtomicFileSource();

        Assert.That(
            HasTransactionAuthority(source, atomicFile),
            Is.True,
            "The launcher must stage every member, sync directories, and commit the result last.");
    }

    [Test]
    public void PublicationMutationRestoringManifestFirstWritesIsRejected()
    {
        var source = ReadLauncherSource();
        var mutated = source.Replace(
            "StagePublication(members);",
            "AtomicFile.WriteBytesAsync(arguments.PublishCompilerManifestPath!, artifactBytes).GetAwaiter().GetResult();",
            StringComparison.Ordinal);

        Assert.That(
            HasTransactionAuthority(mutated, ReadAtomicFileSource()),
            Is.False);
    }

    [Test]
    public void PublicationMutationMovingResultBeforeCommitCompletionIsRejected()
    {
        var source = ReadLauncherSource();
        var mutated = source.Replace(
            "PublishMember(members[^1]);",
            "PublishMember(members[0]);",
            StringComparison.Ordinal);

        Assert.That(
            HasTransactionAuthority(mutated, ReadAtomicFileSource()),
            Is.False);
    }

    private static bool HasTransactionAuthority(
        string source,
        string atomicFile)
    {
        var publishStart = source.IndexOf(
            "private static void PublishOutputs(",
            StringComparison.Ordinal);
        var publishEnd = source.IndexOf(
            "private static Task WriteLauncherFailureAsync(",
            publishStart,
            StringComparison.Ordinal);
        if (publishStart < 0 || publishEnd <= publishStart)
        {
            return false;
        }

        var body = source[publishStart..publishEnd];
        var stage = body.IndexOf(
            "StagePublication(members);",
            StringComparison.Ordinal);
        var commit = body.IndexOf(
            "PublishMember(members[^1]);",
            StringComparison.Ordinal);
        return stage >= 0 && commit > stage &&
            body.Contains("TryRollbackPublication(members, previous);", StringComparison.Ordinal) &&
            body.Contains("LinuxPathIdentity.SyncDirectory", StringComparison.Ordinal) &&
            atomicFile.Contains("stream.Flush(true);", StringComparison.Ordinal);
    }

    private static string ReadLauncherSource()
    {
        var root = TestRepository.FindRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Worker.Launcher",
            "Program.cs"));
    }

    private static string ReadAtomicFileSource()
    {
        var root = TestRepository.FindRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Ir",
            "AtomicFile.cs"));
    }

}
