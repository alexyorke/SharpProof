using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A generic source callee whose parameter is a constructed delegate type
// (`Func<T, bool>` called with a `Func<int, bool>`) is inlined like one whose
// parameter is a constructed class: both are references, so the declaration
// receives the caller's reference unchanged. Invoking the delegate stays an
// unknown call.
[TestFixture]
public sealed class GenericDelegateParameterCallTests
{
    private const string Types = "using System;\nusing SharpProof.Attributes;\n";

    private const string Helpers = """
        public sealed class Box { }
        public static class H
        {
            public static int Probe<T>(Func<T, bool> f, T x) { return f == null ? 0 : 1; }
            public static int Guard<T>(Func<T, T> f, T x)
            {
                if (f == null) { throw new ArgumentNullException(nameof(f)); }
                return 0;
            }
            public static T Call<T>(Func<T, T> f, T x) { return f(x); }
            public static int Either<T>(Func<T, bool> f, Func<T, bool> g) { return f == g ? 1 : 0; }
        }
        """;

    [TestCase("[DoesNotThrow] public static int M(Func<int, bool> f) { return H.Probe(f, 3); }")]
    [TestCase("public static int M(Func<int, bool> f) { Contract.Requires(f != null); Contract.Ensures(Contract.Result<int>() == 1); return H.Probe(f, 0); }")]
    [TestCase("public static int M(Func<Box, bool> f, Box b) { Contract.Ensures(f != null || Contract.Result<int>() == 0); return H.Probe(f, b); }")]
    [TestCase("[DoesNotThrow] public static int M(Func<int, int> f) { Contract.Requires(f != null); return H.Guard(f, 1); }")]
    [TestCase("public static int M(Func<int, bool> f) { Contract.Ensures(Contract.Result<int>() == 1); return H.Either(f, f); }")]
    [TestCase("[EnforcePure] public static int M(Func<long, bool> f) { return H.Probe(f, 2L); }")]
    public async Task InlinedCalleesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[DoesNotThrow] public static int M(Func<int, int> f) { return H.Guard(f, 1); }")]
    [TestCase("public static int M(Func<int, bool> f) { Contract.Ensures(Contract.Result<int>() == 1); return H.Probe(f, 0); }")]
    [TestCase("public static int M(Func<int, bool> f, Func<int, bool> g) { Contract.Ensures(Contract.Result<int>() == 1); return H.Either(f, g); }")]
    public async Task InlinedCalleesAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // The delegate's target is unknown, so what invoking it does is too.
    [TestCase("[DoesNotThrow] public static int M(Func<int, int> f) { Contract.Requires(f != null); return H.Call(f, 1); }")]
    [TestCase("[EnforcePure] public static int M(Func<int, int> f) { Contract.Requires(f != null); return H.Call(f, 1); }")]
    [TestCase("public static int M(Func<int, int> f) { Contract.Requires(f != null); Contract.Ensures(Contract.Result<int>() == 1); return H.Call(f, 1); }")]
    public async Task DelegateInvocationsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + Helpers + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
