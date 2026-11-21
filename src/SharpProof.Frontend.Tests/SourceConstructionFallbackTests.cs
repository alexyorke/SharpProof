using NUnit.Framework;

namespace SharpProof.Frontend.Test;

// A declared source class constructor the lowerer cannot expand runs as an
// unknown call when opaque calls are allowed: one of a class whose instance
// initializers cannot be separated from its body, and a primary
// constructor. A class whose type initializer has an effect is not
// constructed at all, and an unknown call does not take a struct by `in`.
[TestFixture]
public sealed class SourceConstructionFallbackTests
{
    private const string Types = """
        public sealed class Box<T> { public int N = 1; public Box(int n) { N = n; } }
        public sealed class Notifier { public event System.Action Changed; public int N = 1; public Notifier(int n) { N = n; } }
        public sealed class Primary(int value) { public int Value => value; }
        public class PrimaryBase(int value) { public int Value => value; }
        public sealed class OnPrimary(int value) : PrimaryBase(value + 1) { }
        public readonly struct Mark { public readonly int Line; public Mark(int line) { Line = line; } }
        public class Event : System.EventArgs { public Event(in Mark start) { } }
        public class Loud { public static int Count; static Loud() { System.Console.WriteLine(); } public Loud(int v) { } }
        public sealed class LoudPrimary(int v) { public static int Count; static LoudPrimary() { System.Console.WriteLine(); } }
        """;

    [TestCase("int Target(int x) { var b = new Box<int>(x); return x; }", true)]
    [TestCase("int Target(int x) { var n = new Notifier(x); return x; }", true)]
    [TestCase("int Target(int x) { var p = new Primary(x); return x; }", true)]
    [TestCase("int Target(int x) { var p = new OnPrimary(x); return x; }", true)]
    [TestCase("int Target(int x) { var m = new Mark(x); var e = new Event(m); return x; }", false)]
    [TestCase("int Target(int x) { var e = new Event(new Mark(x)); return x; }", false)]
    [TestCase("int Target(int x) { var l = new Loud(x); return x; }", false)]
    [TestCase("int Target(int x) { var l = new LoudPrimary(x); return x; }", false)]
    public void UnexpandedConstructorsRunAsUnknownCalls(string target, bool exact)
    {
        System.ArgumentNullException.ThrowIfNull(target);
        using var subject = TypedProgramSubject.Create(target, Types);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.EqualTo(exact), lowering.Classification.Abstention.ToString());
        Assert.That(subject.LowerSourceCalls(false).IsExact, Is.False);
    }
}
