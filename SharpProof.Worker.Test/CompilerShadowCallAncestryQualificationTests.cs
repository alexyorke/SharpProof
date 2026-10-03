using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Contracts;
using SharpProof.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
namespace SharpProof.Worker.Test;
[TestFixture]
public sealed class CompilerShadowCallAncestryQualificationTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class C {
            private static int Leaf(int x) { Contract.Requires(x > 0); return x; }
            private static int Mid(int x) => Leaf(x) + Leaf(x);
            public static int Root(int x) => Mid(x) + Mid(x);
        }
        """;
    [Test]
    public void RepeatedTransitiveInlineChainsRoundTripWithoutChangingPortableIds()
    {
        var (body, root, _) = Prepare();
        var before = JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(body));
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var roundTrip = JsonSerializer.Deserialize<CompilerShadowCallAncestryArtifact>(JsonSerializer.Serialize(wire))!;
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None).Body;
        var chains = CompilerShadowCallAncestryCodec.Decode(roundTrip, detached, root);
        Assert.That(chains, Has.Length.EqualTo(4));
        Assert.That(chains.All(chain => chain.Length == 2), Is.True);
        Assert.That(wire.Nodes, Has.Length.EqualTo(6));
        Assert.That(chains[0][0].Start, Is.EqualTo(chains[1][0].Start));
        Assert.That(chains[2][0].Start, Is.EqualTo(chains[3][0].Start));
        Assert.That(chains[0][0].Start, Is.Not.EqualTo(chains[2][0].Start));
        Assert.That(chains[0][1].Start, Is.EqualTo(chains[2][1].Start));
        Assert.That(chains[1][1].Start, Is.EqualTo(chains[3][1].Start));
        Assert.That(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(body)), Is.EqualTo(before));
        TestContext.Out.WriteLine("4 owned marker chains,6 canonical prefix nodes,depth2; original portable artifact unchanged");
    }
    [TestCase("parent-cycle")]
    [TestCase("caller-link")]
    [TestCase("callee")]
    [TestCase("site")]
    [TestCase("swapped-links")]
    [TestCase("missing-link")]
    [TestCase("orphan")]
    [TestCase("duplicate-node")]
    [TestCase("unknown-root")]
    [TestCase("null-node")]
    [TestCase("null-array")]
    [TestCase("unicode")]
    [TestCase("byte-bound")]
    [TestCase("node-bound")]
    public void CorruptionIsRejected(string mutation)
    {
        var (body, root, _) = Prepare();
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var end = wire.Markers[0].Node;
        switch (mutation)
        {
            case "parent-cycle":
                wire.Nodes[end] = wire.Nodes[end] with { Parent = end };
                break;
            case "caller-link":
                wire.Nodes[end] = wire.Nodes[end] with { Caller = "foreign" };
                break;
            case "callee":
                wire.Nodes[end] = wire.Nodes[end] with { Callee = "foreign" };
                break;
            case "site":
                wire.Nodes[end] = wire.Nodes[end] with { Start = wire.Nodes[end].Start + 1 };
                break;
            case "swapped-links":
                (wire.Markers[0], wire.Markers[2]) = (wire.Markers[2], wire.Markers[0]);
                break;
            case "missing-link":
                wire.Markers = wire.Markers[1..];
                break;
            case "orphan":
                wire.Nodes = [.. wire.Nodes, wire.Nodes[0] with { Start = wire.Nodes[0].Start + 1 }];
                break;
            case "duplicate-node":
                wire.Nodes = [.. wire.Nodes, wire.Nodes[0]];
                break;
            case "unknown-root":
                wire.RootIdentity = "foreign";
                break;
            case "null-node":
                wire.Nodes[0] = null!;
                break;
            case "null-array":
                wire.Nodes = null!;
                break;
            case "unicode":
                wire.Nodes[0] = wire.Nodes[0] with { Document = "\uD800" };
                break;
            case "byte-bound":
                wire.Nodes[0] = wire.Nodes[0] with { Document = new string('\u0800', 1366) };
                break;
            case "node-bound":
                wire.Nodes = Enumerable.Repeat(wire.Nodes[0], 4097).ToArray();
                break;
        }
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryCodec.Decode(wire, body, root)));
    }
    [Test]
    public void Depth257RejectsWithoutRecursiveTraversal()
    {
        var (body, root, _) = Prepare();
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        wire.Nodes = Enumerable.Range(0, 257).Select(index => new CompilerShadowCallAncestryNode(index - 1, root, root, "source", index, 1)).ToArray();
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryCodec.Decode(wire, body, root)));
    }
    [Test]
    public void IndependentSourceCensusMatchesRepeatedTransitiveChains()
    {
        var (body, root, compilation) = Prepare();
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, body, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(4));
        Assert.That(match.Matched, Is.EqualTo(4));
        Assert.That(match.Failures, Is.Empty);
    }

    [Test]
    public void CoherentSiblingReplacementPreservesFlatMarkersButFailsSourceCensus()
    {
        var (body, root, compilation) = Prepare();
        var original = JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(body));
        var markers = body.CallPreconditions;
        var fabricated = body with
        {
            CallPreconditions = [.. markers.Select((marker, index) => marker with
            {
                Ancestry = index < 2 ? marker.Ancestry.SetItem(0,
                    marker.Ancestry[0] with { Site = markers[2].Ancestry[0].Site }) : marker.Ancestry
            })]
        };
        Assert.That(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(fabricated)), Is.EqualTo(original));
        var wire = CompilerShadowCallAncestryCodec.Encode(fabricated, root);
        Assert.That(CompilerShadowCallAncestryCodec.Decode(wire, body, root), Has.Length.EqualTo(4));
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, body, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(4));
        Assert.That(match.Matched, Is.EqualTo(2));
        Assert.That(match.Failures.Count(failure => failure.StartsWith("MissingSourceAncestry:", StringComparison.Ordinal)), Is.EqualTo(2));
        TestContext.Out.WriteLine("Coherent canonical sibling transplant: flat portable artifact unchanged; independent source census expected4 matched2 rejected2missing");
    }
    [TestCase("encode")]
    [TestCase("decode")]
    [TestCase("source-census")]
    public void PreCanceledOperationsAbort(string operation)
    {
        var (body, root, compilation) = Prepare();
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() =>
        {
            switch (operation)
            {
                case "encode":
                    CompilerShadowCallAncestryCodec.Encode(body, root, cancellation.Token);
                    break;
                case "decode":
                    CompilerShadowCallAncestryCodec.Decode(wire, body, root, cancellation.Token);
                    break;
                default:
                    CompilerShadowCallAncestryQualifier.Match(compilation, body, root, wire, cancellation.Token);
                    break;
            }
        }));
    }
    [Test]
    public void OwnedInstructionClonesShareOneSourcePathButRequireSeparateCanonicalLinks()
    {
        const string singleSource = "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } public static int Root(int x) => Leaf(x); }";
        var (body, root, compilation) = Prepare(singleSource);
        var original = body.CallPreconditions.Single();
        var factory = body.Program.Factory;
        var assignment = body.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>()
            .Single(instruction => instruction.Id == original.Instruction);
        var target = factory.GetVariableInfo(assignment.Target);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("clone-entry");
        foreach (var initialization in body.Program.GetBlock(body.Program.Entry).Instructions
            .Take(body.Parameters.Length * 2).Cast<IrAssignInstruction>())
        { builder.Assign(entry, initialization.Operation, initialization.Target, initialization.Value); }
        var first = builder.Assign(entry, assignment.Operation, assignment.Target, assignment.Value);
        var copyTarget = factory.CreateVariable(factory.GetString(target.Name), target.Type);
        var second = builder.Assign(entry, assignment.Operation, copyTarget, assignment.Value);
        builder.Return(entry, assignment.Operation, factory.Variable(body.Result!.Value));
        var copied = body with { Program = builder.Build(), CallPreconditions = [original with { Instruction = first.Id }, original with { Instruction = second.Id }] };
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(copied.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(copied)!, CancellationToken.None).Body;
        var wire = CompilerShadowCallAncestryCodec.Encode(copied, root);
        Assert.That(wire.Nodes, Has.Length.EqualTo(1));
        Assert.That(wire.Markers, Has.Length.EqualTo(2));
        Assert.That(wire.Markers[0].Node, Is.EqualTo(wire.Markers[1].Node));
        Assert.That(CompilerShadowCallAncestryCodec.Decode(wire, detached, root), Has.Length.EqualTo(2));
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, detached, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(1));
        Assert.That(match.Matched, Is.EqualTo(1));
        Assert.That(match.Failures, Has.Length.EqualTo(1));
        Assert.That(match.Failures.Single(), Is.EqualTo("DuplicateOriginalSourceAncestry"));
        wire.Markers[1] = wire.Markers[0];
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryCodec.Decode(wire, detached, root)));
    }
    [Test]
    public void MandatoryPreparationDoesNotCaptureOrScreenShadowAncestry()
    {
        const string mandatorySource = "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } [EnforcePure] public static int Root(int x) { Contract.Requires(x > 0); return Leaf(x); } }";
        var compilation = TestCompilation.Create("OrdinaryAncestry", ("Subject.cs", mandatorySource));
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(owner => owner.Method.Name == "Root");
        var body = CompilerTotalCallableLowerer.Prepare(compilation, target,
            CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(body.CallPreconditions.All(marker => marker.Ancestry.IsEmpty), Is.True);
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void CompanionClauseUsesPhysicalTreeEvenWithCollisionAndLineMap(bool collision, bool mapped)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp12);
        var prefix = mapped ? "#line 900 \"Mapped.cs\"\n" : "";
        var implementation = CSharpSyntaxTree.ParseText(prefix + "public static class Helper { public static int Target(int x) => x; } public static class C { public static int Root(int x) => Helper.Target(x); }", options, "Implementation.cs");
        var companion = CSharpSyntaxTree.ParseText("#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes;\n" + prefix + "[ContractFor(typeof(Helper))] public static class HelperContracts { public static int Target(int x) { Contract.Requires(x > 0); return x; } }", options, collision ? "Implementation.cs" : "Companion.cs");
        var suffix = CSharpSyntaxTree.ParseText("internal class Unused {}", options, "Implementation.cs#1");
        var compilation = TestCompilation.Create("CompanionAncestry", ("Empty.cs", ""))
            .RemoveAllSyntaxTrees().AddSyntaxTrees(implementation, companion, suffix);
        var (body, root, _) = Prepare(compilation);
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None).Body;
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, detached, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(1));
        Assert.That(match.Matched, Is.EqualTo(1));
        Assert.That(match.Failures, Is.Empty);
        var captured = CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None);
        var marker = detached.CallPreconditions.Single();
        var clause = detached.Program.Factory.GetOperationInfo(marker.ClauseSite).SourceSpan!;
        Assert.That(clause.Document, Is.EqualTo(captured[1].Path));
        Assert.That(clause.Document, Is.Not.EqualTo(captured[0].Path));
        Assert.That(wire.Nodes.Single().Document, Is.EqualTo(captured[0].Path));
        Assert.That(clause.Start, Is.EqualTo(companion.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single().SpanStart));
        if (mapped)
        { Assert.That(clause.Document, Is.Not.EqualTo("Mapped.cs")); }
        var substitute = detached.Program.Factory.CreateOperation("substituted-clause-tree",
            new IrSourceSpan(captured[0].Path, clause.Start, clause.Length));
        var wrong = detached with { CallPreconditions = [marker with { ClauseSite = substitute }] };
        var rejected = CompilerShadowCallAncestryQualifier.Match(compilation, wrong, root, wire, CancellationToken.None);
        Assert.That(rejected.Matched, Is.Zero);
        Assert.That(rejected.Failures, Has.Some.EqualTo("UnexpectedSourceAncestry"));
        Assert.That(rejected.Failures, Has.Some.StartsWith("MissingSourceAncestry:"));
    }
    [Test]
    public void NestedArgumentCallUsesCallerChainBeforeCalleeEntry()
    {
        const string nestedSource = "using SharpProof.Attributes; public static class C { private static int Inner(int x) { Contract.Requires(x > 0); return x; } private static int Outer(int x) { Contract.Requires(x > 1); return x; } private static int Mid(int x) => Outer(Inner(x)); public static int Root(int x) => Mid(x); }";
        var (body, root, compilation) = Prepare(nestedSource);
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var chains = CompilerShadowCallAncestryCodec.Decode(wire, body, root);
        Assert.That(chains, Has.Length.EqualTo(2));
        Assert.That(chains.All(chain => chain.Length == 2), Is.True);
        var inner = chains.Single(chain => chain[^1].Callee.Contains("Inner", StringComparison.Ordinal));
        var outer = chains.Single(chain => chain[^1].Callee.Contains("Outer", StringComparison.Ordinal));
        Assert.That(inner[^1].Caller, Is.EqualTo(outer[^1].Caller));
        Assert.That(inner[0], Is.EqualTo(outer[0]));
        Assert.That(inner.Any(hop => hop.Callee.Contains("Outer", StringComparison.Ordinal)), Is.False);
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, body, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(2));
        Assert.That(match.Matched, Is.EqualTo(2));
        Assert.That(match.Failures, Is.Empty);
        var innerMarker = body.CallPreconditions.Single(marker => marker.CalleeIdentity.Contains("Inner", StringComparison.Ordinal));
        var outerMarker = body.CallPreconditions.Single(marker => marker.CalleeIdentity.Contains("Outer", StringComparison.Ordinal));
        Assert.That(innerMarker.Instruction.Value, Is.LessThan(outerMarker.Instruction.Value));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void SourceFinallyReturnExpansionKeepsDistinctLogicalAncestryCoverage(bool repeated)
    {
        const string declarations = "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } private static int Work(int x) { try { if (x > 0) return x; return 0; } finally { x = Leaf(x); } } ";
        var source = declarations + (repeated ? "public static int Root(int x) => Work(x) + Work(x); }" : "public static int Root(int x) => Work(x); }");
        var (body, root, compilation) = Prepare(source);
        var count = repeated ? 2 : 1;
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None).Body;
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, detached, root, wire, CancellationToken.None);
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(count));
        Assert.That(match.Expected, Is.EqualTo(count));
        Assert.That(match.Matched, Is.EqualTo(count));
        Assert.That(match.Failures, Is.Empty);
        Assert.That(body.CallPreconditions.Select(marker => body.Program.Factory.GetOperationInfo(marker.ClauseSite).SourceSpan)
            .Select(span => (span!.Document, span.Start, span.Length)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(wire.Nodes.Where(node => node.Parent != -1).Select(node => (node.Document, node.Start, node.Length)).Distinct().Count(), Is.EqualTo(1));
        TestContext.Out.WriteLine($"finally return paths: logical source call sites 1; expanded ordered paths {count}; original marker instructions {body.CallPreconditions.Length}");
        if (!repeated)
        { return; }
        var omitted = body with { CallPreconditions = [body.CallPreconditions[1]] };
        var omissionWire = CompilerShadowCallAncestryCodec.Encode(omitted, root);
        var omission = CompilerShadowCallAncestryQualifier.Match(compilation, omitted, root, omissionWire, CancellationToken.None);
        Assert.That(omission.Expected, Is.EqualTo(2));
        Assert.That(omission.Matched, Is.EqualTo(1));
        Assert.That(omission.Failures, Has.Some.StartsWith("MissingSourceAncestry:"));
        Assert.Throws<InvalidDataException>(new Action(() => CompilerTotalCallableArtifactCodec.DecodeShadowBody(
            omitted.CallableId, CompilerTotalCallableArtifactCodec.Encode(omitted)!, CancellationToken.None)));
        var first = body.CallPreconditions[0];
        var sibling = body.CallPreconditions[1];
        var replaced = body with { CallPreconditions = [first with { Ancestry = sibling.Ancestry }, sibling] };
        var replacementWire = CompilerShadowCallAncestryCodec.Encode(replaced, root);
        var replacement = CompilerShadowCallAncestryQualifier.Match(compilation, body, root, replacementWire, CancellationToken.None);
        Assert.That(replacement.Expected, Is.EqualTo(2));
        Assert.That(replacement.Matched, Is.EqualTo(1));
        Assert.That(replacement.Failures, Has.Some.StartsWith("MissingSourceAncestry:"));
    }
    [Test]
    public void GenuineLoopSearchClonesRequireCompleteOriginalInstructionMapping()
    {
        const string loopSource = "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } public static int Root(int x) { while (x > 0) { x = Leaf(x) - 1; } return x; } }";
        var (body, root, compilation) = Prepare(loopSource);
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        var wire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        var match = CompilerShadowCallAncestryQualifier.Match(compilation, detached.Body, root, wire, CancellationToken.None);
        Assert.That(match.Expected, Is.EqualTo(1));
        Assert.That(match.Failures, Is.Empty);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(detached);
        Assert.That(PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out var reason, CancellationToken.None), Is.True, reason.ToString());
        Assert.That(search!.CallMarkers.Count, Is.GreaterThan(1));
        Assert.That(search.CallMarkers.Values.Distinct().Single(), Is.EqualTo(detached.Body.CallPreconditions.Single().Instruction));
        ValidateTransformMapping(detached.Body, proof!);
        ValidateTransformMapping(detached.Body, search);
        var pair = search.CallMarkers.First();
        var omitted = search with { CallMarkers = search.CallMarkers.Remove(pair.Key) };
        Assert.Throws<InvalidDataException>(new Action(() => ValidateTransformMapping(detached.Body, omitted)));
        var foreign = detached.Body.Program.GetBlock(detached.Body.Program.Entry).Terminator.Id;
        var replaced = search with { CallMarkers = search.CallMarkers.SetItem(pair.Key, foreign) };
        Assert.Throws<InvalidDataException>(new Action(() => ValidateTransformMapping(detached.Body, replaced)));
        TestContext.Out.WriteLine($"original logical markers 1; proof copies {proof!.CallMarkers.Count}; search copies {search.CallMarkers.Count}; all search copies map to owned original marker");
    }
    private static void ValidateTransformMapping(CompilerTotalCallablePreparation original, PassiveLoopCutter.Encoding transformed)
    {
        var ids = original.CallPreconditions.Select(marker => marker.Instruction).ToHashSet();
        var instructions = original.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>()
            .Where(instruction => ids.Contains(instruction.Id)).ToDictionary(instruction => instruction.Id);
        var markers = transformed.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>()
            .Where(instruction => transformed.Program.Factory.GetString(
                transformed.Program.Factory.GetVariableInfo(instruction.Target).Name).StartsWith("$sharpproof.requires:", StringComparison.Ordinal)).ToArray();
        if (markers.Length != transformed.CallMarkers.Count || markers.Length > 65_536)
        { throw new InvalidDataException("Incomplete bounded transformation marker map."); }
        foreach (var marker in markers)
        {
            if (!transformed.CallMarkers.TryGetValue(marker.Id, out var originalId) ||
                !instructions.TryGetValue(originalId, out var source) || marker.Operation != source.Operation ||
                marker.Target != source.Target || marker.Value.Id != source.Value.Id)
            { throw new InvalidDataException("Unowned transformed marker."); }
        }
    }
    [Test]
    public async System.Threading.Tasks.Task CompilerRecomputationRejectsDisconnectedOriginalMarker()
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var (body, root, compilation) = Prepare("using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } public static int Root() => Leaf(0); }");
        var original = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        var originalCandidate = PassiveCallableArtifactAdapter.EnrollShadow(original);
        Assert.That(PassiveCallableVcBuilder.TryBuild(originalCandidate, out var originalPlan, out var reason), Is.True, reason.ToString());
        using var originalSolver = new PassiveCallableSolver(originalPlan!);
        var originalOutcome = await originalSolver.VerifyCallPreconditionAsync(0);
        Assert.That(originalOutcome.Outcome, Is.InstanceOf<SharpProof.Verify.RefutedOutcome>());
        var marker = body.CallPreconditions.Single();
        var assignment = body.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>().Single(instruction => instruction.Id == marker.Instruction);
        var factory = body.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("bypassed-original-call");
        builder.Return(entry, assignment.Operation, factory.Integer(factory.GetVariableInfo(body.Result!.Value).Type, 0));
        var detached = builder.CreateBlock("detached-original-marker");
        var replacement = builder.Assign(detached, assignment.Operation, assignment.Target, assignment.Value);
        builder.Return(detached, assignment.Operation, factory.Integer(factory.GetVariableInfo(body.Result.Value).Type, 0));
        var fabricated = body with { Program = builder.Build(), CallPreconditions = [marker with { Instruction = replacement.Id }] };
        var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBody(fabricated.CallableId, CompilerTotalCallableArtifactCodec.Encode(fabricated)!, CancellationToken.None);
        var wire = CompilerShadowCallAncestryCodec.Encode(fabricated, root);
        var identityOnly = CompilerShadowCallAncestryQualifier.Match(compilation, decoded.Body, root, wire, CancellationToken.None);
        Assert.That(identityOnly.Expected, Is.EqualTo(1));
        Assert.That(identityOnly.Matched, Is.EqualTo(1));
        Assert.That(identityOnly.Failures, Is.Empty);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var outcome = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(outcome.Outcome, Is.InstanceOf<SharpProof.Verify.ProvenOutcome>());
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(compilation, decoded.Body, root, wire, CancellationToken.None)));
    }
    [Test]
    public void SourceAncestryRejectsOwnedMetadataClauseWithCoherentSuppliedHop()
    {
        using var subject = new MetadataTestSubject("using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => value; }", "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References, CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All, CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references, CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, true);
        Assert.That(batch.Gaps, Is.Empty);
        var body = batch.Callers.Single().Body;
        var detached = CompilerDecodedShadowBody.Decode(body.CallableId, CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None, references);
        var root = CompilerIdentityBridge.CreateSymbolDisplay(subject.Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single());
        var marker = detached.Body.CallPreconditions.Single();
        Assert.That(marker.MetadataClause, Is.Not.Null);
        Assert.That(detached.Body.Program.Factory.GetOperationInfo(marker.ClauseSite).SourceSpan, Is.Null);
        var assignment = detached.Body.Program.Blocks.SelectMany(block => block.Instructions).Single(instruction => instruction.Id == marker.Instruction);
        var span = detached.Body.Program.Factory.GetOperationInfo(assignment.Operation).SourceSpan!;
        var withHop = detached.Body with { CallPreconditions = [marker with { Ancestry = [new(root, marker.CalleeIdentity, span)] }] };
        var wire = CompilerShadowCallAncestryCodec.Encode(withHop, root);
        Assert.That(CompilerShadowCallAncestryCodec.Decode(wire, withHop, root, CancellationToken.None), Has.Length.EqualTo(1));
        var failure = Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryQualifier.Match(subject.Compilation, withHop, root, wire, CancellationToken.None)));
        Assert.That(failure!.Message, Is.EqualTo("Source ancestry requires a physically owned source clause."));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async System.Threading.Tasks.Task RecomputedSourceRejectsRealFinallyOrLoopBranchTampering(bool loop)
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var source = loop ? "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } public static int Root() { int x = 1; while (x > 0) { x = Leaf(0) - 1; } return x; } }" : "using SharpProof.Attributes; public static class C { private static int Leaf(int x) { Contract.Requires(x > 0); return x; } public static int Root() { int x = 0; try { if (x == 0) return Leaf(x); return 1; } finally { x = 2; } } }";
        var (body, root, compilation) = Prepare(source);
        var original = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        if (loop)
        {
            Assert.That(PassiveLoopCutter.TryCreate(PassiveCallableArtifactAdapter.EnrollShadow(original), out var proof, out var search, out var transformReason, CancellationToken.None), Is.True, transformReason.ToString());
            ValidateTransformMapping(original.Body, proof!);
            ValidateTransformMapping(original.Body, search!);
            await AssertNativeCallOutcome(original, false);
        }
        var originalMarkers = ObserveOriginalMarkers(original.Body);
        Assert.That(originalMarkers, Has.Length.EqualTo(1));
        Assert.That(originalMarkers.Single(), Is.False);
        var artifact = CompilerTotalCallableArtifactCodec.Encode(body)!;
        var sourceRoot = await compilation.SyntaxTrees.Single().GetRootAsync();
        var condition = loop ? sourceRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax>().Single().Condition.Span : sourceRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>().Single().Condition.Span;
        var branch = artifact.Graph.Blocks.SelectMany(block => block.Instructions).Single(instruction => instruction.Kind == IrInstructionKind.Branch && artifact.Graph.Operations[instruction.Operation].SourceSpan is { } span && span.Start == condition.Start && span.Length == condition.Length);
        (branch.B, branch.C) = (branch.C, branch.B);
        var changed = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, artifact, CancellationToken.None);
        var sidecar = CompilerShadowCallAncestryCodec.Encode(body, root);
        var identity = CompilerShadowCallAncestryQualifier.Match(compilation, changed.Body, root, sidecar, CancellationToken.None);
        Assert.That(identity.Expected, Is.EqualTo(1));
        Assert.That(identity.Matched, Is.EqualTo(1));
        Assert.That(identity.Failures, Is.Empty);
        Assert.That(ObserveOriginalMarkers(changed.Body), Is.Empty);
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(compilation, changed.Body, root, sidecar, CancellationToken.None)));
    }
    [Test]
    public async System.Threading.Tasks.Task RecomputedSourceRejectsDelayedEarlierArgumentCapture()
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        const string source = "using SharpProof.Attributes; public static class C { private static int Leaf(int first, int second) { Contract.Requires(first == 0); return first * 10 + second; } public static int Root() { int x = 0; return Leaf(x, x = 1); } }";
        var (body, root, compilation) = Prepare(source);
        var original = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        await AssertNativeCallOutcome(original, true);
        var syntaxRoot = await compilation.SyntaxTrees.Single().GetRootAsync();
        var argument = syntaxRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single(invocation => invocation.Expression.ToString() == "Leaf").ArgumentList.Arguments[0].Expression.Span;
        var undelayed = DelaySourceCapture(body, argument.Start, argument.Length, false);
        var baselineWire = CompilerShadowCallAncestryCodec.Encode(undelayed, root);
        Assert.That(CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(compilation, undelayed, root, baselineWire, CancellationToken.None).Failures, Is.Empty);
        var changedBody = DelaySourceCapture(body, argument.Start, argument.Length, true);
        var changed = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, CompilerTotalCallableArtifactCodec.Encode(changedBody)!, CancellationToken.None);
        var sidecar = CompilerShadowCallAncestryCodec.Encode(body, root);
        var identity = CompilerShadowCallAncestryQualifier.Match(compilation, changed.Body, root, sidecar, CancellationToken.None);
        Assert.That(identity.Matched, Is.EqualTo(1));
        Assert.That(identity.Failures, Is.Empty);
        await AssertNativeCallOutcome(changed, false);
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(compilation, changed.Body, root, sidecar, CancellationToken.None)));
    }
    private static bool[] ObserveOriginalMarkers(CompilerTotalCallablePreparation body)
    {
        var observations = new List<bool>();
        var owned = body.CallPreconditions.Select(marker => marker.Instruction).ToHashSet();
        var options = new IrProgramReplayOptions(static _ => null)
        {
            AssignmentObserver = (instruction, value, _) => { if (owned.Contains(instruction.Id)) { observations.Add(value.Boolean); } }
        };
        var execution = new IrProgramInterpreter(body.Program.Factory).Execute(body.Program, new Dictionary<IrVarId, IrValue>(), 10000, options);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        return [.. observations];
    }
    private static CompilerTotalCallablePreparation DelaySourceCapture(CompilerTotalCallablePreparation body, int start, int length, bool delay)
    {
        var builder = new IrProgramBuilder(body.Program.Factory);
        var blocks = body.Program.Blocks.ToDictionary(block => block.Id, block => builder.CreateBlock(block.Name is { } name ? body.Program.Factory.GetString(name) : null));
        builder.SetEntry(blocks[body.Program.Entry]);
        var ids = new Dictionary<IrInstructionId, IrInstructionId>();
        var changed = false;
        foreach (var block in body.Program.Blocks)
        {
            var rows = block.Instructions.ToList();
            var capture = rows.OfType<IrAssignInstruction>().SingleOrDefault(instruction => body.Program.Factory.GetOperationInfo(instruction.Operation).SourceSpan is { } span && span.Start == start && span.Length == length);
            if (capture != null && delay)
            {
                Assert.That(rows.Remove(capture), Is.True);
                var markerIndex = rows.FindIndex(instruction => body.CallPreconditions.Any(marker => marker.Instruction == instruction.Id));
                Assert.That(markerIndex, Is.GreaterThan(0));
                rows.Insert(markerIndex, capture);
                changed = true;
            }
            foreach (var instruction in rows)
            {
                IrInstruction copied = instruction switch
                {
                    IrAssignInstruction assign => builder.Assign(blocks[block.Id], assign.Operation, assign.Target, assign.Value),
                    IrWriteInstruction write => builder.Write(blocks[block.Id], write.Operation, write.Region),
                    IrAssumeInstruction assume => builder.Assume(blocks[block.Id], assume.Operation, assume.Condition),
                    IrAssertInstruction assertion => builder.Assert(blocks[block.Id], assertion.Operation, assertion.Condition),
                    IrHavocInstruction havoc => builder.Havoc(blocks[block.Id], havoc.Operation, havoc.HavocKind, havoc.Origin, [.. havoc.Variables]),
                    IrBranchInstruction branch => builder.Branch(blocks[block.Id], branch.Operation, branch.Condition, blocks[branch.WhenTrue], blocks[branch.WhenFalse]),
                    IrGotoInstruction jump => builder.Goto(blocks[block.Id], jump.Operation, blocks[jump.Target]),
                    IrReturnInstruction returned => builder.Return(blocks[block.Id], returned.Operation, returned.Value),
                    IrThrowInstruction thrown => builder.Throw(blocks[block.Id], thrown.Operation, thrown.ExceptionKind, blocks[thrown.Target]),
                    IrExceptionalExitInstruction exceptional => builder.ExceptionalExit(blocks[block.Id], exceptional.Operation),
                    _ => throw new InvalidDataException("Unexpected capture fixture instruction.")
                };
                ids.Add(instruction.Id, copied.Id);
            }
        }
        Assert.That(changed, Is.EqualTo(delay));
        return body with { Program = builder.Build(), CallPreconditions = [.. body.CallPreconditions.Select(marker => marker with { Instruction = ids[marker.Instruction] })] };
    }
    private static async System.Threading.Tasks.Task AssertNativeCallOutcome(CompilerDecodedShadowBody body, bool proven)
    {
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.EnrollShadow(body), out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var outcome = await solver.VerifyCallPreconditionAsync(0);
        if (proven)
        { Assert.That(outcome.Outcome, Is.InstanceOf<SharpProof.Verify.ProvenOutcome>()); }
        else
        { Assert.That(outcome.Outcome, Is.InstanceOf<SharpProof.Verify.RefutedOutcome>()); }
    }
    internal static async System.Threading.Tasks.Task<string> TamperedPredicateGolden(GoldenCase fixture)
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var (body, root, compilation) = Prepare(TestCompilation.Create("TamperedAncestry", (fixture.RelativePath, fixture.Source)));
        var original = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        var originalWire = CompilerShadowCallAncestryCodec.Encode(body, root);
        var qualified = CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(compilation, original.Body, root, originalWire, CancellationToken.None);
        Assert.That(qualified.Failures, Is.Empty);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(original);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var originalSolver = new PassiveCallableSolver(plan!);
        var originalOutcome = await originalSolver.VerifyCallPreconditionAsync(0);
        Assert.That(originalOutcome.Outcome, Is.InstanceOf<SharpProof.Verify.RefutedOutcome>());
        var marker = body.CallPreconditions.Single();
        var assignment = body.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>()
            .Single(instruction => instruction.Id == marker.Instruction);
        var factory = body.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("fabricated-true-predicate");
        var replacement = builder.Assign(entry, assignment.Operation, assignment.Target, factory.Boolean(true));
        builder.Return(entry, assignment.Operation, factory.Integer(factory.GetVariableInfo(body.Result!.Value).Type, 0));
        var fabricated = body with
        {
            Program = builder.Build(),
            CallPreconditions = [marker with { Instruction = replacement.Id, Value = factory.Boolean(true), Safe = factory.Boolean(true) }]
        };
        var decoded = CompilerTotalCallableArtifactCodec.DecodeShadowBody(fabricated.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(fabricated)!, CancellationToken.None);
        var wire = CompilerShadowCallAncestryCodec.Encode(fabricated, root);
        var identityOnly = CompilerShadowCallAncestryQualifier.Match(compilation, decoded.Body, root, wire, CancellationToken.None);
        Assert.That(identityOnly.Expected, Is.EqualTo(1));
        Assert.That(identityOnly.Matched, Is.EqualTo(1));
        Assert.That(identityOnly.Failures, Is.Empty);
        var mutatedCandidate = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(mutatedCandidate, out var mutatedPlan, out reason), Is.True, reason.ToString());
        using var mutatedSolver = new PassiveCallableSolver(mutatedPlan!);
        var mutatedOutcome = await mutatedSolver.VerifyCallPreconditionAsync(0);
        Assert.That(mutatedOutcome.Outcome, Is.InstanceOf<SharpProof.Verify.ProvenOutcome>());
        Assert.Throws<InvalidDataException>(new Action(() => CompilerShadowCallAncestryQualifier.MatchCompilerRecomputed(
            compilation, decoded.Body, root, wire, CancellationToken.None)));
        return "original: Refuted\nstructural-mutation: Proven\nsource-identity: 1/1\ncompiler-recomputation: Rejected\n";
    }
    private static (CompilerTotalCallablePreparation Body, string Root, CSharpCompilation Compilation) Prepare(string source = Source)
    {
        var compilation = TestCompilation.Create("Ancestry", ("Subject.cs", source));
        return Prepare(compilation);
    }
    private static (CompilerTotalCallablePreparation Body, string Root, CSharpCompilation Compilation) Prepare(CSharpCompilation compilation)
    {
        TestCompilation.AssertNoErrors(compilation);
        var root = CompilerIdentityBridge.CreateSymbolDisplay(compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single());
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(batch.Gaps.All(gap => gap.Reason == "UnsupportedOwnContracts"), Is.True);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single();
        return (batch.Callers.Single(caller => caller.OwnerId == SemanticClaimIdentity.CreateCallableId(method)).Body, root, compilation);
    }
}
