using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Accessors and operators carry effect claims like methods do, and a call to a
// trusted bodyless boundary has only the effects its contract declares.
[TestFixture]
public sealed class NativeCallableKindTests
{
    [Test]
    public async Task AutoPropertyGetterIsPureAndSetterWritesItsField()
    {
        var getter = Prepare("public sealed class C { public int Value { [EnforcePure, ZeroAllocations, DoesNotThrow] get; set; } }");
        var setter = Prepare("public sealed class C { public int Value { get; [EnforcePure] set; } }");
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(getter, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(getter, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(getter, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var write = await NativeEffectSiteVerifier.VerifyPurityAsync(setter, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            foreach (var result in ImmutableArray.Create(purity, allocations, exceptions))
            { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
            Assert.That(write.Outcome, Is.TypeOf<RefutedOutcome>(), write.Reason.ToString());
        }
    }

    [TestCase("public int Value { [ZeroAllocations] get { return 1; } }", typeof(ProvenOutcome))]
    [TestCase("public object Value { [ZeroAllocations] get => new object(); }", typeof(RefutedOutcome))]
    [TestCase("[ZeroAllocations] public static int operator +(C left, int right) => right;", typeof(ProvenOutcome))]
    [TestCase("[ZeroAllocations] public static explicit operator int(C value) { var box = new object(); return 1; }", typeof(RefutedOutcome))]
    public async Task BodiedAccessorsAndOperatorsAreLowered(string member, Type outcome)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        System.ArgumentNullException.ThrowIfNull(outcome);
        var result = await NativeEffectSiteVerifier.VerifyAsync(Prepare("public sealed class C { " + member + " }"),
            new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    // A boundary's declared contract stands for its implementation, even one
    // whose metadata body does less.
    [TestCase("SharpProofCapability.Clock", false, typeof(ProvenOutcome))]
    [TestCase("SharpProofCapability.None", false, null)]
    [TestCase("SharpProofCapability.Clock", true, typeof(ProvenOutcome))]
    [TestCase("SharpProofCapability.None", true, null)]
    public async Task TrustedBoundaryCallsUseTheirDeclaredCapabilities(string allowed, bool hasBody, Type? outcome)
    {
        System.ArgumentNullException.ThrowIfNull(allowed);
        using var directory = new TempDirectory("sharpproof-boundary-");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(
            PrepareBoundaryCaller(directory, "[AllowedCapabilities(" + allowed + ")]", hasBody), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>()); }
    }

    // The boundary reads a clock: it allocates nothing but is not pure.
    [Test]
    public async Task TrustedBoundaryCallsUseTheirDeclaredEffects()
    {
        using var directory = new TempDirectory("sharpproof-boundary-");
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(
            PrepareBoundaryCaller(directory, "[ZeroAllocations]"), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(
            PrepareBoundaryCaller(directory, "[EnforcePure]"), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        }
    }

    private static CompilerCallablePreparation PrepareBoundaryCaller(TempDirectory directory, string attribute, bool hasBody = false)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(directory);
        var external = TestCompilation.Create("TrustedBoundary", """
            using SharpProof.Attributes;
            public static class Boundary {
                [SharpProofTrusted("Reviewed clock boundary.")]
                [EffectContract(SharpProofEffect.ReadsAmbientState, Capabilities = SharpProofCapability.Clock, Complete = true, PreconditionFree = true)]
                public static
            """ + (hasBody ? " int Now() => 0;" : " extern int Now();") + """
             }
            """);
        var path = Path.Combine(directory.FullName, "TrustedBoundary.dll");
        if (!File.Exists(path))
        { Assert.That(external.Emit(path).Success, Is.True); }
        var compilation = CSharpCompilation.Create("TrustedCaller", [CSharpSyntaxTree.ParseText(
            "using SharpProof.Attributes; public static class C { " + attribute + " public static int Target() => Boundary.Now(); }",
            (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs")],
            external.References.Append(MetadataReference.CreateFromFile(path)), external.Options);
        TestCompilation.AssertNoErrors(compilation);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.EffectClaims.Length != 0));
    }

    // A constructor that runs no member initializers or base constructor
    // other than object's initializes an object no caller observes until
    // `this` escapes.
    [TestCase("int _x; [EnforcePure, DoesNotThrow, ZeroAllocations] public C(int x) { _x = x; }", "proven")]
    [TestCase("int _x; [ZeroAllocations] public C() { var box = new object(); _x = 1; }", "refuted")]
    [TestCase("int _x; int _y = 1; [EnforcePure] public C(int x) { _x = x; }", "unknown")]
    [TestCase("int _x; [EnforcePure] public C(int x) { _x = x; Keep(this); } static void Keep(C c) { }", "unknown")]
    // `new C(...)` allocates, then runs a plain source constructor as written.
    [TestCase("int _x; public C(int x) { _x = x; } [ZeroAllocations] public static int Make(int y) { var c = new C(y); return y; }", "refuted")]
    [TestCase("int _x; public C(int x) { _x = 10 / x; } [EnforcePure] public static int Make(int y) { var c = new C(y); return y; }", "proven")]
    // A local function that captures nothing runs from its parameters alone.
    [TestCase("public static int Target(int x) { return Twice(x); [ZeroAllocations] static int Twice(int y) => y * 2; }", "proven")]
    [TestCase("public static int Target(int x) { return Times(x); [ZeroAllocations] int Times(int y) => y * x; }", "unknown")]
    public async Task ConstructorsAndLocalFunctionsAreLowered(string members, string expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(members);
        var preparation = Prepare("public sealed class C { " + members + " }");
        var outcome = preparation.EffectClaims.Any(static claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ContractKind == WorkerEffectContractKind.EnforcePure))
            ? (await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome
            : (await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome;
        if (preparation.EffectClaims.Length > 1)
        {
            foreach (var result in ImmutableArray.Create(
                await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken),
                await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)))
            { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        }
        Assert.That(outcome switch { ProvenOutcome => "proven", RefutedOutcome => "refuted", _ => "unknown" }, Is.EqualTo(expected));
    }

    // A static read runs no code when the type has no initializer, but it reads
    // ambient state, which observable purity excludes.
    [Test]
    public async Task StaticReadsAreSafeButNotPure()
    {
        var safe = Prepare("public static class C { public static int State { get; set; } " +
            "[DoesNotThrow, ZeroAllocations] public static int Target() => State; }");
        var impure = Prepare("public static class C { public static int State; [EnforcePure] public static int Target() => State; }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(safe, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(safe, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(impure, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        }
    }

    // A call that reads ambient state or is nondeterministic is not pure.
    [TestCase("System.DateTime.Now.Ticks")]
    [TestCase("System.Environment.TickCount")]
    public async Task AmbientCallsAreNotPure(string expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(
            Prepare("public static class C { [EnforcePure] public static long Target() => " + expression + "; }"), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(purity.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    private static CompilerCallablePreparation Prepare(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; " + type);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.EffectClaims.Length != 0));
    }
}
