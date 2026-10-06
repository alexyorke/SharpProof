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
    [Test]
    public void ClosedSealedCertificateRejectsErrorTypeArguments()
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("CertificateError",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public sealed class G<T> { } public static class C { " +
                "public static void Target(G<Missing> input) { } }")],
            TestMetadataReferences.Platform, TestCompilation.CreateOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS0246"), Is.True);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Target").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single();
        Assert.That(SharpProof.Frontend.CompilerIdentityBridge.IsClosedSealedReferenceType(method.Parameters[0].Type), Is.False);
    }

    [Test]
    public void ClosedSealedTypeCertificatesSurviveCanonicalRoundTrip()
    {
        var artifact = CreateArtifact(NativeAliasingBoundaryTests.DistinctSealedFieldSource);
        var graph = artifact.Callables.Single().Total!.Graph;
        Assert.That(graph.Types.Count(type => type.ClosedSealedReference), Is.EqualTo(3));
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var encoded = CompilerTotalCallableArtifactCodec.Encode(preparations.Single().Total)!;
        Assert.That(encoded.Graph.Types.Select(type => type.ClosedSealedReference), Is.EqualTo(graph.Types.Select(type => type.ClosedSealedReference)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void NonNominalTypesCannotCarryClosedSealedCertificates(int index)
    {
        var artifact = CreateArtifact("using SharpProof.Attributes; public static class C { public static int[] Target(int[] items) { " +
            "Contract.Ensures(Contract.Result<int[]>() == items); return items; } }");
        var graph = artifact.Callables.Single().Total!.Graph;
        Assert.That(index < graph.Types.Length, Is.True);
        graph.Types[index].ClosedSealedReference = true;
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase("public sealed class A<T> { }", "A<T>", false)]
    [TestCase("public sealed class A<T> { }", "A<int>", true)]
    [TestCase("public class A { }", "A", false)]
    [TestCase("public interface A { }", "A", false)]
    public void CompilerCertificateRequiresClosedSealedClassSymbols(string declaration, string typeName, bool expected)
    {
        var artifact = CreateArtifact("using SharpProof.Attributes; " + declaration + " public static class C { public static bool Target<T>(" +
            typeName + " input) { Contract.Ensures(Contract.Result<bool>()); return true; } }");
        var total = artifact.Callables.Single().Total!;
        var type = total.Graph.Variables[total.Parameters[0].Entry].Type;
        Assert.That(total.Graph.Types[type].ClosedSealedReference, Is.EqualTo(expected));
    }

    private const string DisjointInputSource = "using SharpProof.Attributes; public sealed class A { } public sealed class B { } " +
        "public sealed class Other { } public static class C { public static bool Target(A a, B b, Other c, object o, int n) { " +
        "Contract.Ensures(Contract.Result<bool>()); object left = a; object right = b; return left != right || a == null || b == null; } }";

    [Test]
    public async Task DisjointInputsSurviveCanonicalArtifactRoundTrip()
    {
        var artifact = CreateArtifact(DisjointInputSource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Total!.DisjointInputs, Is.EqualTo(new CompilerDisjointInputPair[] { new(0, 1), new(0, 2), new(1, 2) }));
        Assert.That(CompilerTotalCallableArtifactCodec.Encode(preparation.Total)!.DisjointInputs, Is.EqualTo(artifact.Callables.Single().Total!.DisjointInputs));
        Assert.That((await Check(preparation, 0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase("null-row")]
    [TestCase("negative")]
    [TestCase("self")]
    [TestCase("reversed")]
    [TestCase("range")]
    [TestCase("duplicate")]
    [TestCase("unsorted")]
    [TestCase("object")]
    [TestCase("scalar")]
    [TestCase("entry")]
    [TestCase("abstraction")]
    [TestCase("missing-left")]
    [TestCase("missing-right")]
    public void MalformedDisjointInputsRejectThePreparedArtifact(string mutation)
    {
        var artifact = CreateArtifact(DisjointInputSource);
        var total = artifact.Callables.Single().Total!;
        switch (mutation)
        {
            case "null-row":
                total.DisjointInputs = [null!];
                break;
            case "negative":
                total.DisjointInputs = [new(-1, 1)];
                break;
            case "self":
                total.DisjointInputs = [new(0, 0)];
                break;
            case "reversed":
                total.DisjointInputs = [new(1, 0)];
                break;
            case "range":
                total.DisjointInputs = [new(0, 5)];
                break;
            case "duplicate":
                total.DisjointInputs = [new(0, 1), new(0, 1)];
                break;
            case "unsorted":
                total.DisjointInputs = [new(1, 2), new(0, 1)];
                break;
            case "object":
                total.DisjointInputs = [new(0, 3)];
                break;
            case "scalar":
                total.DisjointInputs = [new(0, 4)];
                break;
            case "entry":
                artifact.Callables.Single().TotalEntry!.DisjointInputs = [new(0, 1)];
                break;
            case "abstraction":
                total.IsBodyAbstraction = true;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        if (mutation is "missing-left" or "missing-right")
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var pair = node["callables"]![0]!["total"]!["disjointInputs"]![0]!.AsObject();
            Assert.That(pair.Remove(mutation == "missing-left" ? "left" : "right"), Is.True);
            json = node.ToJsonString();
        }
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyStructuralReturnsAreOnlyAllowedOutsideReachableNonvoidFlow(bool reachable)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        var result = factory.CreateVariable("result", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var operation = factory.CreateOperation("structural-exit");
        if (reachable)
        { builder.Return(entry, operation); }
        else
        {
            builder.Return(entry, operation, factory.Integer(type, 1));
            builder.Return(builder.CreateBlock("unused-cfg-exit"), operation);
        }
        var body = new CompilerTotalCallablePreparation("M:Subject.Root", builder.Build(), [], result, []);
        var dto = CompilerTotalCallableArtifactCodec.Encode(body)!;
        if (reachable)
        {
            Assert.Throws<InvalidDataException>(new Action(() =>
                CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, dto, CancellationToken.None)));
        }
        else
        {
            var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, dto, CancellationToken.None);
            Assert.That(decoded.Body.Program.Blocks, Has.Length.EqualTo(2));
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ShadowDecoderChecksExecutableEvidenceInEveryBlock(bool reachable, bool wrongReturn)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var operation = factory.CreateOperation("block-validation");
        var selected = reachable ? entry : builder.CreateBlock("detached");
        if (!reachable)
        { builder.Return(entry, operation); }
        if (wrongReturn)
        { builder.Return(selected, operation, factory.Integer(factory.GetOrCreateIntegerType(32, true), 1)); }
        else
        {
            builder.Assert(selected, operation, factory.Boolean(false));
            builder.Return(selected, operation);
        }
        var preparation = new CompilerTotalCallablePreparation("M:Subject.Root", builder.Build(), [], null, []);
        var dto = CompilerTotalCallableArtifactCodec.Encode(preparation)!;
        var failure = Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBody(preparation.CallableId, dto, CancellationToken.None)));
        Assert.That(failure!.Message, Is.EqualTo(wrongReturn
            ? "The Total return type disagrees with its canonical result."
            : "The Total source program contains unsupported executable evidence."));
    }

    [TestCase("clauses")]
    [TestCase("null-clauses")]
    [TestCase("effects")]
    [TestCase("null-effects")]
    [TestCase("exceptions")]
    [TestCase("null-exceptions")]
    [TestCase("abstraction")]
    [TestCase("entry-effects")]
    [TestCase("markers")]
    [TestCase("null-markers")]
    [TestCase("owner")]
    public void ShadowDecoderRejectsClaimedMetadataAndMalformedMarkers(string mutation)
    {
        var (ownerId, dto) = CreateShadowBodyArtifact();
        switch (mutation)
        {
            case "clauses":
                dto.Clauses = [new CompilerTotalClauseArtifact()];
                break;
            case "null-clauses":
                dto.Clauses = null!;
                break;
            case "effects":
                dto.ValidEffectClaimIds = ["unowned"];
                break;
            case "null-effects":
                dto.ValidEffectClaimIds = null!;
                break;
            case "exceptions":
                dto.ExceptionConstraints = [new CompilerTotalExceptionConstraintArtifact()];
                break;
            case "null-exceptions":
                dto.ExceptionConstraints = null!;
                break;
            case "abstraction":
                dto.IsBodyAbstraction = true;
                break;
            case "entry-effects":
                dto.EffectsCompleteAtEntry = true;
                break;
            case "markers":
                dto.CallPreconditions = [];
                break;
            case "null-markers":
                dto.CallPreconditions = null!;
                break;
            case "owner":
                ownerId = " ";
                break;
        }
        Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBody(ownerId, dto, CancellationToken.None)));
    }

    [Test]
    public async Task ShadowDecoderDetachesOwnedGraphFromMutablePayload()
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var (ownerId, dto) = CreateShadowBodyArtifact();
        var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBody(ownerId, dto, CancellationToken.None);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var before = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(before.Outcome, Is.TypeOf<RefutedOutcome>(), before.Reason.ToString());
        Assert.That(before.CallPreconditionWitness, Is.Not.Null);
        dto.Graph.Roots = [];
        dto.Parameters[0].Entry = dto.Parameters[0].Current;
        dto.CallPreconditions[0].CalleeIdentity = "unrelated::M:Other.Helper";
        dto.CallPreconditions[0].InstructionIndex = -1;
        var after = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(after.Outcome, Is.TypeOf<RefutedOutcome>(), after.Reason.ToString());
        Assert.That(after.CallPreconditionWitness, Is.EqualTo(before.CallPreconditionWitness));
        Assert.That(plan!.ReplayCallPrecondition(0, after.EntryModel, CancellationToken.None),
            Is.EqualTo(before.CallPreconditionWitness));
        Assert.That(after.EntryModel.Keys, Is.EquivalentTo(decoded.Body.Parameters.Select(static parameter => parameter.Entry)));
        Assert.That(decoded.Body.CallPreconditions[0].CalleeIdentity, Is.Not.EqualTo(dto.CallPreconditions[0].CalleeIdentity));
        var reenrolled = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(reenrolled, out var freshPlan, out var freshReason), Is.True, freshReason.ToString());
        using var freshSolver = new PassiveCallableSolver(freshPlan!);
        var fresh = await freshSolver.VerifyCallPreconditionAsync(0);
        Assert.That(fresh.Outcome, Is.TypeOf<RefutedOutcome>(), fresh.Reason.ToString());
        Assert.That(fresh.CallPreconditionWitness, Is.EqualTo(before.CallPreconditionWitness));
        Assert.That(freshPlan!.ReplayCallPrecondition(0, fresh.EntryModel, CancellationToken.None),
            Is.EqualTo(before.CallPreconditionWitness));
        Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBody(ownerId, dto, CancellationToken.None)));
    }

    private static (string OwnerId, CompilerTotalCallableArtifact Artifact) CreateShadowBodyArtifact()
    {
        var artifact = CreateArtifact(GoldenTest.Load("worker", "reachable-call-precondition-artifact").Source);
        var callable = artifact.Callables.Single(static owner => owner.Total is { Clauses.Length: 0, CallPreconditions.Length: 2 });
        var total = callable.Total!;
        total.EffectsCompleteAtEntry = false;
        total.ValidEffectClaimIds = [];
        total.ExceptionConstraints = [];
        return (callable.CallableId, total);
    }

    [TestCase("baseline", false)]
    [TestCase("entry-before", true)]
    [TestCase("entry", true)]
    [TestCase("old", true)]
    [TestCase("current", false)]
    public void CanonicalRootInputsRemainImmutableWithoutOwnClauses(string mutation, bool rejected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        var entry = factory.CreateVariable("entry:0", type);
        var current = factory.CreateVariable("current:0", type);
        var old = factory.CreateVariable("old:0", type);
        var result = factory.CreateVariable("result", type);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var operation = factory.CreateOperation("canonical-test");
        if (mutation == "entry-before")
        { builder.Assign(block, operation, entry, factory.Integer(type, 1)); }
        builder.Assign(block, operation, current, factory.Variable(entry));
        builder.Assign(block, operation, old, factory.Variable(entry));
        if (mutation == "entry")
        { builder.Assign(block, operation, entry, factory.Integer(type, 1)); }
        if (mutation == "old")
        { builder.Assign(block, operation, old, factory.Integer(type, 1)); }
        if (mutation == "current")
        { builder.Assign(block, operation, current, factory.Integer(type, 1)); }
        builder.Assign(block, operation, result, factory.Variable(mutation == "old" ? old : current));
        builder.Return(block, operation, factory.Variable(result));
        var preparation = new CompilerTotalCallablePreparation("M:Subject.Root(System.Int32)~System.Int32",
            builder.Build(), [new(entry, current, old)], result, []);
        var encoded = CompilerTotalCallableArtifactCodec.Encode(preparation)!;
        var owner = new WorkerCallableManifestEntry { CallableId = preparation.CallableId };
        if (rejected)
        {
            var failure = Assert.Throws<InvalidDataException>(new Action(() =>
                CompilerTotalCallableArtifactCodec.Decode(encoded, owner, [], CancellationToken.None)));
            Assert.Throws<InvalidDataException>(new Action(() =>
                CompilerTotalCallableArtifactCodec.DecodeShadowBody(preparation.CallableId, encoded, CancellationToken.None)));
            Assert.That(failure!.Message, Is.EqualTo(mutation == "entry-before"
                ? "The Total source prologue is missing its canonical input initialization."
                : "The Total source body overwrites an immutable canonical input."));
        }
        else
        {
            var ordinary = CompilerTotalCallableArtifactCodec.Decode(encoded, owner, [], CancellationToken.None)!;
            var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBody(preparation.CallableId, encoded, CancellationToken.None).Body;
            Assert.That(decoded.CallableId, Is.EqualTo(ordinary.CallableId));
            var input = decoded.Parameters.Single().Entry;
            var execution = new IrProgramInterpreter(decoded.Program.Factory).Execute(decoded.Program,
                new Dictionary<IrVarId, IrValue>
                { [input] = decoded.Program.Factory.CreateIntegerValue(decoded.Program.Factory.GetVariableInfo(input).Type, 0L) });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.GetCurrentValue(input)!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
            Assert.That(execution.GetCurrentValue(decoded.Parameters.Single().Old)!.IntegerNumericValue,
                Is.EqualTo(System.Numerics.BigInteger.Zero));
            Assert.That(execution.ReturnValue!.IntegerNumericValue,
                Is.EqualTo(new System.Numerics.BigInteger(mutation == "current" ? 1 : 0)));
        }
    }

    [TestCase("roots", "The Total graph has an invalid mode or root closure.")]
    [TestCase("blocks", "The Total graph exceeds its program bound.")]
    [TestCase("instructions", "The Total graph exceeds its program bound.")]
    public void TotalGraphBoundsRejectBeforeMaterializingMalformedTerms(string mutation, string expectedMessage)
    {
        var artifact = CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject { [ZeroAllocations] public static int Root() => 1; }
            """);
        var total = artifact.Callables.Single().Total!;
        switch (mutation)
        {
            case "roots":
                total.Graph.Roots = Enumerable.Repeat(0, 65_536).ToArray();
                break;
            case "blocks":
                total.Graph.Blocks = Enumerable.Repeat(total.Graph.Blocks[0], 4097).ToArray();
                break;
            case "instructions":
                total.Graph.Blocks[0].Instructions = Enumerable.Repeat(total.Graph.Blocks[0].Instructions[0], 4097).ToArray();
                break;
        }
        total.Graph.Terms[0].Type = -1;
        var failure = Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.Decode(total, artifact.Manifest.Callables.Single(),
                [.. artifact.Manifest.Claims], CancellationToken.None)));
        Assert.That(failure!.Message, Is.EqualTo(expectedMessage));
    }

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
        var interpreter = new IrProgramInterpreter(total.Program.Factory);
        var entries = new Dictionary<IrVarId, IrValue>();
        Assert.That(interpreter.Execute(total.Program, entries).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var result = interpreter.Execute(total.Program, entries, 10000, new IrProgramReplayOptions(_ => null));
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
        var statements = string.Concat(Enumerable.Repeat("x = 0;\n", CompilerArtifactLimits.MaximumInstructions + 1));
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

    // A metadata body that supplies no evidence is either abstracted or an
    // opaque call: in neither case does its IL reach the program.
    internal static bool IsAbstractOrOpaque(CompilerTotalCallableArtifact total)
    {
        return total.IsBodyAbstraction ||
            total.Graph.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.Kind == IrInstructionKind.Call);
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
