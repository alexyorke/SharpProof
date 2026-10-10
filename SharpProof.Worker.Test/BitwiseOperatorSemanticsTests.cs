using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Built-in integer |, ^ and ~ promote narrow operands to int and mixed
// operands to the common numeric type, and never overflow. Boolean &, | and ^
// evaluate both operands. Every expectation is checked against CLR execution.
[TestFixture]
public sealed class BitwiseOperatorSemanticsTests
{
    [TestCase("return (a ^ b ^ b) == a;")]
    [TestCase("return (a | b) == (a ^ b) + (a & b);")]
    [TestCase("return (a | b) >= a || b < 0;")]
    [TestCase("return ~a == -a - 1;")]
    [TestCase("return ~~c == c && (c ^ ~c) == -1;")]
    [TestCase("return (c | 1) != 0 && ((c | b) & 1) >= (b & 1);")]
    [TestCase("return ~d == uint.MaxValue - d && (d ^ d) == 0;")]
    [TestCase("return (e | 0x80) >= 128 && ~e < 0;")]
    [TestCase("return (h ^ h) == 0 && (h | ~h) == ulong.MaxValue;")]
    [TestCase("return (i ^ -1) == ~i && (e ^ i) == (i ^ e);")]
    [TestCase("return ((a | d) < 0) == (a < 0);")]
    [TestCase("return ((c ^ d) >= 0) == (c >= 0);")]
    [TestCase("int x = a; x |= b; x ^= b; return x == (a & ~b);")]
    [TestCase("byte x = e; x |= 0x80; return x >= 128;")]
    [TestCase("sbyte x = i; x ^= -1; return x == (sbyte)~i;")]
    [TestCase("byte x = e; checked { x ^= 0xFF; } return x == 255 - e;")]
    [TestCase("ulong x = h; x ^= h; x |= 1; return x == 1;")]
    [TestCase("return (p | q) == (p || q) && (p ^ q) == (p != q) && (p & q) == (p && q);")]
    [TestCase("bool x = p; x &= q; x |= p; x ^= q & !q; return x == p;")]
    [TestCase("int n = 0; bool r = p & (++n > 0); r = r | (++n > 0); return n == 2;")]
    public async Task ClrBitwiseFactsAreProven(string body)
    {
        var (falses, claim) = await ObserveAndVerifyAsync(body);
        Assert.That(falses, Is.Zero, "The CLR must satisfy this bitwise fact.");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("return (a | b) >= a;")]
    [TestCase("return (a ^ b) >= 0;")]
    [TestCase("return ~c < 0;")]
    [TestCase("return (d | 1) == d + 1;")]
    [TestCase("return ((a | d) < 0) == (d > 0x7FFFFFFF);")]
    [TestCase("return (e | i) >= 0;")]
    [TestCase("return (~h) < h;")]
    [TestCase("byte x = e; x ^= 0x0F; return x <= e;")]
    [TestCase("return (p ^ q) == (p | q);")]
    [TestCase("int n = 0; bool r = p & (++n > 0); return n == 0 || r;")]
    public async Task FalseBitwiseFactsAreRefuted(string body)
    {
        var (falses, claim) = await ObserveAndVerifyAsync(body);
        Assert.That(falses, Is.Positive, "The CLR must falsify this bitwise fact.");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    private static async Task<(int ClrFalses, WorkerClaimResult Claim)> ObserveAndVerifyAsync(string body)
    {
        var source = """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target(int a, int b, long c, uint d, byte e, ulong h, sbyte i, bool p, bool q) {
                    Contract.Ensures(Contract.Result<bool>());
                    __BODY__
                }
            }
            """.Replace("__BODY__", body, StringComparison.Ordinal) + "\n";
        var compilation = TestCompilation.Create("BitwiseOperatorSemantics", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("BitwiseOperatorSemantics", isCollectible: true);
        var falses = 0;
        try
        {
            var method = context.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in Inputs())
            {
                if (!(bool)method.Invoke(null, input)!)
                { falses++; }
            }
        }
        finally { context.Unload(); }

        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables.Single().Total, Is.Not.Null, "Expected admitted compiler-captured total body.");
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        TestContext.WriteLine($"CLR falses={falses}; native={claim.Outcome}; reason={claim.Reason}");
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        return (falses, claim);
    }

    private static IEnumerable<object[]> Inputs()
    {
        long[] edges = [0, 1, -1, 2, 7, 8, 15, 16, 127, 128, 255, 256, -127, -128, -129, 0x7FFF, 0x8000, 0xFFFF,
            int.MaxValue, int.MinValue, uint.MaxValue, long.MaxValue, long.MinValue, 0x40000000, 0x55555555, unchecked((long)0xAAAAAAAAAAAAAAAAUL)];
        var random = new Random(20261010);
        long Pick()
        {
            return random.Next(3) == 0 ? random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(64) : edges[random.Next(edges.Length)];
        }
        for (var index = 0; index < 4000; index++)
        {
            yield return [unchecked((int)Pick()), unchecked((int)Pick()), Pick(), unchecked((uint)Pick()), unchecked((byte)Pick()),
                unchecked((ulong)Pick()), unchecked((sbyte)Pick()), random.Next(2) == 0, random.Next(2) == 0];
        }
    }
}
