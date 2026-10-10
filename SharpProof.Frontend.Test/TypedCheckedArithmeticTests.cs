using System.Globalization;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedCheckedArithmeticTests
{
    public static IEnumerable<TestCaseData> CheckedCases()
    {
        yield return Case("sbyte Target(sbyte x) => checked((sbyte)(x & 128));", (sbyte)-1);
        yield return Case("short Target(short x) => checked((short)(x & 32768));", (short)-1);
        yield return Case("byte Target(byte x) { checked { x &= 255; return x; } }", byte.MaxValue);
        yield return Case("sbyte Target(sbyte x) => checked((sbyte)(x | 128));", (sbyte)0);
        yield return Case("sbyte Target(sbyte x) => checked((sbyte)(x ^ 255));", (sbyte)-1);
        yield return Case("byte Target(byte x) => checked((byte)~x);", (byte)0);
        yield return Case("short Target(short x) { checked { x ^= -1; return x; } }", short.MinValue);
        yield return Case("byte Target(byte x) { checked { x |= 128; return x; } }", (byte)1);
        foreach (var expression in new[] { "x + y", "x - y", "x * y" })
        {
            foreach (var pair in new[] { (int.MaxValue, 2), (int.MinValue, -1), (int.MaxValue, -1), (int.MinValue, 1),
                (-2, -3), (2, -3), (-2, 3), (0, int.MinValue), (int.MinValue, 0), (int.MinValue, int.MinValue), (int.MaxValue, int.MaxValue) })
            { yield return Case($"int Target(int x, int y) => checked({expression});", pair.Item1, pair.Item2); }
            foreach (var pair in new[] { (long.MaxValue, 2L), (long.MinValue, -1L), (long.MaxValue, -1L), (long.MinValue, 1L),
                (-2L, -3L), (2L, -3L), (-2L, 3L), (0L, long.MinValue), (long.MinValue, 0L), (long.MinValue, long.MinValue), (long.MaxValue, long.MaxValue) })
            { yield return Case($"long Target(long x, long y) => checked({expression});", pair.Item1, pair.Item2); }
            foreach (var pair in new[] { (uint.MaxValue, 2U), (0U, 1U), (2U, 3U), (0U, uint.MaxValue), (uint.MaxValue, 0U), (uint.MaxValue, uint.MaxValue) })
            { yield return Case($"uint Target(uint x, uint y) => checked({expression});", pair.Item1, pair.Item2); }
            foreach (var pair in new[] { (ulong.MaxValue, 2UL), (0UL, 1UL), (2UL, 3UL), (0UL, ulong.MaxValue), (ulong.MaxValue, 0UL), (ulong.MaxValue, ulong.MaxValue) })
            { yield return Case($"ulong Target(ulong x, ulong y) => checked({expression});", pair.Item1, pair.Item2); }
            yield return Case($"int Target(short x, short y) => checked({expression});", short.MinValue, short.MinValue);
            yield return Case($"int Target(ushort x, ushort y) => checked({expression});", ushort.MaxValue, ushort.MaxValue);
        }
        yield return Case("int Target(int x) => checked(-x);", int.MinValue);
        yield return Case("long Target(long x) => checked(-x);", long.MinValue);
        yield return Case("long Target(uint x) => checked(-x);", uint.MaxValue);
        yield return Case("int Target(short x) => checked(+x);", short.MinValue);
        foreach (var type in new[] { "sbyte", "byte", "short", "ushort", "char", "int", "uint", "long", "ulong" })
        {
            var value = type switch
            {
                "sbyte" => (object)sbyte.MaxValue,
                "byte" => byte.MaxValue,
                "short" => short.MaxValue,
                "ushort" => ushort.MaxValue,
                "char" => char.MaxValue,
                "int" => int.MaxValue,
                "uint" => uint.MaxValue,
                "long" => long.MaxValue,
                _ => ulong.MaxValue
            };
            yield return Case($"{type} Target({type} x) {{ checked {{ return ++x; }} }}", value);
            yield return Case($"{type} Target({type} x) {{ checked {{ x += ({type})1; return x; }} }}", value);
            yield return Case($"{type} Target({type} x) {{ checked {{ x *= ({type})2; return x; }} }}", value);
            yield return Case($"{type} Target({type} x) {{ checked {{ return x--; }} }}", value);
            var zero = Convert.ChangeType(0, value.GetType(), CultureInfo.InvariantCulture);
            yield return Case($"{type} Target({type} x) {{ checked {{ return --x; }} }}", zero);
            yield return Case($"{type} Target({type} x) {{ checked {{ x -= ({type})1; return x; }} }}", zero);
            yield return Case($"{type} Target({type} x) {{ unchecked {{ return ++x; }} }}", value);
            yield return Case($"{type} Target({type} x) {{ unchecked {{ x *= ({type})2; return x; }} }}", value);
            if (type is "sbyte" or "short" or "int" or "long")
            {
                var minimum = type switch { "sbyte" => (object)sbyte.MinValue, "short" => short.MinValue, "int" => int.MinValue, _ => long.MinValue };
                yield return Case($"{type} Target({type} x) {{ checked {{ return --x; }} }}", minimum);
                yield return Case($"{type} Target({type} x) {{ checked {{ x -= ({type})1; return x; }} }}", minimum);
            }
        }
        yield return Case("byte Target(byte x, byte y) { checked { x += y; return x; } }", (byte)100, (byte)155);
        yield return Case("byte Target(byte x, byte y) { checked { x *= y; return x; } }", (byte)10, (byte)25);
        yield return Case("int Target(short x) => checked(-x);", short.MinValue);
        yield return Case("int Target(sbyte x, sbyte y) => checked(x * y);", sbyte.MinValue, sbyte.MinValue);
        yield return Case("long Target(uint x, int y) => checked(x + y);", uint.MaxValue, int.MaxValue);
        yield return Case("long Target(uint x, int y) => checked(x * y);", uint.MaxValue, int.MinValue);
    }

    // SP-CORR-0001: compile-time constants do not execute runtime fault sites.
    [TestCase("int Target(int unused) => int.MinValue % -1;")]
    [TestCase("int Target(int unused) => checked(int.MinValue % -1);")]
    [TestCase("long Target(int unused) => long.MinValue % -1L;")]
    [TestCase("long Target(int unused) => checked(long.MinValue % -1L);")]
    public void ConstantFoldedRemainderMatchesCompiledRuntime(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(Convert.ToInt64(subject.Invoke([0]), CultureInfo.InvariantCulture), Is.Zero);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var execution = subject.Execute(lowered, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
    }

    [TestCaseSource(nameof(CheckedCases))]
    public void CheckedCandidateMatchesCompiledBoundary(string members, object[] arguments)
    {
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke(arguments);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var execution = subject.Execute(lowered, arguments);
        if (actual is Exception)
        {
            Assert.That(actual, Is.TypeOf<OverflowException>());
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue,
                Is.EqualTo(subject.Value(subject.Context.Result!.Value, actual!).IntegerNumericValue));
        }
    }

    [TestCase("byte Target(byte x, byte y) { checked { x += y; return x; } }", "byte", "int", false)]
    [TestCase("int Target(int x, short y) { checked { x += y; return x; } }", "int", "int", true)]
    [TestCase("uint Target(uint x, uint y) { checked { x += y; return x; } }", "uint", "uint", true)]
    public void ResolvedCompoundConversionsSelectPromotedOperatorBeforeStorage(string members, string targetType, string rightType, bool identity)
    {
        using var subject = TypedProgramSubject.Create(members);
        foreach (var operation in subject.Graph.Blocks.SelectMany(block => block.Operations).SelectMany(Descendants)
            .OfType<Microsoft.CodeAnalysis.Operations.ICompoundAssignmentOperation>())
        {
            Assert.That(operation.Target.Type!.ToDisplayString(), Is.EqualTo(targetType));
            Assert.That(operation.Value.Type!.ToDisplayString(), Is.EqualTo(rightType));
            Assert.That(operation.InConversion.IsIdentity, Is.EqualTo(identity));
            Assert.That(operation.OutConversion.IsIdentity, Is.EqualTo(identity));
        }
    }

    [TestCase("sbyte", false)]
    [TestCase("short", false)]
    [TestCase("sbyte", true)]
    [TestCase("short", true)]
    public void UIntCompoundWithSignedNarrowOperandIsRejectedByCSharp(string narrow, bool constant)
    {
        var members = constant ? $"uint Target(uint x) {{ const {narrow} y = 1; checked {{ x += y; return x; }} }}"
            : $"uint Target(uint x, {narrow} y) {{ checked {{ x += y; return x; }} }}";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers("public static " + members));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("PromotionProbe", [tree], TestMetadataReferences.Platform,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS0266"));
    }

    [TestCase("x += x++", 1073741824, 1073741825, "x += x++")]
    [TestCase("-x++", int.MinValue, int.MinValue + 1, "-x++")]
    [TestCase("++x", int.MaxValue, int.MaxValue, "++x")]
    [TestCase("--x", int.MinValue, int.MinValue, "--x")]
    [TestCase("x++ + ++x", int.MaxValue, int.MaxValue, "x++")]
    public void FaultRetainsEarlierEffectsButPreventsItsOwnWrite(string expression, int input, int stored, string site)
    {
        using var subject = TypedProgramSubject.Create($$"""
            int Target(int x) { try { return checked({{expression}}); } finally { } }
            """);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(subject.Invoke([input]), Is.TypeOf<OverflowException>());
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(stored)));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo(site));
    }

    [TestCase("x += x++", 1073741824, 1073741825)]
    [TestCase("-x++", int.MinValue, int.MinValue + 1)]
    [TestCase("++x", int.MaxValue, int.MaxValue)]
    public void CatchAndFinallyObserveOnlyCompletedOperandWrites(string expression, int input, int observed)
    {
        using var subject = TypedProgramSubject.Create($$"""
            int Target(int x) { try { try { return checked({{expression}}); }
                catch (System.OverflowException) { return x; } } finally { x = 7; } }
            """);
        Assert.That(subject.Invoke([input]), Is.EqualTo(observed));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(observed)));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(7)));
    }

    private static IEnumerable<Microsoft.CodeAnalysis.IOperation> Descendants(Microsoft.CodeAnalysis.IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations.SelectMany(Descendants))
        { yield return child; }
    }

    private static TestCaseData Case(string members, params object[] arguments)
    {
        var display = arguments.Select(argument => argument is char character
            ? ((int)character).ToString(CultureInfo.InvariantCulture) : Convert.ToString(argument, CultureInfo.InvariantCulture));
        return new TestCaseData(members, arguments).SetName("Checked oracle: " + members + " [" + string.Join(",", display) + "]");
    }
}
