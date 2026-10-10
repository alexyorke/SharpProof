using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP0027 reports a call whose callee precondition is false in every state the
// advisory interpreter reaches it with; a call it cannot decide stays silent.
[TestFixture]
public sealed class AdvisoryRequiresDiagnosticsTests
{
    [TestCase("return Need(0);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("var x = -3; return Need(x + 1);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Need(1);", null)]
    [TestCase("return Need(input);", null)]
    // Source callees run as written, so Unknown() is known to return -1.
    [TestCase("return Need(Unknown());", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return input > 0 ? Need(input) : 0;", null)]
    // A local function that captures nothing runs as written, like a static
    // method, so its call site is reached with the caller's argument.
    [TestCase("return Twice(0); static int Twice(int y) => Need(y) * 2;", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Twice(1); static int Twice(int y) => Need(y) * 2;", null)]
    [TestCase("return Local(); static int Local() => Need(0);", "Call to 'Need' violates precondition 'value > 0'")]
    [TestCase("return Bounded(11);", "Call to 'Bounded' violates precondition '[InRange(0, 10)] value'")]
    [TestCase("return new Box(-1).Value;", "Call to '.ctor' violates precondition 'value > 0'")]
    [TestCase("return new Box(2).Value;", null)]
    // The callee is the call the marker belongs to, not whatever symbol the
    // call-site syntax binds to: an implicit collection-initializer Add sits
    // on its argument, and an accessor call sits on a property reference.
    [TestCase("return new Bag { -1 }.Count;", "Call to 'Add' violates precondition 'value > 0'")]
    [TestCase("return new Bag { 1 }.Count;", null)]
    [TestCase("return new Grid()[-1];", "Call to 'get_Item' violates precondition 'index >= 0'")]
    [TestCase("return new Grid()[1];", null)]
    [TestCase("return new Gate { Level = -1 }.Level;", "Call to 'set_Level' violates precondition 'value > 0'")]
    [TestCase("return new Gate { Level = 1 }.Level;", null)]
    [TestCase("return Generic<string>(0);", "Call to 'Generic' violates precondition 'value > 0'")]
    public async Task ReportsCallsThatAlwaysViolateAPrecondition(string body, string? message)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "private static int Bounded([InRange(0, 10)] int value) => value; " +
            "private static int Unknown() => -1; " +
            "private static int Generic<T>(int value) { Contract.Requires(value > 0); return value; } " +
            "public sealed class Box { public int Value; public Box(int value) { Contract.Requires(value > 0); Value = value; } } " +
            "public sealed class Bag : System.Collections.IEnumerable { public int Count; " +
            "public void Add(int value) { Contract.Requires(value > 0); Count = value; } " +
            "public System.Collections.IEnumerator GetEnumerator() => throw new System.NotSupportedException(); } " +
            "public sealed class Grid { public int this[int index] { get { Contract.Requires(index >= 0); return index; } } } " +
            "public sealed class Gate { private int _level; public int Level { get => _level; set { Contract.Requires(value > 0); _level = value; } } } " +
            "public static int Caller(int input) { " + body + " } }",
            "contracts", ["SP0027"]);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            message == null ? Is.Empty : Is.EqualTo([message]));
    }
}
