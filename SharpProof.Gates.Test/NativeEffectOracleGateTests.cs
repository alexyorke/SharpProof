using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeEffectOracleGateTests
{
    [Test]
    public async Task RecursiveSourceSummariesAreObservedWithoutPublishingProofs()
    {
        var document = Document("""
            public static class C {
                public static int Target(int x) => Helper(x);
                static int Helper(int x) => Target(x);
            }
            """);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.ReachableSourceBodyCount, Is.EqualTo(2));
        Assert.That(report.ReachableSourceMayDivergeCount, Is.EqualTo(2));
        Assert.That(report.ReachableSourceUnknownEffectCount, Is.EqualTo(2));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [TestCase("int x", "return 10 / x;")]
    [TestCase("ulong x", "return (int)(10UL / x);")]
    [TestCase("bool x", "return 10 / (x ? 0 : 1);")]
    [TestCase("int[] x", "return x.Length;")]
    public async Task SourceProbeRoundTripAndCompiledRuntimeConfirmConcreteWitnesses(string parameter, string body)
    {
        var document = Document("public static class C { public static int Target(" + parameter + ") { " + body + " } }");
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(1));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DelegateEmissionModesDoNotManufactureExceptionWitnesses(bool escapes)
    {
        var source = escapes
            ? "public static class C { public static System.Func<string> Target(int x) { return new System.Func<string>(((string)null).Trim); } }"
            : "public static class C { public static int Target(int x) { System.Func<string> action = new System.Func<string>(((string)null).Trim); return x; } }";
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(Document(source), CancellationToken.None),
            ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(escapes ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(escapes ? "Confirmed" : "NotRun"));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [Test]
    public async Task PinnedUniverseIsExhaustivelyComparedWithoutOracleContradictions()
    {
        var root = RepositoryLayout.FindRoot();
        var report = await NativeEffectOracleGate.RunAsync(root);
        Assert.That(report.UniverseMethodCount, Is.EqualTo(200));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(report.UniverseMethodCount));
        Assert.That(report.Exhaustive, Is.True);
        Assert.That(report.UniverseSha256, Is.EqualTo("A35CBCBDE4CF956E35790BC55A9E381913CBDFDABF725BAF7ED99706028D29C3"));
        Assert.That(report.Rows.Select(row => row.MethodId), Is.EquivalentTo(OpenSourceCorpusCatalog.Load(root).Methods.Select(method => method.Id)));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
        Assert.That(report.Rows.Single(row => row.MethodId == "OSS0199").NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        await TestContext.Progress.WriteLineAsync($"Exception oracle: {report.CheckedMethodCount} methods; {report.NativeProven} proven; " +
            $"{report.NativeRefuted} refuted; {report.RuntimeWitnesses} runtime confirmations; {report.WallSeconds:F1}s.");
    }

    [Test]
    public async Task SimpleSourceProofSurvivesArtifactRoundTrip()
    {
        var document = Document("public static class C { public static int Target(int x) { return x; } }");
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None),
            ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.NativeProven, Is.EqualTo(1));
        Assert.That(report.RuntimeWitnesses, Is.Zero);
    }

    [Test]
    public async Task UnknownBodiesAreCountedInsteadOfExcludedFromCoverage()
    {
        var document = Document("public static class C { public static int Target(int x) { System.Console.WriteLine(x); return x; } }");
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None),
            ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.CheckedMethodCount, Is.EqualTo(1));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(report.NativeUnknownReasons.Values.Sum(), Is.EqualTo(1));
        Assert.That(report.RuntimeWitnesses, Is.Zero);
    }

    [Test]
    public void DuplicateCoverageAndOracleContradictionsCannotPass()
    {
        var row = Row("one", WorkerClaimOutcome.Proven);
        Assert.Throws<ArgumentException>(new Action(() => NativeEffectOracleGate.Summarize("test", 2, [row, row], 0)));
        var report = NativeEffectOracleGate.Summarize("test", 1,
            [row with { NativeOutcome = WorkerClaimOutcome.Refuted, RuntimeOracle = "Contradiction" }], 0);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.NativeRefuted, Is.EqualTo(1));
        Assert.That(report.RuntimeContradictions, Is.EqualTo(1));
    }

    private static NativeEffectOracleRow Row(string id, WorkerClaimOutcome native)
    {
        return new(id, id, native,
            native == WorkerClaimOutcome.Unknown ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None,
            native != WorkerClaimOutcome.Unknown, false, null, "NotRun");
    }

    private static OpenSourceCorpusDocument Document(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var span = method.GetLocation().GetLineSpan();
        return new(2, [], [new("test", "sample.cs", "test", source)],
            [new("sample", "test", "sample.cs", span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                "test", "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported)]);
    }

    [TestCase("return x;", WorkerClaimOutcome.Proven)]
    [TestCase("new object(); return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("int[] values = new int[2]; return values.Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("int[] values = new int[] { 1, -2 }; return values[0] + values[1];", WorkerClaimOutcome.Refuted)]
    [TestCase("short[] values = [1, -2]; return values[0] + values[1];", WorkerClaimOutcome.Refuted)]
    [TestCase("System.Console.WriteLine(x); return x;", WorkerClaimOutcome.Unknown)]
    [TestCase("System.Threading.Monitor.Enter(null); return x;", WorkerClaimOutcome.Unknown)]
    [TestCase("object value = x; return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("object value = new object(); return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("System.Func<int, int> action = new System.Func<int, int>(Target); return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("System.Func<string> action = new System.Func<string>(((string)null).Trim); return x;", WorkerClaimOutcome.Unknown)]
    [TestCase("return string.Concat(x == 0 ? \"a\" : \"b\", \"c\").Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("return string.Concat(\"\", x == 0 ? \"a\" : \"b\").Length;", WorkerClaimOutcome.Proven, "PotentialAllocationOpcode")]
    [TestCase("return ((x == 0 ? \"a\" : \"b\") + \"c\").Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("return (\"\" + (x == 0 ? \"a\" : \"b\")).Length;", WorkerClaimOutcome.Proven, "PotentialAllocationOpcode")]
    public async Task AllocationOracleMeasuresDecodedArtifactsAndKeepsOracleGapsVisible(string body, WorkerClaimOutcome outcome,
        string allocationIlOracle = "NoReachableAllocationOpcode")
    {
        var document = Document("public static class C { public static int Target(int x) { " + body + " } }");
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);
        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1, allocations: true);
        Assert.That(report.ContractKind, Is.EqualTo("ZeroAllocations"));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(outcome));
        var row = report.Rows.Single();
        Assert.That(row.RuntimeOracle, Is.EqualTo(outcome == WorkerClaimOutcome.Unknown ? "NotRun" : "Confirmed"));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(outcome == WorkerClaimOutcome.Unknown ? 0 : 1));
        Assert.That(row.RuntimeChecks, Is.EqualTo(outcome == WorkerClaimOutcome.Unknown ? 0 : 1));
        if (outcome == WorkerClaimOutcome.Proven)
        {
            Assert.That(row.AllocatedBytes, Is.Zero);
            Assert.That(row.AllocationIlOracle, Is.EqualTo(allocationIlOracle));
        }
        else if (outcome == WorkerClaimOutcome.Refuted)
        {
            Assert.That(row.AllocatedBytes, Is.GreaterThan(0));
            Assert.That(row.AllocationIlOracle, Is.EqualTo("PotentialAllocationOpcode"));
        }
    }

    [Test]
    public async Task PinnedAllocationUniverseIsExhaustivelyMeasured()
    {
        var root = RepositoryLayout.FindRoot();
        var report = await NativeEffectOracleGate.RunAsync(root, allocations: true);
        Assert.That(report.ContractKind, Is.EqualTo("ZeroAllocations"));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(200));
        Assert.That(report.Exhaustive, Is.True);
        Assert.That(report.UniverseSha256, Is.EqualTo("A35CBCBDE4CF956E35790BC55A9E381913CBDFDABF725BAF7ED99706028D29C3"));
        Assert.That(report.Rows.Select(row => row.MethodId), Is.EquivalentTo(OpenSourceCorpusCatalog.Load(root).Methods.Select(method => method.Id)));
        Assert.That(report.Passed, Is.True);
        var retained = report.Rows.Single(row => row.MethodId == "OSS0199");
        Assert.That(retained.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(retained.RuntimeChecks, Is.EqualTo(2));
        Assert.That(retained.AllocatedBytes, Is.Zero);
        Assert.That(report.RuntimeContradictions, Is.Zero);
        await TestContext.Progress.WriteLineAsync($"Allocation oracle: {report.CheckedMethodCount} methods; {report.NativeProven} proven; " +
            $"{report.NativeRefuted} refuted; {report.RuntimeWitnesses} runtime confirmations; {report.WallSeconds:F1}s.");
    }

    [TestCase("public static class C", "", "", "Confirmed", 1)]
    [TestCase("public static class C<T>", "", "", "Confirmed", 2)]
    [TestCase("public static class C", "<T>", "", "Confirmed", 2)]
    [TestCase("public static class C", "", "SharpProof.Attributes.Contract.Requires(false);", "NoFeasibleEntryWitness", 0)]
    public async Task AllocationOracleDistinguishesEmptyModelsFromVacuousEntries(string owner, string generic, string requires,
        string expected, int checks)
    {
        var document = Document(owner + " { public static int Target" + generic + "() { " + requires + " return 1; } }");
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document,
            CancellationToken.None, allocations: true), ["sample"], RepositoryLayout.FindRoot(), "test", 1, allocations: true);
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(expected));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(checks));
    }

    [TestCase(nameof(NoAllocation), false)]
    [TestCase(nameof(FilteredHandlerAllocation), true)]
    public void IlOracleIncludesExceptionFilterHandlers(string name, bool expected)
    {
        var method = typeof(NativeEffectOracleGateTests).GetMethod(name,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.EqualTo(expected));
    }

    private static int NoAllocation(int x)
    { return x; }

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
    public async Task AllocationOracleInvokesExactScalarArgumentTypesWithoutHarnessAllocations(string type)
    {
        var document = Document("public static class C { public static " + type + " Target(" + type + " x) { return x; } }");
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document,
            CancellationToken.None, allocations: true), ["sample"], RepositoryLayout.FindRoot(), "test", 1, allocations: true);
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(report.Rows.Single().AllocatedBytes, Is.Zero);
    }

    [Test]
    public async Task IndependentSourceOracleRejectsAnIncorrectAllocationProof()
    {
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(Document(
            "public static class C { public static int Target(int x) { new object(); return x; } }"),
            CancellationToken.None, allocations: true);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var target = discovery.Targets.Values.Single();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var preparation = preparations.Single();
        var evidence = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        using var oracle = new NativeAllocationWitnessOracle(compilation);
        var observation = oracle.Check(target.Method, preparation.Total!, evidence, WorkerClaimOutcome.Proven, CancellationToken.None);
        Assert.That(observation.RuntimeOracle, Is.EqualTo("Contradiction"));
        Assert.That(observation.AllocatedBytes, Is.GreaterThan(0));
    }

    private static object? FilteredHandlerAllocation(int x)
    {
        try
        { return 1 / x == 0 ? null : null; }
        catch (DivideByZeroException) when (x == 0) { return new object(); }
    }

    [TestCase("var y = x; y++; return y;", WorkerClaimOutcome.Proven)]
    [TestCase("new object(); return x;", WorkerClaimOutcome.Proven)]
    [TestCase("int[] values = new int[2]; return values.Length;", WorkerClaimOutcome.Proven)]
    [TestCase("int[] values = new int[] { 1, -2 }; return values[0] + values[1];", WorkerClaimOutcome.Proven)]
    [TestCase("short[] values = [1, -2]; return values[0] + values[1];", WorkerClaimOutcome.Proven)]
    [TestCase("System.Console.WriteLine(x); return x;", WorkerClaimOutcome.Unknown)]
    [TestCase("System.Threading.Monitor.Enter(null); return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("lock ((object)null) { x++; } return x;", WorkerClaimOutcome.Refuted)]
    [TestCase("object value = x; return x;", WorkerClaimOutcome.Proven)]
    [TestCase("object value = new object(); return x;", WorkerClaimOutcome.Proven)]
    [TestCase("System.Func<int, int> action = new System.Func<int, int>(Target); return x;", WorkerClaimOutcome.Proven)]
    [TestCase("System.Func<string> action = new System.Func<string>(((string)null).Trim); return x;", WorkerClaimOutcome.Proven)]
    [TestCase("return string.Concat(x == 0 ? \"a\" : \"b\", \"c\").Length;", WorkerClaimOutcome.Proven)]
    [TestCase("return ((x == 0 ? \"a\" : \"b\") + \"c\").Length;", WorkerClaimOutcome.Proven)]
    public async Task PurityOracleUsesProductionArtifactRoundTrip(string body, WorkerClaimOutcome expected)
    {
        var document = Document("public static class C { public static int Target(int x) { " + body + " } }");
        var report = await NativeEffectOracleGate.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document,
            CancellationToken.None, purity: true), ["sample"], RepositoryLayout.FindRoot(), "test", 1, purity: true);
        Assert.That(report.ContractKind, Is.EqualTo("EnforcePure"));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(expected));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("PurityOracleNotRun"));
    }

    [Test]
    public async Task PinnedPurityUniverseIncludesEveryMethod()
    {
        var root = RepositoryLayout.FindRoot();
        var report = await NativeEffectOracleGate.RunAsync(root, purity: true);
        Assert.That(report.CheckedMethodCount, Is.EqualTo(200));
        Assert.That(report.Exhaustive, Is.True);
        Assert.That(report.Rows.Select(row => row.MethodId), Is.EquivalentTo(OpenSourceCorpusCatalog.Load(root).Methods.Select(method => method.Id)));
        Assert.That(report.UniverseSha256, Is.EqualTo("A35CBCBDE4CF956E35790BC55A9E381913CBDFDABF725BAF7ED99706028D29C3"));
        Assert.That(report.Passed, Is.True);
        await TestContext.Progress.WriteLineAsync($"Purity oracle: {report.CheckedMethodCount} methods; {report.NativeProven} proven; {report.NativeRefuted} refuted.");
    }
}
