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
    [TestCase("remove-one")]
    [TestCase("remove-all")]
    [TestCase("callee")]
    [TestCase("ordinal")]
    [TestCase("site")]
    public void CoherentCallPreconditionMutationsAreRejected(string mutation)
    {
        var artifact = CreateArtifact(GoldenTest.Load("worker", "reachable-call-precondition-artifact").Source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        var original = prepared.Single(owner => owner.Total?.CallPreconditions.Length == 2).Total!;
        var first = original.CallPreconditions[0];
        var second = original.CallPreconditions[1];
        var modified = mutation switch
        {
            "remove-one" => original with { CallPreconditions = [first] },
            "remove-all" => original with { CallPreconditions = [] },
            "callee" => original with { CallPreconditions = [first, second with { CalleeIdentity = "unrelated::M:Other.Helper" }] },
            "ordinal" => original with { CallPreconditions = [first, second with { ClauseOrdinal = 4095 }] },
            "site" => original with
            {
                CallPreconditions = [first, second with { ClauseSite = original.Program.Blocks
                .SelectMany(block => block.Instructions).Single(instruction => instruction.Id == second.Instruction).Operation }]
            },
            _ => throw new AssertionException("Unknown mutation.")
        };
        var encoded = CompilerTotalCallableArtifactCodec.Encode(modified)!;
        artifact.Callables.Single(owner => owner.Total?.CallPreconditions.Length == 2).Total = encoded;
        var variables = encoded.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.Old })
            .Concat(encoded.Result == -1 ? Array.Empty<int>() : [encoded.Result]).Distinct().OrderBy(index => index).ToArray();
        var operations = encoded.Clauses.Select(clause => clause.Operation)
            .Concat(encoded.CallPreconditions.Select(call => call.ClauseSite)).Distinct().OrderBy(index => index).ToArray();
        Assert.DoesNotThrow(new Action(() => PortableIrGraphCodec.Decode(encoded.Graph, variables, operations)));
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("index")]
    [TestCase("callee")]
    [TestCase("ordinal")]
    [TestCase("site")]
    [TestCase("root")]
    [TestCase("abstraction")]
    public void MalformedCallPreconditionMetadataIsRejected(string mutation)
    {
        var artifact = CreateArtifact(GoldenTest.Load("worker", "reachable-call-precondition-artifact").Source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        var total = artifact.Callables.Single(callable => callable.Total?.CallPreconditions.Length == 2).Total!;
        var row = total.CallPreconditions[0];
        switch (mutation)
        {
            case "missing":
                total.CallPreconditions = [];
                break;
            case "duplicate":
                total.CallPreconditions[1].InstructionIndex = row.InstructionIndex;
                break;
            case "index":
                row.InstructionIndex = -1;
                break;
            case "callee":
                row.CalleeIdentity = "";
                break;
            case "ordinal":
                row.ClauseOrdinal = -1;
                break;
            case "site":
                row.ClauseSite = -1;
                break;
            case "root":
                row.ValueRoot = row.SafeRoot;
                break;
            case "abstraction":
                total.IsBodyAbstraction = true;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [Test]
    public void NativeStringArtifactRejectsNonCanonicalComparisonMutation()
    {
        var artifact = CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static string Target(string left, string right) {
                    Contract.Ensures(Contract.Result<string>() != null);
                    return string.Concat(left, right);
                }
            }
            """);
        var graph = artifact.Callables.Single().Total!.Graph;
        var comparison = graph.Terms.Single(term => term.Kind == IrTermKind.Binary &&
            (IrBinaryOperator)term.A == IrBinaryOperator.NotEqual);
        comparison.C = comparison.B;
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    private const string EmptyArraySource = """
        using System;
        using SharpProof.Attributes;
        public static class Subject {
            public static int[] Target() {
                Contract.Ensures(Contract.Result<int[]>() != null);
                return Array.Empty<int>();
            }
        }
        """;

    [Test]
    public void EmptyArrayTermSurvivesArtifactRoundTrip()
    {
        var preparation = RoundTrip(EmptyArraySource);
        var total = preparation.Total!;
        Assert.That(total, Is.Not.Null);
        var result = new IrProgramInterpreter(total.Program.Factory).Execute(total.Program,
            new Dictionary<IrVarId, IrValue>());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Sequence));
        Assert.That(result.ReturnValue.Elements, Is.Empty);
        Assert.That(result.ConsumedApproximation, Is.False);
    }

    [Test]
    public void EmptyArrayTermRejectsNonSequenceArtifactType()
    {
        var artifact = CreateArtifact(EmptyArraySource);
        var graph = artifact.Callables.Single().Total!.Graph;
        var empty = graph.Terms.Single(term => term.Kind == IrTermKind.EmptyArray);
        empty.Type = Array.FindIndex(graph.Types, type => type.Kind == IrTypeKind.Boolean);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    private const string EntryOnlySource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static ulong Target(ulong x) {
                Contract.Requires(x == ulong.MaxValue);
                Contract.Ensures(x.ToString() != null);
                System.Console.WriteLine(x);
                return x;
            }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task EntryEvidenceSurvivesUnsupportedBodyAndPostcondition(bool contradictory)
    {
        var source = contradictory
            ? EntryOnlySource.Replace("x == ulong.MaxValue", "x == ulong.MaxValue && x == 0UL", StringComparison.Ordinal)
            : EntryOnlySource;
        var preparation = RoundTrip(source);
        Assert.That(preparation.Total, Is.Null);
        var entry = preparation.TotalEntry!;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
        Assert.That(entry.Clauses.Select(clause => clause.Kind), Is.All.EqualTo(CompilerContractKind.Requires));
        Assert.That(entry.Clauses.Single().AssumptionId, Is.EqualTo(preparation.Entry.Assumptions.Single().Id));
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(contradictory ? CallableEntryFeasibilityKind.Contradictory : CallableEntryFeasibilityKind.Feasible));
        if (contradictory)
        { Assert.That(result.UsedAssumptionIds, Is.EquivalentTo(preparation.Entry.Assumptions.Select(assumption => assumption.Id))); }
    }

    [TestCase("body")]
    [TestCase("ensures")]
    [TestCase("id")]
    [TestCase("count")]
    [TestCase("current")]
    [TestCase("result")]
    [TestCase("mode")]
    [TestCase("span")]
    public void EntryPayloadRejectsForeignEvidence(string mutation)
    {
        var artifact = CreateArtifact(EntryOnlySource);
        var entry = artifact.Callables.Single().TotalEntry!;
        switch (mutation)
        {
            case "body":
                entry.Graph.HasProgram = true;
                break;
            case "ensures":
                entry.Clauses[0].Kind = CompilerContractKind.Ensures;
                break;
            case "id":
                entry.Clauses[0].AssumptionId = "foreign";
                break;
            case "count":
                entry.Clauses = [];
                entry.Graph.Roots = [];
                break;
            case "current":
                (entry.Parameters[0].Entry, entry.Parameters[0].Current) = (entry.Parameters[0].Current, entry.Parameters[0].Entry);
                break;
            case "result":
                entry.Result = entry.Parameters[0].Entry;
                break;
            case "mode":
                entry.Graph.Semantics = IrExecutionSemantics.Legacy;
                break;
            case "span":
                entry.Graph.Operations[entry.Clauses[0].Operation].SourceSpan = null;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [Test]
    public async Task MutatingEntryDtoAfterDecodeCannotChangeNativeQuery()
    {
        var artifact = CreateArtifact(EntryOnlySource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        roundTrip.Callables.Single().TotalEntry!.Graph.Roots = [];
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparations.Single(), new WorkerBudgets(), CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
    }

    [TestCase("expression")]
    [TestCase("placement")]
    public async Task UnsupportedRequiresCannotEstablishEntry(string scenario)
    {
        var source = scenario == "expression"
            ? EntryOnlySource.Replace("x == ulong.MaxValue", "x.ToString() != null", StringComparison.Ordinal)
            : EntryOnlySource.Replace("Contract.Requires(x == ulong.MaxValue);", "System.Console.WriteLine(x); Contract.Requires(x == ulong.MaxValue);", StringComparison.Ordinal);
        var preparation = RoundTrip(source);
        Assert.That(preparation.TotalEntry, Is.Null);
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Unknown));
    }

    [Test]
    public async Task BodyAssumptionsCannotMakeEntryContradictory()
    {
        var preparation = RoundTrip(EntryOnlySource.Replace("System.Console.WriteLine(x);", "Contract.Assume(false);", StringComparison.Ordinal));
        Assert.That(preparation.Entry.Assumptions, Has.Length.EqualTo(2));
        Assert.That(preparation.TotalEntry!.Clauses, Has.Length.EqualTo(1));
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(result.UsedAssumptionIds, Is.Empty);
    }

    [Test]
    public void IndependentEntryHonorsCancellation()
    {
        var preparation = RoundTrip(EntryOnlySource);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), cancellation.Token)));
    }

    [Test]
    public async Task IndependentEntryCannotIgnoreQueryResourceLimit()
    {
        var preparation = RoundTrip(EntryOnlySource);
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets { QueryRlimit = 1 }, CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

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

    [TestCase(false)]
    [TestCase(true)]
    public void LegacyGraphBytesAndFailureRowsRemainUnchanged(bool sourceAssume)
    {
        var compilation = TestCompilation.Create("LegacyPreservation", ("Subject.cs", sourceAssume ? WorkerVcSourceAssumeTests.Source : DiamondSource));
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

    [Test]
    public void OptionalLoopEvidencePreservesEveryLegacyRowAndGraphByte()
    {
        var compilation = TestCompilation.Create("LegacyLoopPreservation", ("Subject.cs", WorkerVcLoopTests.LoopSource));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        Assert.That(preparation.Total, Is.Not.Null);
        var withTotal = CompilerLoweredArtifact.Encode(preparation);
        var legacy = CompilerLoweredArtifact.Encode(preparation with { Total = null });
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
    [TestCase("call")]
    [TestCase("attribute")]
    public void SourceAdmissionDistinguishesUnsupportedBodiesAndTypedAttributes(string kind)
    {
        var body = kind switch
        {
            "assume" => "Contract.Assume(System.Math.Abs(x) > 0); Contract.Ensures(Contract.Result<int>() == x); return x;",
            "call" => "Contract.Ensures(Contract.Result<int>() == x); return System.Math.Abs(x);",
            _ => "return x;"
        };
        var annotation = kind == "attribute" ? "[return: Positive]" : "";
        var preparation = RoundTrip($$"""
            using SharpProof.Attributes;
            public static class Subject { {{annotation}} public static int Target(int x) { {{body}} } }
            """);
        if (kind is "call" or "attribute")
        {
            Assert.That(preparation.Total, Is.Not.Null);
            Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
            Assert.That(PassiveCallableArtifactAdapter.Enroll(preparation), Is.Not.Null);
        }
        else
        {
            Assert.That(preparation.Total, Is.Null);
            Assert.That(PassiveCallableArtifactAdapter.Enroll(preparation), Is.Null);
        }
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

    internal static CompilerManifestArtifact CreateArtifact(string source,
        int maximumExpressionDepth = WorkerBudgets.DefaultMaximumExpressionDepth)
    {
        var compilation = TestCompilation.Create("TotalArtifact", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            maximumExpressionDepth, CancellationToken.None);
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
