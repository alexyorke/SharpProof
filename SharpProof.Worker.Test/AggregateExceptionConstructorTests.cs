using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AggregateExceptionConstructorTests
{
    [TestCase("\"outer\", (System.Exception)null")]
    [TestCase("(string)null, default(System.Exception)")]
    [TestCase("innerException: (System.Exception)null, message: \"outer\"")]
    public async Task NullInnerExceptionCannotProveOnlyAggregateExceptionEscapes(string arguments)
    {
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { " +
            "throw new System.AggregateException(" + arguments + "); } }";
        AssertRuntimeException<ArgumentNullException>(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PossiblyNullInnerExceptionCannotProveConstructionDoesNotThrow(bool requireNull)
    {
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "[DoesNotThrow] public static object Target(System.Exception inner) { " +
            (requireNull ? "Contract.Requires(inner == null); " : "") +
            "return new System.AggregateException(\"outer\", inner); } }";
        AssertRuntimeException<ArgumentNullException>(source, [null]);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase("new System.AggregateException()")]
    [TestCase("new System.AggregateException(\"outer\")")]
    [TestCase("new System.AggregateException(\"outer\", new System.Exception())")]
    [TestCase("new System.AggregateException(innerException: new System.Exception(), message: \"outer\")")]
    public async Task KnownSafeAggregateConstructionRetainsExactExceptionProof(string expression)
    {
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw " + expression + "; } }";
        AssertRuntimeException<AggregateException>(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task OrdinaryExceptionConstructorStillAcceptsNullInnerException()
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "[DoesNotThrow] public static object Target() { return new System.Exception(\"outer\", (System.Exception)null); } }";
        AssertRuntimeReturnsException(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task DisallowedOrdinaryExceptionRetainsConcreteRefutation()
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw new System.ArgumentNullException(\"inner\"); } }";
        AssertRuntimeException<ArgumentNullException>(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }

    private static void AssertRuntimeException<TException>(string source, object?[]? arguments = null) where TException : Exception
    {
        WithRuntimeTarget(source, target =>
        {
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => target.Invoke(null, arguments)));
            Assert.That(error!.InnerException, Is.TypeOf<TException>());
        });
    }

    private static void AssertRuntimeReturnsException(string source)
    {
        WithRuntimeTarget(source, target => Assert.That(target.Invoke(null, null), Is.TypeOf<Exception>()));
    }

    private static void WithRuntimeTarget(string source, Action<System.Reflection.MethodInfo> check)
    {
        using var image = new MemoryStream();
        var emission = TestCompilation.Create("AggregateExceptionConstructorOracle", source).Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("AggregateExceptionConstructorOracle", isCollectible: true);
        try
        { check(context.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!); }
        finally { context.Unload(); }
    }
}
