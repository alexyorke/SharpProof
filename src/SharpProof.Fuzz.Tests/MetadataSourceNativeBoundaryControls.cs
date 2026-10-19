using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class MetadataSourceNativeBoundaryControls
{
    private const string Library = "public static long M0(long value) => value;";

    [TestCase("identity")]
    [TestCase("static-read")]
    [TestCase("countdown")]
    [TestCase("two-entry-cycle")]
    public async Task ActualDecodedSourceKeepsItsInterpreterAndNativeBoundary(string family)
    {
        var body = family switch
        {
            "identity" => "return value;",
            "static-read" => "return State;",
            "countdown" => "while (value > 0) value = unchecked(value - 1); return value;",
            "two-entry-cycle" => "if (value > 3) goto A; goto B; " +
                "A: value = unchecked(value - 1); if (value > 0) goto B; return value; " +
                "B: value = unchecked(value - 1); if (value > 0) goto A; return value;",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        var subject = (family == "static-read" ? "public static long State = 7; " : "") +
            "public static long Target0(long value) { Contract.Ensures(true); " + body + " }";
        ImmutableArray<MetadataProgramSourceCase> cases = [new(Library, subject, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            var compiled = await actual.CompileAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(compiled.Premises, Has.Length.EqualTo(1));
            Assert.That(compiled.Premises[0].CallableId, Does.Contain("MetadataSubject.Target0("));
            Assert.That(compiled.Premises[0].HasTotal, Is.True);
            Assert.That(compiled.Premises[0].HasResult, Is.True);
            var expected = family is "identity" or "static-read" ? 7L : 0L;
            Assert.That(compiled.Subject.GetMethod("Target0")!.Invoke(null, [7L]), Is.EqualTo(expected));
            if (family == "static-read")
            {
                Assert.That(compiled.Subject.GetField("State")!.GetValue(null), Is.EqualTo(7L));
            }
        }
        var replay = new MetadataProgramSourceReplay(12370);
        await replay.CompareBatchAsync(cases, 0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = replay.Result;
        await TestContext.Out.WriteLineAsync("family=" + family + "; agreements=" + result.Agreements +
            "; nativeProofs=" + result.NativeProofs + "; nativeRefutations=" + result.NativeRefutations +
            "; details=" + string.Join(" | ", result.Failures.Select(failure => failure is null ? throw new System.ArgumentNullException(nameof(failure)) : (failure.Detail))));
        Assert.That(result.Cases, Is.EqualTo(1));
        if (family == "identity")
        {
            Assert.That(result.Agreements, Is.EqualTo(1));
            Assert.That(result.NativeProofs, Is.EqualTo(1));
            Assert.That(result.NativeRefutations, Is.EqualTo(1));
            Assert.That(result.Failures, Is.Empty);
        }
        else
        {
            Assert.That(result.Agreements, Is.Zero);
            Assert.That(result.Failures, Has.Length.EqualTo(1));
            var detail = result.Failures[0].Detail;
            if (family == "static-read")
            {
                Assert.That(result.NativeProofs, Is.Zero);
                Assert.That(result.NativeRefutations, Is.Zero);
                Assert.That(detail, Is.EqualTo("Target0: compiled/original disagreement"));
            }
            else if (family == "two-entry-cycle")
            {
                Assert.That(result.NativeProofs, Is.Zero);
                Assert.That(result.NativeRefutations, Is.Zero);
                Assert.That(detail, Is.EqualTo("Target0: native admission UnsupportedBody"));
            }
            else
            {
                Assert.That(detail, Does.StartWith("Target0: native agreement "));
            }
            var reproducer = Library + "\n" + subject + "\n// input: 7";
            Assert.That(result.Failures[0].Original, Is.EqualTo(reproducer));
            Assert.That(result.Failures[0].Minimized, Is.EqualTo(reproducer));
        }
    }
}
