using System.Reflection;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class MetadataProgramSourceReplayTests
{
    private const string PositiveLibrary = "public static long M0(long value) => value + 1L;";
    private const string PositiveSubject = "public static long Target0(long value) { " +
        "Contract.Ensures(true); return MetadataLibrary.M0(value); }";

    [Test]
    public async Task ActualLibraryAndCallerAgreeWithDecodedInterpreterAndNativeProofs()
    {
        MetadataProgramSourceCase[] cases = [new(PositiveLibrary, PositiveSubject, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            await actual.CompileAsync();
            var premises = actual.Premises;
            Assert.That(premises, Has.Length.EqualTo(1));
            Assert.That(premises[0].CallableId, Does.Contain("MetadataSubject.Target0("));
            Assert.That(premises[0].HasTotal, Is.True);
            Assert.That(premises[0].HasResult, Is.True);
            Assert.That(actual.Subject.GetMethod("Target0")!.Invoke(null, [7L]), Is.EqualTo(8L));
        }
        var replay = new MetadataProgramSourceReplay(12345);
        await replay.CompareBatchAsync(cases, 0);
        Assert.That(replay.Result.Cases, Is.EqualTo(1));
        Assert.That(replay.Result.Agreements, Is.EqualTo(1));
        Assert.That(replay.Result.NativeProofs, Is.EqualTo(1));
        Assert.That(replay.Result.NativeRefutations, Is.EqualTo(1));
        Assert.That(replay.Result.Failures, Is.Empty);
    }

    [Test]
    public async Task ActualUncaughtLibraryDivisionFaultIsRetainedAfterRealArtifactPreparation()
    {
        const string library = "public static long M0(long value) => value / (value - value);";
        MetadataProgramSourceCase[] cases = [new(library, PositiveSubject, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            await actual.CompileAsync();
            var premises = actual.Premises;
            Assert.That(premises, Has.Length.EqualTo(1));
            Assert.That(premises[0].HasTotal, Is.True);
            Assert.That(premises[0].HasResult, Is.True);
            Action invoke = () => actual.Subject.GetMethod("Target0")!.Invoke(null, [7L]);
            var exception = Assert.Throws<TargetInvocationException>(invoke);
            Assert.That(exception!.InnerException, Is.TypeOf<DivideByZeroException>());
        }
        var replay = new MetadataProgramSourceReplay(12346);
        await replay.CompareBatchAsync(cases, 0);
        Assert.That(replay.Result.Cases, Is.EqualTo(1));
        Assert.That(replay.Result.Agreements, Is.Zero);
        Assert.That(replay.Result.NativeProofs, Is.Zero);
        Assert.That(replay.Result.NativeRefutations, Is.Zero);
        var reproducer = library + "\n" + PositiveSubject + "\n// input: 7";
        Assert.That(replay.Result.Failures, Has.Length.EqualTo(1));
        Assert.That(replay.Result.Failures[0], Is.EqualTo(new FuzzFailure(0, 12346,
            "metadata-program", reproducer, reproducer,
            "Target0: unexpected compiled fault DivideByZeroException")));
    }

    [Test]
    public async Task TwoActuallyDecodedTargetsRejectOneOwnedSourceCase()
    {
        const string extra = PositiveSubject + " public static long Target1(long value) { " +
            "Contract.Ensures(true); return MetadataLibrary.M0(value); }";
        MetadataProgramSourceCase[] cases = [new(PositiveLibrary, extra, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            await actual.CompileAsync();
            var premises = actual.Premises;
            Assert.That(premises, Has.Length.EqualTo(2));
            Assert.That(premises.All(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.HasTotal && item.HasResult)), Is.True);
            Assert.That(actual.Subject.GetMethod("Target0")!.Invoke(null, [7L]), Is.EqualTo(8L));
            Assert.That(actual.Subject.GetMethod("Target1")!.Invoke(null, [7L]), Is.EqualTo(8L));
        }
        var replay = new MetadataProgramSourceReplay(12347);
        Func<Task> compare = () => replay.CompareBatchAsync(cases, 0);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(compare);
        Assert.That(exception!.Message,
            Is.EqualTo("Metadata case universe does not match collected callables."));
    }

    [Test]
    public async Task ActualVoidArtifactWithoutResultRejectsTheScalarComparisonContract()
    {
        const string subject = "public static void Target0(long value) { " +
            "Contract.Ensures(value == Contract.Old(value)); }";
        MetadataProgramSourceCase[] cases = [new(PositiveLibrary, subject, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            await actual.CompileAsync();
            var premises = actual.Premises;
            Assert.That(premises, Has.Length.EqualTo(1));
            Assert.That(premises[0].HasTotal, Is.True);
            Assert.That(premises[0].HasResult, Is.False);
            var method = actual.Subject.GetMethod("Target0")!;
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(method.Invoke(null, [7L]), Is.Null);
        }
        var replay = new MetadataProgramSourceReplay(12348);
        await replay.CompareBatchAsync(cases, 0);
        Assert.That(replay.Result.Cases, Is.EqualTo(1));
        Assert.That(replay.Result.Agreements, Is.Zero);
        Assert.That(replay.Result.NativeProofs, Is.Zero);
        Assert.That(replay.Result.NativeRefutations, Is.Zero);
        var reproducer = PositiveLibrary + "\n" + subject + "\n// input: 7";
        Assert.That(replay.Result.Failures, Has.Length.EqualTo(1));
        Assert.That(replay.Result.Failures[0], Is.EqualTo(new FuzzFailure(0, 12348,
            "metadata-program", reproducer, reproducer, "Target0: incomplete metadata artifact")));
    }
}
