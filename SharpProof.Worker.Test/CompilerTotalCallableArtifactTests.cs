using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerTotalCallableArtifactTests
{
    internal const string DiamondSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x, bool choose, int unused) {
                Contract.Requires(x >= -10);
                Contract.Ensures(Contract.Result<int>() == (choose ? unchecked(Contract.Old(x) + 1) : unchecked(Contract.Old(x) - 1)) && x == Contract.Result<int>());
                Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
                if (choose) x = unchecked(x + 1); else x = unchecked(x - 1);
                return x;
            }
        }
        """;

    [Test]
    public async Task ProducerRoundTripEnrollsOwnedIrAndReplaysEveryCanonicalEntry()
    {
        var artifact = CreateArtifact(DiamondSource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single();
        var total = preparation.Total!;
        Assert.That(total, Is.Not.Null);
        Assert.That(total.Program.Factory, Is.Not.SameAs(preparation.Factory));
        Assert.That(total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Ensures).Select(clause => clause.ClaimId),
            Is.EqualTo(roundTrip.Manifest.Claims.Select(claim => claim.ClaimId)));
        Assert.That(total.Clauses.Single(clause => clause.Kind == CompilerContractKind.Requires).AssumptionId,
            Is.EqualTo(roundTrip.Manifest.Callables.Single().Assumptions.Single().Id));
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(candidate.CallableId, Is.EqualTo(preparation.Entry.CallableId));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        // Mutating the wire DTO after decoding cannot alter the owned program.
        roundTrip.Callables.Single().Total!.Graph.Roots = [];
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(total.Parameters.Select(parameter => parameter.Entry)));
        Assert.That(refuted.EntryModel, Has.Count.EqualTo(3));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase("sbyte", 8, true)]
    [TestCase("byte", 8, false)]
    [TestCase("short", 16, true)]
    [TestCase("ushort", 16, false)]
    [TestCase("int", 32, true)]
    [TestCase("uint", 32, false)]
    [TestCase("long", 64, true)]
    [TestCase("ulong", 64, false)]
    public async Task AllIntegerWidthsComeFromTheTypedGraph(string type, int width, bool hasSign)
    {
        var preparation = RoundTrip($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static {{type}} Target({{type}} x) {
                    Contract.Ensures(Contract.Result<{{type}}>() == Contract.Old(x));
                    return x;
                }
            }
            """);
        var total = preparation.Total!;
        var info = total.Program.Factory.GetTypeInfo(total.Program.Factory.GetVariableInfo(total.Parameters[0].Entry).Type);
        Assert.That(info.Width, Is.EqualTo(width));
        Assert.That(info.Signed, Is.EqualTo(hasSign));
        Assert.That(total.Program.Factory.GetVariableInfo(total.Result!.Value).Type, Is.EqualTo(info.Id));
        Assert.That((await Check(preparation, 0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task LegacyAdmissionFailureDoesNotSuppressFullUlongTypedCandidate()
    {
        var preparation = RoundTrip("""
            using SharpProof.Attributes;
            public static class Subject {
                public static ulong Target(ulong x) {
                    Contract.Requires(x == 18446744073709551615UL);
                    Contract.Ensures(Contract.Result<ulong>() == 0UL);
                    return unchecked(x + 1UL);
                }
            }
            """);
        Assert.That(preparation.IsSuccess, Is.False);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That((await Check(preparation, 0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task BooleanAndVoidReturnCandidatesRemainTyped()
    {
        var boolean = RoundTrip("""
            using SharpProof.Attributes;
            public static class Subject { public static bool Target(bool x) {
                Contract.Ensures(Contract.Result<bool>() == !Contract.Old(x)); return !x;
            } }
            """);
        Assert.That((await Check(boolean, 0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var empty = RoundTrip("""
            using SharpProof.Attributes;
            public static class Subject { public static void Target(int x) {
                Contract.Ensures(x == Contract.Old(x));
            } }
            """);
        Assert.That(empty.Total!.Result, Is.Null);
        Assert.That((await Check(empty, 0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task NormalReturnsExcludeDivisionThrowPathsAndGuardSelectedClauses()
    {
        var preparation = RoundTrip("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Requires(d == 0 || d == 1);
                Contract.Ensures(d == 0 ? Contract.Result<int>() == 0 : Contract.Result<int>() == 10 / d);
                return 10 / d;
            } }
            """);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions),
            Has.Some.TypeOf<IrThrowInstruction>());
        var checkedClause = await Check(preparation, 0);
        Assert.That(checkedClause.Outcome, Is.TypeOf<ProvenOutcome>(), checkedClause.Reason.ToString());
    }

    [Test]
    public async Task UnsafeEnsuresKeepsItsSafetyRootThroughTheArtifact()
    {
        var preparation = RoundTrip("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int d) {
                Contract.Ensures(Contract.Result<int>() / d == 0); return 0;
            } }
            """);
        var check = await Check(preparation, 0);
        Assert.That(check.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(check.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
    }

    [Test]
    public void LegacyGraphBytesAndFailureRowsRemainUnchanged()
    {
        var compilation = TestCompilation.Create("LegacyPreservation", ("Subject.cs", DiamondSource));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        var withTotal = CompilerLoweredArtifact.Encode(preparation);
        var legacy = CompilerLoweredArtifact.Encode(preparation with { Total = null });
        Assert.That(JsonSerializer.Serialize(withTotal.Graph, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(legacy.Graph, WorkerProtocolJson.SharedOptions)));
        withTotal.Total = null;
        Assert.That(JsonSerializer.Serialize(withTotal, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(legacy, WorkerProtocolJson.SharedOptions)));
    }

    [TestCase("Subject.cs")]
    [TestCase("")]
    [TestCase("/project/Subject.cs")]
    public void TotalOperationDocumentsUseCapturedTreeIdentity(string document)
    {
        var compilation = TestCompilation.Create("DocumentIdentity", (document, DiamondSource));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var total = artifact.Callables.Single().Total;
        Assert.That(total, Is.Not.Null, "A supported source document lost candidate admission.");
        Assert.That(total!.Graph.Operations.Where(operation => operation.SourceSpan != null).Select(operation => operation.SourceSpan!.Document),
            Is.All.EqualTo(artifact.Compilation.SyntaxTrees.Single().Path));
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        Assert.That(prepared.Single().Total, Is.Not.Null);
    }

    [Test]
    public void SameSpanTreesKeepDistinctCapturedDocumentsAndMappedReporting()
    {
        var source = "#line 100 \"mapped.cs\"\n" + DiamondSource;
        var compilation = TestCompilation.Create("MultipleDocuments", ("shared.cs", source),
            ("shared.cs", source.Replace("class Subject", "class Another", StringComparison.Ordinal)));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var documents = artifact.Callables.Select(callable => callable.Total!.Graph.Operations[callable.Total.Clauses[1].Operation].SourceSpan!).ToArray();
        Assert.That(documents.Select(span => span.Document), Is.EquivalentTo(artifact.Compilation.SyntaxTrees.Select(tree => tree.Path)));
        Assert.That(documents.Select(span => span.Start).Distinct().Count(), Is.EqualTo(1));
        Assert.That(artifact.Manifest.Claims.Select(claim => claim.Location.Path), Is.All.EqualTo("mapped.cs"));
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        Assert.That(prepared, Has.Length.EqualTo(2));
        Assert.That(prepared.Select(callable => callable.Total), Is.All.Not.Null);
    }

    [Test]
    public void OversizedExactSourceOmitsTotalWithoutInvalidatingLegacyArtifact()
    {
        var statements = string.Concat(Enumerable.Repeat("x = 0;\n", CompilerPreparedBody.MaximumInstructions + 1));
        var artifact = CreateArtifact($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(Contract.Result<int>() == 0);
                {{statements}}
                return x;
            } }
            """);
        var row = artifact.Callables.Single();
        Assert.That(row.Total, Is.Null, "Oversized exact lowering escaped producer admission.");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        Assert.That(prepared.Single().FailureReason, Is.EqualTo(row.FailureReason));
    }

    [Test]
    public void ForeignDecodedCallablePreparationCannotEnroll()
    {
        var first = RoundTrip(DiamondSource);
        var second = RoundTrip(DiamondSource.Replace("class Subject", "class Another", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(new Action(() => PassiveCallableArtifactAdapter.Enroll(first with { Total = second.Total })));
    }

    [TestCase("assume")]
    [TestCase("loop")]
    [TestCase("call")]
    [TestCase("attribute")]
    public void UnsupportedSourceRemainsClosed(string kind)
    {
        var body = kind switch
        {
            "assume" => "Contract.Assume(x > 0); Contract.Ensures(Contract.Result<int>() == x); return x;",
            "loop" => "Contract.Ensures(Contract.Result<int>() == x); while (x < 0) x++; return x;",
            "call" => "Contract.Ensures(Contract.Result<int>() == x); return System.Math.Abs(x);",
            _ => "return x;"
        };
        var annotation = kind == "attribute" ? "[return: Positive]" : "";
        var preparation = RoundTrip($$"""
            using SharpProof.Attributes;
            public static class Subject { {{annotation}} public static int Target(int x) { {{body}} } }
            """);
        Assert.That(preparation.Total, Is.Null);
        Assert.That(PassiveCallableArtifactAdapter.Enroll(preparation), Is.Null);
    }

    [TestCase("mode")]
    [TestCase("roots")]
    [TestCase("alias")]
    [TestCase("result-alias")]
    [TestCase("role-swap")]
    [TestCase("type")]
    [TestCase("claim")]
    [TestCase("assumption")]
    [TestCase("site-swap")]
    [TestCase("root-swap")]
    [TestCase("assume-kind")]
    [TestCase("missing-program")]
    [TestCase("missing-span")]
    [TestCase("missing-roles")]
    [TestCase("foreign-graph")]
    public void MalformedTotalEvidenceRejectsTheWholePreparedArtifact(string mutation)
    {
        var artifact = CreateArtifact(DiamondSource);
        var total = artifact.Callables.Single().Total!;
        var parameter = total.Parameters[0];
        switch (mutation)
        {
            case "mode":
                total.Graph.Semantics = IrExecutionSemantics.Legacy;
                break;
            case "roots":
                total.Graph.Roots = [];
                break;
            case "alias":
                parameter.Old = parameter.Current;
                break;
            case "result-alias":
                total.Result = parameter.Entry;
                break;
            case "role-swap":
                (parameter.Entry, parameter.Current) = (parameter.Current, parameter.Entry);
                break;
            case "type":
                total.Graph.Variables[parameter.Old].Type = total.Graph.Variables[total.Parameters[1].Entry].Type;
                break;
            case "claim":
                total.Clauses[1].ClaimId = total.Clauses[2].ClaimId;
                break;
            case "assumption":
                total.Clauses[0].AssumptionId = "foreign";
                break;
            case "site-swap":
                (total.Clauses[1].Operation, total.Clauses[2].Operation) = (total.Clauses[2].Operation, total.Clauses[1].Operation);
                break;
            case "root-swap":
                (total.Clauses[1].ValueRoot, total.Clauses[2].ValueRoot) = (total.Clauses[2].ValueRoot, total.Clauses[1].ValueRoot);
                break;
            case "assume-kind":
                total.Clauses[0].Kind = CompilerContractKind.Assume;
                break;
            case "missing-program":
                total.Graph.HasProgram = false;
                break;
            case "missing-span":
                total.Graph.Operations[total.Clauses[0].Operation].SourceSpan = null;
                break;
            case "missing-roles":
                total.Parameters = null!;
                break;
            case "foreign-graph":
                total.Graph = CreateArtifact(DiamondSource.Replace("int x, bool choose, int unused", "long x, bool choose, long unused", StringComparison.Ordinal)
                    .Replace("Result<int>", "Result<long>", StringComparison.Ordinal).Replace("static int Target", "static long Target", StringComparison.Ordinal)).Callables.Single().Total!.Graph;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    internal static CompilerManifestArtifact CreateArtifact(string source)
    {
        var compilation = TestCompilation.Create("TotalArtifact", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }

    private static CompilerCallablePreparation RoundTrip(string source)
    {
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        return preparations.Single();
    }

    private static async Task<PassiveCallableCheckResult> Check(CompilerCallablePreparation preparation, int ordinal)
    {
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        return await solver.VerifyEnsuresAsync(ordinal);
    }
}
