using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Built-in shifts use only the low five (32-bit) or six (64-bit) count bits,
// >> is arithmetic for signed values, >>> is logical, and byte/short storage
// is promoted to int. Every expectation is checked against CLR execution.
[TestFixture]
public sealed class ShiftOperatorSemanticsTests
{
    [TestCase("return (a << b) == (a << (b & 31));")]
    [TestCase("return (a << 32) == a && (a << -1) == (a << 31);")]
    [TestCase("return (a >> 33) == (a >> 1);")]
    [TestCase("return (a >> 31) == 0 || (a >> 31) == -1;")]
    [TestCase("return (a >>> 31) == 0 || (a >>> 31) == 1;")]
    [TestCase("return (int)((uint)a >> b) == a >>> b;")]
    [TestCase("return (d >> 1) <= 0x7FFFFFFF;")]
    [TestCase("return (c << b) == (c << (b & 63));")]
    [TestCase("return (c << 64) == c && (c >> -1) == (c >> 63);")]
    [TestCase("return (c >>> b) >= 0 || (b & 63) == 0;")]
    [TestCase("return ((ulong)d << 32 >> 32) == d;")]
    [TestCase("return (e << 8) <= 65280;")]
    [TestCase("return (i >> b) <= 0 || i > 0;")]
    [TestCase("int x = a; x <<= b; x >>= b; return x == a || (b & 31) != 0;")]
    [TestCase("int x = a; x >>>= b; return x == (a >>> b);")]
    [TestCase("ulong x = h; x >>>= b; return x == h >> b;")]
    [TestCase("byte x = e; x <<= b; return x == (byte)(e << b);")]
    [TestCase("byte x = e; x >>= 1; return x <= 127;")]
    [TestCase("sbyte x = i; x >>>= 1; return x == (sbyte)(i >>> 1);")]
    [TestCase("byte x = e; checked { x <<= 1; } return e <= 127;")]
    public async Task ClrShiftFactsAreProven(string body)
    {
        var (falses, claim) = await ObserveAndVerifyAsync(body);
        Assert.That(falses, Is.Zero, "The CLR must satisfy this shift fact.");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("return (a << 32) == 0;")]
    [TestCase("return (c << b) == (c << (b & 31));")]
    [TestCase("return (a >> 1) == a / 2;")]
    [TestCase("return (a >>> 31) == (a >> 31);")]
    [TestCase("return (c >> 1) >= 0;")]
    [TestCase("return (d >> 1) <= 0x3FFFFFFF;")]
    [TestCase("return (e << 24) >= 0;")]
    [TestCase("byte x = e; x <<= 1; return x >= e;")]
    [TestCase("sbyte x = i; x >>>= 1; return x >= 0;")]
    public async Task FalseShiftFactsAreRefuted(string body)
    {
        var (falses, claim) = await ObserveAndVerifyAsync(body);
        Assert.That(falses, Is.Positive, "The CLR must falsify this shift fact.");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    private static async Task<(int ClrFalses, WorkerClaimResult Claim)> ObserveAndVerifyAsync(string body)
    {
        var source = """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target(int a, int b, long c, uint d, byte e, ulong h, sbyte i) {
                    Contract.Ensures(Contract.Result<bool>());
                    __BODY__
                }
            }
            """.Replace("__BODY__", body, StringComparison.Ordinal) + "\n";
        var compilation = TestCompilation.Create("ShiftOperatorSemantics", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("ShiftOperatorSemantics", isCollectible: true);
        var falses = 0;
        try
        {
            var method = context.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in Inputs())
            {
                try
                {
                    if (!(bool)method.Invoke(null, input)!)
                    { falses++; }
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is OverflowException)
                {
                    // A checked narrowing fault is an exceptional exit, not a false postcondition.
                }
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
        long[] edges = [0, 1, -1, 2, 7, 8, 31, 32, 33, 63, 64, 65, 127, 128, 200, 255, -31, -32, -33, -64,
            int.MaxValue, int.MinValue, uint.MaxValue, long.MaxValue, long.MinValue, 0x40000000];
        var random = new Random(20261010);
        long Pick()
        {
            return random.Next(3) == 0 ? random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(64) : edges[random.Next(edges.Length)];
        }
        for (var index = 0; index < 4000; index++)
        {
            yield return [unchecked((int)Pick()), unchecked((int)Pick()), Pick(), unchecked((uint)Pick()), unchecked((byte)Pick()),
                unchecked((ulong)Pick()), unchecked((sbyte)Pick())];
        }
    }
}
