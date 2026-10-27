using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskEmittedFaultSiteTests
{
    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public async Task ActualCompilerArtifactCarriesCompleteLoopSitesToTheCumulativeUnknownConsumer(
        OptimizationLevel optimization, bool configured)
    {
        var target = Prepare(optimization, configured,
            "for(int i=0;i<n;i++){int sum=checked(i+n);var values=new int[2];}");
        var projection = target.ForkJoinProjection!;
        var inventory = Inventory(target);
        Assert.That(projection.Children, Has.Length.EqualTo(2));
        Assert.That(inventory.Bodies, Has.Length.EqualTo(1), "Repeated callbacks share one emitted source body.");
        var check = Check(target);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.EqualTo("owned-emitted-primitive-fault-sites-v1"));
        var expected = inventory.Bodies.SelectMany(body => body is null ? throw new ArgumentNullException(nameof(body)) : DecodeIndependently(body.Code)
            .Where(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : ExpectedFaults(instruction.OpCode) != CompilerTaskPrimitiveFaultKind.None)
            .Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : (body.MethodToken, instruction.Offset, instruction.OpCode, ExpectedFaults(instruction.OpCode))))
            .ToArray();
        var sites = check.Sites!.Value;
        Assert.That(sites.Select(static site => site is null ? throw new ArgumentNullException(nameof(site)) : (site.MethodToken, site.IlOffset, site.OpCode, site.Faults)),
            Is.EqualTo(expected));
        Assert.That(sites.Count(static site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Faults == CompilerTaskPrimitiveFaultKind.NewArray), Is.EqualTo(1));
        Assert.That(sites.Any(static site => site is null ? throw new ArgumentNullException(nameof(site)) : site.Faults == CompilerTaskPrimitiveFaultKind.Overflow), Is.True);
        var bodyCode = DecodeIndependently(inventory.Bodies.Single().Code);
        Assert.That(bodyCode.Any(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.TargetOffset >= 0 && instruction.TargetOffset < instruction.Offset),
            Is.True, "The fixture must retain a loop; static site count is not an execution count.");
        var routes = NativeTaskPrimitiveHelperRouteVerifier.Check(projection, target.TotalEntry!, Layout(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.NewArrayDirect));
        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.NewArrayValueDynamic));
        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.ReadyToRunNewArray));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        AssertUnknown(result, "TaskPrimitiveHelperDynamicArrayRouteUnproved");
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void EmptyEmittedBodiesRemainOwnedWithoutBecomingARuntimeFaultProof(OptimizationLevel optimization, bool configured)
    {
        var target = Prepare(optimization, configured, "");
        var check = Check(target);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.Sites, Is.Not.Null);
        Assert.That(check.Sites!.Value, Is.Empty);
        Assert.That(Inventory(target).Bodies, Has.Length.EqualTo(1));
        AssertUnknown(VerifyRoot(target), "TaskRuntimeFaultFrameCompositionUnproved");
    }

    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void DistinctBodiesRetainEverySiteAndEveryOwnedMethodAfterArtifactRoundTrip(OptimizationLevel optimization)
    {
        var target = Prepare(optimization, false, "int quotient=n/(n-1);", "var values=new int[1];");
        var inventory = Inventory(target);
        Assert.That(inventory.Bodies, Has.Length.EqualTo(2));
        Assert.That(inventory.Bodies.Select(static body => body is null ? throw new ArgumentNullException(nameof(body)) : body.MethodToken), Is.Ordered.Ascending);
        var check = Check(target);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.Sites!.Value.Select(static site => site is null ? throw new ArgumentNullException(nameof(site)) : site.MethodToken).Distinct(),
            Is.EquivalentTo(inventory.Bodies.Select(static body => body is null ? throw new ArgumentNullException(nameof(body)) : body.MethodToken)));
        foreach (var body in inventory.Bodies)
        {
            Assert.That(body.CodeSha256, Is.EqualTo(WorkerProtocolJson.ComputeSha256(body.Code)));
            Assert.That(body.Code, Is.Not.Empty);
        }
        AssertUnknown(VerifyRoot(target), "TaskPrimitiveHelperDynamicArrayRouteUnproved");
    }

    [TestCase("absent")]
    [TestCase("capture-failure")]
    [TestCase("mixed-capture")]
    [TestCase("root")]
    [TestCase("module")]
    [TestCase("compiler")]
    [TestCase("framework")]
    [TestCase("callbacks")]
    [TestCase("source-bodies")]
    [TestCase("invalid-source-token")]
    [TestCase("duplicate-source-token")]
    [TestCase("invalid-forwarder-token")]
    [TestCase("invalid-forwarder-target-token")]
    [TestCase("source-forwarder-target-mismatch")]
    [TestCase("missing-later-body")]
    [TestCase("duplicate-body")]
    [TestCase("foreign-body")]
    [TestCase("body-order")]
    [TestCase("later-hash")]
    [TestCase("later-empty-code")]
    [TestCase("later-default-code")]
    [TestCase("later-truncated-newarr")]
    [TestCase("later-call")]
    public void NativeOwnershipOrLaterBodyFailureIsAtomicAtTheActualRootConsumer(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, "int quotient=n/(n-1);", "var values=new int[1];");
        Assert.That(Check(target).Sites!.Value, Has.Length.GreaterThan(1));
        var projection = target.ForkJoinProjection!;
        var shapes = projection.Carriers!.Shapes!;
        var sourceInventory = shapes.SourceBodies!.Inventory!;
        var callbacks = shapes.CallbackActivations!.Activations!;
        var inventory = Inventory(target);
        var bodies = inventory.Bodies;
        var later = bodies[1];
        var changed = inventory;
        switch (mutation)
        {
            case "absent":
                shapes = shapes with { EmittedFaultBodies = null };
                break;
            case "capture-failure":
                shapes = shapes with { EmittedFaultBodies = new(null, "RejectedLaterEmittedBody") };
                break;
            case "mixed-capture":
                shapes = shapes with { EmittedFaultBodies = new(inventory, "RejectedLaterEmittedBody") };
                break;
            case "root":
                changed = inventory with { RootCallableId = "foreign" };
                break;
            case "module":
                changed = inventory with { ModuleSha256 = new string('a', 64) };
                break;
            case "compiler":
                changed = inventory with { CompilerIdentity = "foreign compiler" };
                break;
            case "framework":
                changed = inventory with { FrameworkAssemblyIdentity = "foreign framework" };
                break;
            case "callbacks":
                shapes = shapes with { CallbackActivations = null };
                break;
            case "source-bodies":
                shapes = shapes with { SourceBodies = null };
                break;
            case "invalid-source-token":
                shapes = shapes with
                {
                    SourceBodies = new(sourceInventory with
                    { MethodTokens = sourceInventory.MethodTokens.SetItem(1, 0x0a000001) }, null)
                };
                break;
            case "duplicate-source-token":
                shapes = shapes with
                {
                    SourceBodies = new(sourceInventory with
                    { MethodTokens = sourceInventory.MethodTokens.SetItem(1, sourceInventory.MethodTokens[0]) }, null)
                };
                break;
            case "invalid-forwarder-token":
                shapes = shapes with
                {
                    CallbackActivations = new(callbacks with
                    { Forwarders = callbacks.Forwarders.SetItem(1, callbacks.Forwarders[1] with { MethodToken = 0x0a000001 }) }, null)
                };
                break;
            case "invalid-forwarder-target-token":
                shapes = shapes with
                {
                    CallbackActivations = new(callbacks with
                    { Forwarders = callbacks.Forwarders.SetItem(1, callbacks.Forwarders[1] with { ChildMethodToken = 0x0a000001 }) }, null)
                };
                break;
            case "source-forwarder-target-mismatch":
                shapes = shapes with
                {
                    SourceBodies = new(sourceInventory with { MethodTokens = sourceInventory.MethodTokens.RemoveAt(1) }, null)
                };
                break;
            case "missing-later-body":
                changed = inventory with { Bodies = bodies.RemoveAt(1) };
                break;
            case "duplicate-body":
                changed = inventory with { Bodies = bodies.SetItem(1, bodies[0]) };
                break;
            case "foreign-body":
                changed = inventory with { Bodies = bodies.SetItem(1, later with { MethodToken = 0x0600ffff }) };
                break;
            case "body-order":
                changed = inventory with { Bodies = [.. bodies.Reverse()] };
                break;
            case "later-hash":
                changed = inventory with { Bodies = bodies.SetItem(1, later with { CodeSha256 = new string('a', 64) }) };
                break;
            case "later-empty-code":
                changed = inventory with { Bodies = bodies.SetItem(1, WithCode(later, [])) };
                break;
            case "later-default-code":
                changed = inventory with { Bodies = bodies.SetItem(1, later with { Code = default }) };
                break;
            case "later-truncated-newarr":
                var allocation = DecodeIndependently(later.Code).Single(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode == 0x8d);
                changed = inventory with { Bodies = bodies.SetItem(1, WithCode(later, later.Code.Take(allocation.Offset + 1).ToImmutableArray())) };
                break;
            case "later-call":
                var callCode = later.Code.ToArray();
                var original = DecodeIndependently(later.Code).Single(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode == 0x8d);
                callCode[original.Offset] = 0x28;
                changed = inventory with { Bodies = bodies.SetItem(1, WithCode(later, [.. callCode])) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        if (!ReferenceEquals(changed, inventory))
        { shapes = shapes with { EmittedFaultBodies = new(changed, null) }; }
        target = WithShapes(target, shapes);
        var rejected = Check(target);
        Assert.That(rejected.UnknownReason, Is.Not.Null.And.Not.Empty, mutation);
        if (mutation is "invalid-source-token" or "duplicate-source-token" or "invalid-forwarder-token" or
            "invalid-forwarder-target-token" or "source-forwarder-target-mismatch")
        { Assert.That(rejected.UnknownReason, Is.EqualTo("TaskEmittedFaultBodyCoverageInvalid"), mutation); }
        Assert.That(rejected.Sites, Is.Null, mutation);
        Assert.That(rejected.RequiredCompilerModel, Is.Null, mutation);
        var routes = NativeTaskPrimitiveHelperRouteVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, Layout(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(routes.UnprovedAlternatives, Is.Empty);
        AssertUnknown(VerifyRoot(target), rejected.UnknownReason!);
    }

    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void UnsupportedLaterArrayMetadataDoesNotRetainAnEarlierEmittedBody(OptimizationLevel optimization)
    {
        var target = Prepare(optimization, false, "int quotient=n/(n-1);", "var values=new byte[1];");
        var capture = target.ForkJoinProjection!.Carriers!.Shapes!.EmittedFaultBodies!;
        Assert.That(capture.Inventory, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskSourceBodyArrayTypeUnsupported"));
        var rejected = Check(target);
        Assert.That(rejected.Sites, Is.Null);
        Assert.That(rejected.RequiredCompilerModel, Is.Null);
        AssertUnknown(VerifyRoot(target), capture.UnknownReason!);
    }

    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("offset")]
    [TestCase("token")]
    [TestCase("type-definition")]
    [TestCase("assembly")]
    [TestCase("element")]
    [TestCase("default")]
    public void ArrayOperandOwnershipCannotBeReplacedByAnEarlierValidSite(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, "int quotient=n/(n-1);", "var values=new int[1];");
        var inventory = Inventory(target);
        var later = inventory.Bodies[1];
        var fact = later.ArrayTypeOperands.Single();
        var changed = mutation switch
        {
            "missing" => later with { ArrayTypeOperands = [] },
            "extra" => later with { ArrayTypeOperands = [fact, fact with { IlOffset = fact.IlOffset + 1 }] },
            "offset" => later with { ArrayTypeOperands = [fact with { IlOffset = fact.IlOffset + 1 }] },
            "token" => later with { ArrayTypeOperands = [fact with { TypeToken = fact.TypeToken + 1 }] },
            "assembly" => later with { ArrayTypeOperands = [fact with { AssemblyIdentity = "Fake.Int32, Version=8.0.0.0" }] },
            "element" => later with { ArrayTypeOperands = [fact with { ElementKind = (CompilerTaskEmittedArrayElementKind)999 }] },
            "default" => later with { ArrayTypeOperands = default },
            "type-definition" => ReplaceArrayToken(later, fact, 0x02000001),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        target = WithInventory(target, inventory with { Bodies = inventory.Bodies.SetItem(1, changed) });
        var rejected = Check(target);
        Assert.That(rejected.UnknownReason, Is.EqualTo("TaskEmittedFaultBodyEvidenceInvalid"));
        Assert.That(rejected.Sites, Is.Null);
        Assert.That(rejected.RequiredCompilerModel, Is.Null);
        AssertUnknown(VerifyRoot(target), rejected.UnknownReason!);
    }

    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void ScannerClassifiesActualEmittedPrimitiveFamiliesUsingIndependentInstructionBoundaries(OptimizationLevel optimization)
    {
        var code = CompileProbe(optimization, "public static int Probe(int n,int[] values){values[0]=n;var owned=new int[n];" +
            "return checked(n+1)+n/(n-1)+n%(n-1)+(int)((uint)n/(uint)(n-1))+(int)((uint)n%(uint)(n-1))+owned.Length+values[n];}");
        var independent = DecodeIndependently(code);
        var scan = CompilerTaskPrimitiveIlScanner.Scan(code, TestContext.CurrentContext.CancellationToken);
        Assert.That(scan.UnknownReason, Is.Null);
        var instructions = scan.Instructions!.Value;
        Assert.That(instructions.Select(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : (instruction.Offset, instruction.OpCode, instruction.NextOffset)),
            Is.EqualTo(independent.Select(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : (instruction.Offset, instruction.OpCode, instruction.NextOffset))));
        foreach (var instruction in instructions)
        { Assert.That(instruction.Faults, Is.EqualTo(ExpectedFaults(instruction.OpCode)), "IL offset " + instruction.Offset); }
        foreach (var opcode in new[] { 0xd6, 0x5b, 0x5c, 0x5d, 0x5e, 0x8d, 0x8e, 0x94, 0x9e })
        { Assert.That(independent.Any(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode == opcode), Is.True, "Missing emitted opcode " + opcode); }
    }

    [Test]
    public void ScannerPreservesWideLocalSlotsFromActualCompilerEmission()
    {
        const int lastLocal = byte.MaxValue + 1;
        var declarations = string.Concat(Enumerable.Range(0, lastLocal + 1).Select(static index =>
            "int local" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=0;"));
        // Debug emission retains all initialized locals, forcing the final
        // local's read and write to use the unsigned 16-bit slot encoding.
        var code = CompileProbe(OptimizationLevel.Debug,
            "public static int Probe(int n){" + declarations + "local256=n;return local256;}");
        var independent = DecodeIndependently(code);
        var expected = independent.Where(static instruction => instruction is null ?
            throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode is 0xfe0c or 0xfe0e)
            .Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) :
                (instruction.Offset, instruction.OpCode, instruction.NextOffset,
                    Operand: (int)BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(instruction.Offset + 2, 2)))).ToArray();
        Assert.That(expected.Any(static instruction => instruction.OpCode == 0xfe0c && instruction.Operand == lastLocal), Is.True,
            "The emitted fixture must load local slot 256 with the full ldloc encoding.");
        Assert.That(expected.Any(static instruction => instruction.OpCode == 0xfe0e && instruction.Operand == lastLocal), Is.True,
            "The emitted fixture must store local slot 256 with the full stloc encoding.");
        Assert.That(expected.All(static instruction => instruction.NextOffset == instruction.Offset + 4), Is.True,
            "Each full local instruction must consume its two-byte opcode and two-byte operand.");
        var scan = CompilerTaskPrimitiveIlScanner.Scan(code, TestContext.CurrentContext.CancellationToken);
        Assert.That(scan.UnknownReason, Is.Null);
        Assert.That(scan.Instructions!.Value.Where(static instruction => instruction is null ?
            throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode is 0xfe0c or 0xfe0e)
            .Select(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) :
                (instruction.Offset, instruction.OpCode, instruction.NextOffset, instruction.Operand)), Is.EqualTo(expected));
    }

    [TestCase("public static int Probe(int n)=>System.Math.Abs(n);", "TaskEmittedFaultBodyCallUnsupported")]
    [TestCase("public static long Probe(int n)=>(long)n;", "TaskEmittedFaultBodyInstructionUnsupported")]
    [TestCase("private static volatile int Value; public static int Probe(int n)=>Value;", "TaskEmittedFaultBodyInstructionUnsupported")]
    public void ScannerRejectsActuallyEmittedCallsConversionsAndPrefixes(string declaration, string reason)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(reason);
        var scan = CompilerTaskPrimitiveIlScanner.Scan(CompileProbe(OptimizationLevel.Debug, declaration),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(scan.UnknownReason, Is.EqualTo(reason));
        Assert.That(scan.Instructions, Is.Null);
    }

    [TestCase("two-byte-opcode")]
    [TestCase("operand")]
    [TestCase("branch-into-operand")]
    public void TruncationAndInteriorBranchTargetsRejectTheWholeEmittedStream(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var code = CompileProbe(OptimizationLevel.Debug,
            "public static int Probe(int n){int value=0;for(int i=0;i<n;i++){var owned=new int[2];value+=owned.Length;}return value>0?value:0;}");
        var independent = DecodeIndependently(code);
        var allocation = independent.Single(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode == 0x8d);
        var bytes = code.ToArray();
        if (mutation == "two-byte-opcode")
        {
            var instruction = independent.First(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.OpCode > 0xff);
            bytes = bytes.Take(instruction.Offset + 1).ToArray();
        }
        else if (mutation == "operand")
        { bytes = bytes.Take(allocation.Offset + 2).ToArray(); }
        else
        {
            var branch = independent.First(static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.TargetOffset >= 0);
            var displacement = allocation.Offset + 1 - branch.NextOffset;
            if (branch.NextOffset - branch.Offset == 2)
            {
                Assert.That(displacement, Is.InRange((int)sbyte.MinValue, (int)sbyte.MaxValue));
                bytes[branch.Offset + 1] = unchecked((byte)(sbyte)displacement);
            }
            else
            { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(branch.Offset + 1, 4), displacement); }
        }
        var scan = CompilerTaskPrimitiveIlScanner.Scan([.. bytes], TestContext.CurrentContext.CancellationToken);
        Assert.That(scan.UnknownReason, Is.EqualTo("TaskEmittedFaultBodyInstructionUnsupported"));
        Assert.That(scan.Instructions, Is.Null);
    }

    [TestCase("missing-property")]
    [TestCase("unknown-property")]
    [TestCase("duplicate-property")]
    [TestCase("mixed-capture")]
    [TestCase("root")]
    [TestCase("missing-later-body")]
    [TestCase("later-hash")]
    [TestCase("previous-version")]
    public void DetachedCodecRejectsMalformedFaultBodyEvidenceWithoutPublishingAPartialProjection(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, "int quotient=n/(n-1);", "var values=new int[1];");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(target.ForkJoinProjection!, target.TotalEntry!, cancellation))!.AsObject();
        var shapes = json["carriers"]!["shapes"]!.AsObject();
        var capture = shapes["faultBodies"]!.AsObject();
        var value = capture["value"]!.AsObject();
        switch (mutation)
        {
            case "missing-property":
                shapes.Remove("faultBodies");
                break;
            case "unknown-property":
                value["extra"] = true;
                break;
            case "mixed-capture":
                capture["unknown"] = "RejectedLaterEmittedBody";
                break;
            case "root":
                value["root"] = "foreign";
                break;
            case "missing-later-body":
                value["bodies"]!.AsArray().RemoveAt(1);
                break;
            case "later-hash":
                value["bodies"]![1]!["sha256"] = new string('a', 64);
                break;
            case "previous-version":
                json["version"] = CompilerForkJoinProjectionCodec.Version - 1;
                break;
            case "duplicate-property":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var text = json.ToJsonString();
        if (mutation == "duplicate-property")
        { text = text.Replace("\"faultBodies\":", "\"faultBodies\":null,\"faultBodies\":", StringComparison.Ordinal); }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(Encoding.UTF8.GetBytes(text), target.TotalEntry!, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void CancellationDoesNotTurnAnEmittedInventoryIntoEmptySuccess()
    {
        var target = Prepare(OptimizationLevel.Release, false, "var values=new int[1];");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action scan = () => { _ = CompilerTaskPrimitiveIlScanner.Scan(Inventory(target).Bodies.Single().Code, canceled.Token); };
        Action check = () => { _ = NativeTaskEmittedFaultSiteVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, Layout(), canceled.Token); };
        Assert.Throws<OperationCanceledException>(scan);
        Assert.Throws<OperationCanceledException>(check);
    }

    private static CompilerCallablePreparation Prepare(OptimizationLevel optimization, bool configured, string first, string? second = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        var source = "using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]public static " + (configured ? "async " : "") +
            "Task Target(int n)=>" + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>" +
            (second is null ? "Child" : "Other") + "(3)))" + (configured ? ".ConfigureAwait(false)" : "") +
            ";static void Child(int n){" + first + "}" + (second is null ? "" : "static void Other(int n){" + second + "}") + "}";
        var initial = TestCompilation.Create("EmittedFaultSites", [("Subject.cs", source)]);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        var target = preparations.Single();
        Assert.That(target.ForkJoinProjection, Is.Not.Null, target.ResourceUnknownReason);
        Assert.That(target.TotalEntry, Is.Not.Null);
        Assert.That(target.ForkJoinProjection!.Carriers!.Shapes, Is.Not.Null);
        return target;
    }

    private static CompilerTaskEmittedFaultBodyInventory Inventory(CompilerCallablePreparation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var capture = target.ForkJoinProjection!.Carriers!.Shapes!.EmittedFaultBodies!;
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Inventory, Is.Not.Null);
        return capture.Inventory!;
    }

    private static NativeTaskEmittedFaultSiteCheck Check(CompilerCallablePreparation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return NativeTaskEmittedFaultSiteVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, Layout(),
            TestContext.CurrentContext.CancellationToken);
    }

    private static WorkerClaimResult VerifyRoot(CompilerCallablePreparation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);
    }

    private static void AssertUnknown(WorkerClaimResult result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    private static CompilerCallablePreparation WithShapes(CompilerCallablePreparation target, CompilerTaskCarrierShapes shapes)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(shapes);
        var projection = target.ForkJoinProjection!;
        return target with { ForkJoinProjection = projection with { Carriers = projection.Carriers! with { Shapes = shapes } } };
    }

    private static CompilerCallablePreparation WithInventory(CompilerCallablePreparation target, CompilerTaskEmittedFaultBodyInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inventory);
        return WithShapes(target, target.ForkJoinProjection!.Carriers!.Shapes! with { EmittedFaultBodies = new(inventory, null) });
    }

    private static CompilerTaskEmittedFaultBody WithCode(CompilerTaskEmittedFaultBody body, ImmutableArray<byte> code)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (code.IsDefault)
        { throw new ArgumentNullException(nameof(code)); }
        return body with { Code = code, CodeSha256 = WorkerProtocolJson.ComputeSha256(code) };
    }

    private static CompilerTaskEmittedFaultBody ReplaceArrayToken(CompilerTaskEmittedFaultBody body,
        CompilerTaskEmittedArrayTypeOperand fact, int token)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(fact);
        var bytes = body.Code.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(fact.IlOffset + 1, 4), token);
        return WithCode(body, [.. bytes]) with { ArrayTypeOperands = [fact with { TypeToken = token }] };
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }

    private static ImmutableArray<byte> CompileProbe(OptimizationLevel optimization, string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var initial = TestCompilation.Create("PrimitiveFaultProbe", "public static class ProbeOwner{" + declaration + "}");
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        using var image = new PEReader(new MemoryStream(stream.ToArray(), false));
        var metadata = image.GetMetadataReader();
        var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .Single(value => metadata.GetString(value.Name) == "Probe");
        return [.. image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!];
    }

    private sealed record IndependentInstruction(int Offset, int OpCode, int NextOffset, int TargetOffset);

    // Reflection.Emit's ECMA operand metadata supplies an independent decoder;
    // this does not call either production scanner or the collector's decoder.
    private static ImmutableArray<IndependentInstruction> DecodeIndependently(ImmutableArray<byte> code)
    {
        if (code.IsDefault)
        { throw new ArgumentNullException(nameof(code)); }
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType == typeof(OpCode))
            .Select(static field => field is null ? throw new ArgumentNullException(nameof(field)) : (OpCode)field.GetValue(null)!)
            .ToDictionary(static opcode /* SPINT001: all values valid - all opcode values, including default nop, can be inspected */ => unchecked((ushort)opcode.Value));
        var instructions = ImmutableArray.CreateBuilder<IndependentInstruction>();
        var offset = 0;
        while (offset < code.Length)
        {
            var start = offset;
            var value = (int)code[offset++];
            if (value == 0xfe)
            { value = (value << 8) | code[offset++]; }
            var opcode = opcodes[(ushort)value];
            var width = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineI or OperandType.InlineType or OperandType.InlineMethod or
                    OperandType.InlineField or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineSig or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                _ => throw new InvalidDataException("The emitted fixture uses an unhandled operand encoding.")
            };
            var next = offset + width;
            var target = opcode.OperandType == OperandType.ShortInlineBrTarget ? next + unchecked((sbyte)code[offset]) :
                opcode.OperandType == OperandType.InlineBrTarget ? next + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(offset, 4)) : -1;
            instructions.Add(new(start, value, next, target));
            offset = next;
        }
        Assert.That(offset, Is.EqualTo(code.Length));
        return instructions.ToImmutable();
    }

    private static CompilerTaskPrimitiveFaultKind ExpectedFaults(int opcode)
    {
        return opcode switch
        {
            0xd6 or 0xd7 or 0xd8 or 0xd9 or 0xda or 0xdb or 0x82 or 0x83 or 0x84 or 0x86 or 0x87 or 0x88 or
                0xb3 or 0xb4 or 0xb5 or 0xb6 or 0xb7 or 0xb8 => CompilerTaskPrimitiveFaultKind.Overflow,
            0x5b or 0x5d => CompilerTaskPrimitiveFaultKind.DivideByZero | CompilerTaskPrimitiveFaultKind.Overflow,
            0x5c or 0x5e => CompilerTaskPrimitiveFaultKind.DivideByZero,
            0x8d => CompilerTaskPrimitiveFaultKind.NewArray,
            0x8e => CompilerTaskPrimitiveFaultKind.NullReference,
            0x94 or 0x9e => CompilerTaskPrimitiveFaultKind.NullReference | CompilerTaskPrimitiveFaultKind.RangeCheck,
            _ => CompilerTaskPrimitiveFaultKind.None
        };
    }
}
