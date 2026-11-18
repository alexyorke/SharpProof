using NUnit.Framework;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A postcondition names an out parameter's final value through
// Contract.ValueAtReturn. The parameter may be a field the caller passed, so
// a write to it may change memory, and effect claims stay unsupported.
[TestFixture]
public sealed class OutParameterPostconditionTests
{
    [Test]
    public async Task FinalValueOfAnOutParameterIsProven()
    {
        var outcomes = await Outcomes("""
            using SharpProof.Attributes;
            public static class Ticks {
                public static long Split(long ticks, out long rest) {
                    Contract.Ensures(Contract.ValueAtReturn(out rest) >= 0 && Contract.ValueAtReturn(out rest) < 8);
                    Contract.Ensures(Contract.Result<long>() * 8 + Contract.ValueAtReturn(out rest) == ticks);
                    rest = ticks & 7;
                    return ticks >> 3;
                }
            }
            """);
        Assert.That(outcomes, Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Proven }));
    }

    [Test]
    public async Task WrongFinalValueIsRefuted()
    {
        var outcomes = await Outcomes("""
            using SharpProof.Attributes;
            public static class Ticks {
                public static int Split(long ticks, out long rest) {
                    Contract.Ensures(Contract.ValueAtReturn(out rest) >= 0);
                    rest = ticks % 8;
                    return 0;
                }
            }
            """);
        Assert.That(outcomes, Is.EqualTo(new[] { WorkerClaimOutcome.Refuted }));
    }

    // Called as Read(out Holder.Value), each method returns 1.
    [TestCase("", "Value = 0; written = 1; return Value;", TestName = "WriteToAnOutParameterMayChangeAWrittenField")]
    [TestCase("Contract.Requires(Value == 0);", "written = 1; return Value;", TestName = "WriteToAnOutParameterMayChangeAReadField")]
    public async Task WriteToAnOutParameterMayChangeAField(string requires, string body)
    {
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(body);
        var outcomes = await Outcomes("""
            using SharpProof.Attributes;
            public static class Holder {
                public static int Value;
                public static int Read(out int written) {
            """ + requires + """

                    Contract.Ensures(Contract.Result<int>() == 0);
            """ + body + """

                }
            }
            """);
        Assert.That(outcomes, Is.EqualTo(new[] { WorkerClaimOutcome.Unknown }));
    }

    // Called as Read(box, out box.Value), the write breaks both clauses even
    // though the body itself reads no memory.
    [Test]
    public async Task ContractReadOfMemoryAnOutArgumentMayAliasIsUnknown()
    {
        var outcomes = await Outcomes("""
            using SharpProof.Attributes;
            public sealed class Box { public int Value; }
            public static class Holder {
                public static int Read([NonNull] Box box, out int written) {
                    Contract.Requires(box.Value == 0);
                    Contract.Ensures(box.Value == 0);
                    written = 1;
                    return 0;
                }
            }
            """);
        Assert.That(outcomes, Is.EqualTo(new[] { WorkerClaimOutcome.Unknown }));
    }

    [Test]
    // A store to an out parameter writes the caller's storage.
    public async Task PurityClaimOnAnOutParameterStoreIsRefuted()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Ticks {
                [EnforcePure]
                public static int Split(long ticks, out long rest) { rest = ticks & 7; return 0; }
            }
            """);
        Assert.That(response.ClaimResults.Select(static result => result.Outcome), Is.EqualTo(new[] { WorkerClaimOutcome.Refuted }));
    }

    [Test]
    public async Task CompanionNamesTheTargetsOutParameter()
    {
        var outcomes = await Outcomes("""
            using SharpProof.Attributes;
            public static class Ticks {
                public static long Split(long ticks, out long rest) { rest = ticks & 7; return ticks >> 3; }
            }
            [ContractFor(typeof(Ticks))]
            public static class TicksContracts {
                public static long Split(long ticks, out long rest) {
                    Contract.Ensures(Contract.Result<long>() * 8 + Contract.ValueAtReturn(out rest) == ticks);
                    rest = 0;
                    return 0;
                }
            }
            """);
        Assert.That(outcomes, Is.EqualTo(new[] { WorkerClaimOutcome.Proven }));
    }

    private static async Task<WorkerClaimOutcome[]> Outcomes(string source)
    {
        var response = await Verify(source);
        var postconditions = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.Postcondition)
            .Select(static claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
        var results = response.ClaimResults.Where(result => postconditions.Contains(result.ClaimId)).ToArray();
        await TestContext.Out.WriteLineAsync(string.Join("; ", results.Select(static result => result.Outcome + " " + result.Reason)));
        return [.. results.Select(static result => result.Outcome)];
    }

    private static async Task<WorkerVerifyResponse> Verify(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete), () => string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)));
        return response;
    }
}
