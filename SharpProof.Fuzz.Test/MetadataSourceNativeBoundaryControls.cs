using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

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
        MetadataProgramSourceCase[] cases = [new(Library, subject, 7L)];
        using (var actual = new MetadataSourceCompilation(cases))
        {
            await actual.CompileAsync();
            Assert.That(actual.Premises, Has.Length.EqualTo(1));
            Assert.That(actual.Premises[0].CallableId, Does.Contain("MetadataSubject.Target0("));
            Assert.That(actual.Premises[0].HasTotal, Is.True);
            Assert.That(actual.Premises[0].HasResult, Is.True);
            var total = actual.Preparations.Single().Total!;
            var parameter = total.Parameters.Single();
            var factory = total.Program.Factory;
            var entries = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
            {
                [parameter.Entry] = factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 7L)
            };
            var executed = new IrProgramInterpreter(factory).Execute(total.Program, entries);
            var expected = family is "identity" or "static-read" ? 7L : 0L;
            Assert.That(actual.Subject.GetMethod("Target0")!.Invoke(null, [7L]), Is.EqualTo(expected));
            if (family == "static-read")
            {
                Assert.That(actual.Subject.GetField("State")!.GetValue(null), Is.EqualTo(7L));
                Assert.That(total.Program.Blocks.SelectMany(block => block.Instructions)
                    .OfType<IrHavocInstruction>().Any(instruction => instruction.Origin == IrHavocOrigin.Approximation), Is.True);
                Assert.That(executed.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
            }
            else
            {
                Assert.That(executed.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(executed.ConsumedApproximation, Is.False);
                Assert.That(executed.ReturnValue!.Integer, Is.EqualTo(expected));
            }
        }
        var replay = new MetadataProgramSourceReplay(12370);
        await replay.CompareBatchAsync(cases, 0);
        var result = replay.Result;
        TestContext.Out.WriteLine("family=" + family + "; agreements=" + result.Agreements +
            "; nativeProofs=" + result.NativeProofs + "; nativeRefutations=" + result.NativeRefutations +
            "; details=" + string.Join(" | ", result.Failures.Select(failure => failure.Detail)));
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
