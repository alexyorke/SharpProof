using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849", Justification = "Tests exercise synchronous staging allocation.")]
public sealed class IrStagingAllocationTests
{
    [Test]
    public void AllocationSkipsRealFileAndDirectoryCollisions()
    {
        using var temporary = new TempDirectory("SharpProof.Ir.StagingAllocation.");
        var identities = ImmutableQueue.CreateRange(Enumerable.Range(1, 3).Select(Identity));
        var existingFile = Name(temporary.FullName, Identity(1));
        var existingDirectory = Name(temporary.FullName, Identity(2));
        File.WriteAllText(existingFile, "original");
        Directory.CreateDirectory(existingDirectory);
        var destination = Path.Combine(temporary.FullName, "output.txt");
        var allocated = AtomicFile.PrepareStaged(destination, () => { var identity = identities.Peek(); identities = identities.Dequeue(); return identity; });
        Assert.That(allocated, Is.EqualTo(Name(temporary.FullName, Identity(3))));
        Assert.That(identities, Is.Empty);
        Assert.That(File.Exists(allocated), Is.False);
        Assert.That(Directory.Exists(allocated), Is.False);
        Assert.That(File.ReadAllText(existingFile), Is.EqualTo("original"));
        Assert.That(Directory.Exists(existingDirectory), Is.True);
        Assert.That(File.Exists(destination), Is.False);
    }

    [Test]
    public void AllocationExhaustsExactlyEightRealCollisions()
    {
        using var temporary = new TempDirectory("SharpProof.Ir.StagingExhaustion.");
        var identities = ImmutableQueue.CreateRange(Enumerable.Range(1, 8).Select(Identity));
        foreach (var identity in identities)
        { File.WriteAllText(Name(temporary.FullName, identity), "original"); }
        var destination = Path.Combine(temporary.FullName, "output.txt");
        var failure = Assert.Throws<IOException>((Action)(() => AtomicFile.PrepareStaged(destination, () => { var identity = identities.Peek(); identities = identities.Dequeue(); return identity; })));
        Assert.That(failure!.Message, Is.EqualTo("Could not allocate a SharpProof staging path."));
        Assert.That(identities, Is.Empty);
        foreach (var identity in Enumerable.Range(1, 8).Select(Identity))
        { Assert.That(File.ReadAllText(Name(temporary.FullName, identity)), Is.EqualTo("original")); }
        Assert.That(File.Exists(destination), Is.False);
    }

    [Test]
    public void RootDestinationRejectsBeforeRequestingAStagingIdentity()
    {
        using var temporary = new TempDirectory("SharpProof.Ir.StagingRoot.");
        var root = Path.GetPathRoot(temporary.FullName)!;
        var requested = false;
        var failure = Assert.Throws<InvalidOperationException>((Action)(() => AtomicFile.PrepareStaged(root, () =>
        { requested = true; return Identity(1); })));
        Assert.That(failure!.Message, Is.EqualTo("The output path has no directory."));
        Assert.That(requested, Is.False);
    }

    [Test]
    public void IdentitySourceRejectsNullBeforeCreatingDestinationDirectory()
    {
        using var temporary = new TempDirectory("SharpProof.Ir.StagingSource.");
        var directory = Path.Combine(temporary.FullName, "missing");
        var failure = Assert.Throws<ArgumentNullException>((Action)(() => AtomicFile.PrepareStaged(Path.Combine(directory, "output.txt"), null!)));
        Assert.That(failure!.ParamName, Is.EqualTo("nextIdentity"));
        Assert.That(Directory.Exists(directory), Is.False);
    }

    private static Guid Identity(int value)
    {
        return new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static string Name(string directory, Guid identity)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        return Path.Combine(directory, ".sharpproof-" + identity.ToString("N") + ".tmp");
    }
}
