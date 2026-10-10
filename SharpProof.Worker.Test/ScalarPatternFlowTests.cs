using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Switch statements, switch expressions and `is` tests over a built-in scalar
// reach the Total IR as IIsPatternOperation tests. Constant, relational,
// discard, `not`, `and` and `or` patterns are pure comparisons of the tested
// value with constants; each outcome is checked against CLR execution.
[TestFixture]
[NonParallelizable]
public sealed class ScalarPatternFlowTests
{
    private const string Prelude = "using SharpProof.Attributes; public static class C { public static int State; ";

    private static readonly int[] Inputs = [int.MinValue, int.MinValue + 1, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 97, 122, 123, int.MaxValue - 1, int.MaxValue];

    [TestCase("switch (x) { case 1: return 10 / (x - 1); case 2 when x > 0: return 9; default: return 2; }", WorkerClaimOutcome.Refuted)]
    [TestCase("switch (x) { case 1: return 10 / x; case 2 when x > 0: return 9; default: return 2; }", WorkerClaimOutcome.Proven)]
    [TestCase("switch (x) { case 0: return 1; case 2 when x > 0: return 10 / (x - 2); default: return 10 / x; }", WorkerClaimOutcome.Refuted)]
    [TestCase("switch (x) { case 0: return 1; case 2 when x < 0: return 10 / (x - 2); default: return 10 / x; }", WorkerClaimOutcome.Proven)]
    [TestCase("switch (x) { case > 3: return 10 / (x - 4); case < 0: return 1; default: return 0; }", WorkerClaimOutcome.Refuted)]
    [TestCase("switch (x) { case > 4: return 10 / (x - 4); case < 0: goto case 1; case 1: return 2; default: return 0; }", WorkerClaimOutcome.Proven)]
    [TestCase("switch (x) { case 1: x = 3; goto case 3; case 3: x = 10 / (x - 3); break; } return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("return x switch { 0 => 1, > 5 when x < 9 => 10 / (x - 6), _ => x };", WorkerClaimOutcome.Refuted)]
    [TestCase("return x switch { 0 => 1, > 6 and < 9 => 10 / (x - 6), _ => x };", WorkerClaimOutcome.Proven)]
    [TestCase("return x switch { < 0 or > 9 => 0, not 5 => 1, _ => 10 / (x - 5) };", WorkerClaimOutcome.Refuted)]
    [TestCase("return x switch { < 0 or > 9 => 0, not 5 => 1, _ => 10 / (x - 4) };", WorkerClaimOutcome.Proven)]
    [TestCase("return x is > 2 and < 5 ? throw new System.ArgumentOutOfRangeException() : x;", WorkerClaimOutcome.Refuted)]
    [TestCase("return x is > 2 and < 4 && x != 3 ? throw new System.ArgumentOutOfRangeException() : x;", WorkerClaimOutcome.Proven)]
    [TestCase("return x is not (> 2 and < 5) or 3 ? x : 10 / (x - x);", WorkerClaimOutcome.Refuted)]
    [TestCase("return x is not (> 2 and < 5) or 3 or 4 ? x : 10 / (x - x);", WorkerClaimOutcome.Proven)]
    [TestCase("return x is int.MinValue ? 0 : checked(-x);", WorkerClaimOutcome.Proven)]
    [TestCase("return x is int.MaxValue ? 0 : checked(-x);", WorkerClaimOutcome.Refuted)]
    [TestCase("return x is >= int.MaxValue ? 0 : checked(x + 1);", WorkerClaimOutcome.Proven)]
    [TestCase("return x is > int.MaxValue - 1 ? 0 : checked(x + 2);", WorkerClaimOutcome.Refuted)]
    // Unsigned, narrow, 64-bit and Boolean inputs keep their own comparisons.
    [TestCase("uint u = (uint)x; return u is >= 0x80000000u ? 10 / (x - x) : 0;", WorkerClaimOutcome.Refuted)]
    [TestCase("uint u = (uint)x; return u is >= 0x80000000u ? 0 : x is < 0 ? 10 / (x - x) : 1;", WorkerClaimOutcome.Proven)]
    [TestCase("char c = (char)x; return c is >= 'a' and <= 'z' ? 10 / (c - 'a') : 0;", WorkerClaimOutcome.Refuted)]
    [TestCase("char c = (char)x; return c is > 'a' and <= 'z' ? 10 / (c - 'a') : 0;", WorkerClaimOutcome.Proven)]
    [TestCase("byte b = unchecked((byte)x); return b switch { 0 => 1, < 10 => 2, _ => 10 / (x & 0) };", WorkerClaimOutcome.Refuted)]
    [TestCase("byte b = unchecked((byte)x); return b switch { 0 => 1, < 10 => 2, <= 255 => 3 };", WorkerClaimOutcome.Proven)]
    [TestCase("long l = x; return l is > 2147483647L ? 10 / (x - x) : 0;", WorkerClaimOutcome.Proven)]
    [TestCase("long l = x; return l is > 2147483646L ? 10 / (x - x) : 0;", WorkerClaimOutcome.Refuted)]
    [TestCase("bool b = x > 3; return b is true ? 10 / (x - 4) : 0;", WorkerClaimOutcome.Refuted)]
    [TestCase("bool b = x > 4; return b is not false ? 10 / (x - 4) : 0;", WorkerClaimOutcome.Proven)]
    // Patterns nested in protected regions and loops.
    [TestCase("try { switch (x) { case 1: return 10 / (x - 1); default: return 0; } } finally { x++; }", WorkerClaimOutcome.Refuted)]
    [TestCase("try { return x switch { 0 => 1, _ => 10 / x }; } catch (System.DivideByZeroException) { return 0; }", WorkerClaimOutcome.Proven)]
    [TestCase("for (int i = 0; i < 2; i++) { if (i is 1) x = 10 / (x - 7); } return x;", WorkerClaimOutcome.Refuted)]
    public async Task DoesNotThrowMatchesExecution(string body, WorkerClaimOutcome expected)
    {
        var source = Prelude + "[DoesNotThrow] public static int Target(int x) { " + body + " } }";
        var thrown = Execute(source).FirstOrDefault(run => run.Exception != null);
        Assert.That(thrown.Exception == null, Is.EqualTo(expected == WorkerClaimOutcome.Proven),
            "CLR oracle disagrees with the expected outcome: " + thrown.Exception + " at x=" + thrown.X);
        Assert.That(await Verify(source), Is.EqualTo(expected));
    }

    [TestCase("Contract.Ensures(Contract.Result<int>() >= 0); return x switch { < 0 => 0, _ => x };", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Ensures(Contract.Result<int>() >= 0); return x switch { < -1 => 0, _ => x };", WorkerClaimOutcome.Refuted)]
    [TestCase("Contract.Ensures(Contract.Result<int>() != 3); switch (x) { case 3: case 4: return 5; default: return x is 3 ? 3 : 0; }", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Ensures(Contract.Result<int>() != 3); switch (x) { case 4: return 5; default: return x is 3 ? 3 : 0; }", WorkerClaimOutcome.Refuted)]
    public async Task PostconditionsMatchExecution(string body, WorkerClaimOutcome expected)
    {
        Assert.That(await Verify(Prelude + "public static int Target(int x) { " + body + " } }"), Is.EqualTo(expected));
    }

    [TestCase("switch (x) { case 7: State = 1; break; } return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("switch (x) { case 7: return 1; case > 7: return 2; } return x;", WorkerClaimOutcome.Proven)]
    [TestCase("if (x is 7 && x != 7) State = 1; return x;", WorkerClaimOutcome.Proven)]
    public async Task PurityMatchesExecution(string body, WorkerClaimOutcome expected)
    {
        var source = Prelude + "[EnforcePure] public static int Target(int x) { " + body + " } }";
        Assert.That(Execute(source).Any(run => run.State != 0), Is.EqualTo(expected == WorkerClaimOutcome.Refuted));
        Assert.That(await Verify(source), Is.EqualTo(expected));
    }

    // Controls: patterns that test or bind a type are still not modeled.
    [TestCase("object o = x; return o is int k ? 10 / k : 0;")]
    [TestCase("return x is var k ? 10 / (k - k) : 0;")]
    [TestCase("int? n = x; return n is 3 ? 10 / (x - x) : 0;")]
    public async Task TypeAndDeclarationPatternsStayUnknown(string body)
    {
        Assert.That(await Verify(Prelude + "[DoesNotThrow] public static int Target(int x) { " + body + " } }"),
            Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    private static async Task<WorkerClaimOutcome> Verify(string source)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync(claim.Outcome + " " + claim.Reason);
        return claim.Outcome;
    }

    private static List<(int X, string? Exception, int State)> Execute(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create("P" + Guid.NewGuid().ToString("N"), [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var type = Assembly.Load(stream.ToArray()).GetType("C")!;
        var method = type.GetMethod("Target")!;
        var state = type.GetField("State")!;
        var runs = new List<(int X, string? Exception, int State)>();
        foreach (var x in Inputs)
        {
            state.SetValue(null, 0);
            string? exception = null;
            try
            { method.Invoke(null, [x]); }
            catch (TargetInvocationException invocation)
            { exception = invocation.InnerException!.GetType().Name; }
            runs.Add((x, exception, (int)state.GetValue(null)!));
        }
        return runs;
    }
}
