using NUnit.Framework;
using SharpProof.Worker.Protocol;
using SharpProof.Ir;
using SharpProof.Smt;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task DefaultComparerNormalCompletionHasConcreteRuntimeWitness()
    {
        var preparation = Prepare("[ZeroAllocations] public static int Target() { Contract.Ensures(Contract.Result<int>() == 1); var value = System.Collections.Generic.Comparer<int>.Default; return value != null ? 1 : 0; }");
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, TestContext.CurrentContext.CancellationToken), Is.True, failure.ToString());
        var replay = plan!.NormalCompletionReplay();
        var execution = new IrProgramInterpreter(plan.Factory).Execute(candidate.Program, null, 4096, replay.ReplayOptions!, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), execution.Instruction?.ToString());
        Assert.That(execution.ConsumedApproximation, Is.False);
        var query = plan.NormalCompletionQuery();
        using var session = new CallableSolverSession(plan.Factory, new IrSmtBackendOptions()) { ConcreteOpaqueValueProvider = plan.ObserveFrameworkOpaqueValue };
        var model = (await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken)).Model!.Assignments;
        var interpreter = new IrInterpreter(plan.Factory);
        foreach (var fact in query.Assumptions)
        {
            var evaluated = interpreter.Evaluate(fact.Predicate, model, null, null, TestContext.CurrentContext.CancellationToken, concreteOpaqueValueProvider: plan.ObserveFrameworkOpaqueValue);
            Assert.That(evaluated.Value?.Boolean, Is.True, new IrPrinter(plan.Factory).Print(fact.Predicate));
        }
        Assert.That(interpreter.Evaluate(query.Goal.Predicate, model, null, null, TestContext.CurrentContext.CancellationToken,
            concreteOpaqueValueProvider: plan.ObserveFrameworkOpaqueValue).Value?.Boolean, Is.False);
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    [TestCase("bool")]
    [TestCase("double")]
    [TestCase("decimal")]
    [TestCase("string")]
    [TestCase("object")]
    [TestCase("int?")]
    public async Task DefaultComparerNormalReturnsAreNonNullSingletons(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var first = System.Collections.Generic.Comparer<" + element +
            ">.Default; var second = System.Collections.Generic.Comparer<" + element + ">.Default; " +
            "return first != null && object.ReferenceEquals(first, second) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DefaultComparerSingletonsRemainDistinctAcrossClosedSourceHelperTypes()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); return !object.ReferenceEquals(Get<int>(), Get<long>()) ? 1 : 0;",
            WorkerClaimOutcome.Proven, members: "static object Get<T>() { return System.Collections.Generic.Comparer<T>.Default; }");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DefaultComparerEntryAliasesUseTheObservedFrameworkInstance(bool claim)
    {
        await CheckInterfaceArrayContract("object value", "Contract.Ensures(Contract.Result<bool>() == " + (claim ? "true" : "false") + "); " +
            "return object.ReferenceEquals(value, System.Collections.Generic.Comparer<int>.Default);", WorkerClaimOutcome.Refuted, "bool");
    }

    [Test]
    public async Task DefaultComparerEntryInterfaceAliasUsesTheObservedFrameworkInstance()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IComparer<int> value", "Contract.Ensures(!Contract.Result<bool>()); " +
            "return object.ReferenceEquals(value, System.Collections.Generic.Comparer<int>.Default);", WorkerClaimOutcome.Refuted, "bool");
    }

    [Test]
    public async Task DefaultComparerObservedIdentityCannotBecomeASourceSubclass()
    {
        await CheckInterfaceArrayContract("Custom value", "Contract.Ensures(Contract.Result<bool>()); " +
            "return !object.ReferenceEquals(value, System.Collections.Generic.Comparer<int>.Default);", WorkerClaimOutcome.Unknown, "bool",
            "public sealed class Custom : System.Collections.Generic.Comparer<int> { public override int Compare(int x, int y) => 0; }");
    }

    [TestCase("dynamic", "object")]
    [TestCase("string?", "string")]
    [TestCase("(int Left, int Right)", "(int First, int Second)")]
    public async Task RuntimeEquivalentComparerTypesShareTheSameSingleton(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); return object.ReferenceEquals(System.Collections.Generic.Comparer<" + first + ">.Default, " +
            "System.Collections.Generic.Comparer<" + second + ">.Default) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [TestCase("int")]
    [TestCase("object")]
    public async Task DefaultComparerCachedValuesRetainInitializationEffects(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "var value = System.Collections.Generic.Comparer<" + element + ">.Default; return value != null ? 1 : 0;", false, false, false);
    }

    [Test]
    public async Task DefaultComparerGetterRemainsUnsupportedInPureClauses()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(object.ReferenceEquals(Contract.Result<object>(), System.Collections.Generic.Comparer<int>.Default)); " +
            "return System.Collections.Generic.Comparer<int>.Default;", WorkerClaimOutcome.Unknown, "object");
    }

    [Test]
    public async Task DefaultComparerOpenTypeParametersCannotEstablishDifferentRuntimeTypes()
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T, U>() { " +
            "Contract.Ensures(Contract.Result<int>() == 0); return object.ReferenceEquals(System.Collections.Generic.Comparer<T>.Default, " +
            "System.Collections.Generic.Comparer<U>.Default) ? 1 : 0; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }
}
